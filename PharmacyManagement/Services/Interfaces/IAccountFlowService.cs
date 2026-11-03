using Microsoft.AspNetCore.Identity;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Account;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>Các luồng người dùng tự làm: kích hoạt, quên mật khẩu, đăng nhập lần đầu, xác minh email, đổi mật khẩu.</summary>
public interface IAccountFlowService
{
    /// <summary>Kích hoạt bước 1: đúng mã thì trả về phiếu (ticket) dùng cho bước 2, hiệu lực 15 phút.</summary>
    Task<OperationResult<string>> VerifyActivationCodeAsync(string email, string code);

    Task<ActivatePasswordViewModel?> GetActivationInfoAsync(string ticket);

    /// <summary>Kích hoạt bước 2: nhân viên tự đặt mật khẩu; email được xác minh.</summary>
    Task<OperationResult> CompleteActivationAsync(string ticket, string password);

    /// <summary>Gửi mã đặt lại mật khẩu nếu email thuộc tài khoản đang hoạt động. Không tiết lộ email có tồn tại hay không.</summary>
    Task SendPasswordResetAsync(string email, string baseUrl);

    Task<OperationResult> ResetPasswordAsync(ResetPasswordViewModel model);

    /// <summary>Owner đăng nhập lần đầu: đổi mật khẩu tạm, gỡ cờ bắt đổi mật khẩu.</summary>
    Task<OperationResult> CompleteFirstLoginAsync(IdentityUser user, string newPassword);

    Task<OperationResult<DateTimeOffset>> SendEmailVerificationAsync(IdentityUser user);

    Task<OperationResult> ConfirmEmailAsync(IdentityUser user, string code);

    /// <summary>Nhân viên xác nhận email mới do Owner đổi.</summary>
    Task<OperationResult> ConfirmEmailChangeAsync(string newEmail, string code);

    Task<OperationResult> ChangePasswordAsync(IdentityUser user, ChangePasswordViewModel model);

    /// <summary>Họ tên trong hồ sơ nhân viên gắn với tài khoản; null nếu tài khoản chưa có hồ sơ.</summary>
    Task<string?> GetFullNameAsync(string userId);

    /// <summary>
    /// Owner khai báo / sửa họ và tên đầy đủ của chính mình (Employees.FullName). Chỉ Owner; không đổi tên tài khoản, email.
    /// Chưa có hồ sơ nhân viên thì gắn hồ sơ chưa có tài khoản cùng email, không có mới tạo — không sinh hồ sơ trùng.
    /// </summary>
    Task<OperationResult> UpdateOwnerFullNameAsync(IdentityUser user, string? fullName);
}
