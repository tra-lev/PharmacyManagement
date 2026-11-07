using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Authorization;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.ViewModels.Admin;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Phân quyền vai trò (DEC-47).</summary>
public sealed class RoleAdminService : IRoleAdminService
{
    /// <summary>Description trong bảng Permissions: "Thao tác" (có thêm / sửa) hoặc "Chỉ xem".</summary>
    private const string ActionKind = "Thao tác";

    private readonly PharmacyDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IAuditLogService _audit;

    public RoleAdminService(PharmacyDbContext db, IPermissionService permissions, IAuditLogService audit)
    {
        _db = db;
        _permissions = permissions;
        _audit = audit;
    }

    public static string Describe(string role) => role switch
    {
        RoleNames.Owner => "Toàn quyền. Duy nhất một tài khoản; quản lý tài khoản, phân quyền, chính sách giá, duyệt kiểm kê.",
        RoleNames.Manager => "Danh mục, kho, kiểm tra và hoàn tiền đổi trả, báo cáo; được bán hàng như dược sĩ.",
        RoleNames.Pharmacist => "Bán hàng, bán theo đơn, thu tiền, tiếp nhận đổi trả; chỉ xem tồn kho.",
        _ => string.Empty
    };

    public async Task<RoleListViewModel> GetRolesAsync(CancellationToken ct = default)
    {
        var total = await _db.Permissions.CountAsync(ct);
        var accounts = await _db.UserRoles.AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Count, ct);

        var rows = new List<RoleRow>();
        foreach (var role in RoleNames.All)
        {
            var granted = (await _permissions.GetCodesAsync(role, ct)).Count;
            rows.Add(new RoleRow(role, RoleNames.DisplayName(role), Describe(role),
                accounts.GetValueOrDefault(RoleNames.IdOf(role)!), granted, total, role != RoleNames.Owner));
        }
        return new RoleListViewModel { Roles = rows };
    }

    public async Task<RolePermissionsViewModel?> GetPermissionsAsync(string role, CancellationToken ct = default)
    {
        var roleId = RoleNames.IdOf(role);
        if (roleId is null) return null;

        var permissions = await _db.Permissions.AsNoTracking().OrderBy(p => p.PermissionId).ToListAsync(ct);
        var granted = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId && rp.CanView)
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct);
        var grantedSet = granted.ToHashSet();

        var groups = permissions
            .GroupBy(p => p.ModuleName)
            .OrderBy(g => g.Min(p => p.PermissionId))
            .Select((g, index) => new PermissionGroup("m" + index, PermissionText.Module(g.Key),
                g.Select(p =>
                {
                    var locked = LockedReason(role, p.PermissionCode);
                    return new PermissionItem(p.PermissionId, PermissionText.Name(p.PermissionName), p.Description == ActionKind, p.IsSensitive,
                        role == RoleNames.Owner || (locked is null && grantedSet.Contains(p.PermissionId)), locked);
                }).ToList()))
            .ToList();

        var accounts = await _db.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == roleId, ct);
        return new RolePermissionsViewModel
        {
            Role = role,
            DisplayName = RoleNames.DisplayName(role),
            Description = Describe(role),
            Accounts = accounts,
            Groups = groups,
            Editable = role != RoleNames.Owner
        };
    }

    public async Task<OperationResult> SaveAsync(string role, IReadOnlyCollection<int> grantedIds, string actorUserId, CancellationToken ct = default)
    {
        var roleId = RoleNames.IdOf(role);
        if (roleId is null) return OperationResult.Fail("Không tìm thấy vai trò.");
        if (role == RoleNames.Owner) return OperationResult.Fail("Chủ nhà thuốc luôn có toàn quyền, không sửa được.");

        var wanted = grantedIds.ToHashSet();
        var permissions = await _db.Permissions.AsNoTracking().ToListAsync(ct);
        var unknown = wanted.Except(permissions.Select(p => p.PermissionId)).Any();
        if (unknown) return OperationResult.Fail("Danh sách quyền không hợp lệ. Tải lại trang rồi thử lại.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var rows = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToDictionaryAsync(rp => rp.PermissionId, ct);

        var added = new List<string>();
        var removed = new List<string>();
        foreach (var p in permissions)
        {
            var allow = wanted.Contains(p.PermissionId) && LockedReason(role, p.PermissionCode) is null;
            var isAction = p.Description == ActionKind;
            rows.TryGetValue(p.PermissionId, out var row);
            var had = row?.CanView == true;
            if (allow == had) continue;

            if (row is null)
            {
                row = new RolePermission { RoleId = roleId, PermissionId = p.PermissionId };
                _db.RolePermissions.Add(row);
            }
            row.CanView = allow;
            row.CanCreate = allow && isAction;
            row.CanUpdate = allow && isAction;
            row.CanDelete = false;
            (allow ? added : removed).Add(p.PermissionCode);
        }

        if (added.Count == 0 && removed.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return OperationResult.Ok("Không có thay đổi nào.");
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ROLE_PERMISSION_UPDATE", "RolePermissions", role,
            new { Removed = removed }, new { Added = added }, actorUserId, ct);
        await tx.CommitAsync(ct);
        _permissions.Invalidate(role);

        return OperationResult.Ok($"Đã lưu quyền của vai trò {RoleNames.DisplayName(role)}: bật {added.Count}, tắt {removed.Count} quyền. Áp dụng ngay cho mọi tài khoản thuộc vai trò này.");
    }

    /// <summary>Khóa cứng (SYS-13, DEC-18); null nếu cấp được.</summary>
    public static string? LockedReason(string role, string permissionCode)
    {
        if (role == RoleNames.Pharmacist && PermissionCodes.IsLockedForPharmacist(permissionCode))
            return "Dược sĩ không được làm thay đổi tồn kho.";
        if (role != RoleNames.Owner && PermissionCodes.IsOwnerOnly(permissionCode))
            return "Chỉ chủ nhà thuốc quản lý tài khoản và phân quyền.";
        return null;
    }
}
