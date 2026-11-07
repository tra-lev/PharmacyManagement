using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Admin;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Danh sách vai trò (1.3.2) và phân quyền cho vai trò (1.3.3) — DEC-47. Ba vai trò cố định; vai trò Owner luôn toàn quyền, không sửa.
/// Khóa cứng giữ nguyên: Dược sĩ không có quyền làm thay đổi tồn kho (SYS-13), quản trị tài khoản chỉ Owner (DEC-18).
/// </summary>
public interface IRoleAdminService
{
    Task<RoleListViewModel> GetRolesAsync(CancellationToken ct = default);

    /// <summary>null nếu tên vai trò không hợp lệ.</summary>
    Task<RolePermissionsViewModel?> GetPermissionsAsync(string role, CancellationToken ct = default);

    /// <param name="grantedIds">PermissionId được bật; quyền bị khóa cứng luôn bị bỏ qua.</param>
    Task<OperationResult> SaveAsync(string role, IReadOnlyCollection<int> grantedIds, string actorUserId, CancellationToken ct = default);
}
