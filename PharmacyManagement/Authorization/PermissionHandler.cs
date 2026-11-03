using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Authorization;

/// <summary>
/// Kiểm tra quyền phía server. Thứ tự:
/// 1. Khóa cứng SYS-13 (Pharmacist không có quyền kho) và DEC-18 (1.3.x chỉ Owner) — không phụ thuộc cấu hình 1.3.3.
/// 2. Tài khoản đang bị bắt đổi mật khẩu: không có quyền gì.
/// 3. Chức năng quản trị quan trọng: phải xác minh email.
/// 4. Quyền của vai trò trong bảng RolePermissions.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;

    public PermissionHandler(IPermissionService permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true) return;

        var role = user.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrEmpty(role)) return;

        var code = requirement.Code;
        if (role == RoleNames.Pharmacist && PermissionCodes.IsLockedForPharmacist(code)) return;
        if (role != RoleNames.Owner && PermissionCodes.IsOwnerOnly(code)) return;
        if (user.HasClaim(AppClaimTypes.MustChangePassword, "true")) return;
        if (PermissionCodes.RequiresVerifiedEmail(code) && !user.HasClaim(AppClaimTypes.EmailVerified, "true")) return;

        if (await _permissions.RoleHasAsync(role, code))
        {
            context.Succeed(requirement);
        }
    }
}
