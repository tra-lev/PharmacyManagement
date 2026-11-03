using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Admin;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Quản trị tài khoản — chỉ Owner (1.3.1 tạo tài khoản, 1.3.4 gán vai trò, 1.3.5 khóa).
/// Owner duy nhất: không mời, không gán, không khóa vai trò Owner (DEC-18).
/// </summary>
public interface IUserAdminService
{
    Task<UserListViewModel> GetListAsync(InviteUserViewModel? invite = null, bool openInvite = false, CancellationToken ct = default);

    /// <summary>Tạo tài khoản chưa có mật khẩu, gán vai trò, gửi mã 6 số tới email.</summary>
    Task<OperationResult> InviteAsync(InviteUserViewModel model, string actorUserId, string baseUrl, CancellationToken ct = default);

    Task<OperationResult> ResendInviteAsync(int employeeId, string actorUserId, string baseUrl, CancellationToken ct = default);

    Task<OperationResult> CancelInviteAsync(int employeeId, string actorUserId, CancellationToken ct = default);

    /// <summary>Gửi mã tới email mới; email chỉ đổi khi nhân viên nhập đúng mã.</summary>
    Task<OperationResult> ChangeEmailAsync(ChangeEmailViewModel model, string actorUserId, string baseUrl, CancellationToken ct = default);

    Task<OperationResult> LockAsync(LockUserViewModel model, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> UnlockAsync(int employeeId, string actorUserId, CancellationToken ct = default);
}
