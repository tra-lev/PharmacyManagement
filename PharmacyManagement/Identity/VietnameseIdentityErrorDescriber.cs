using Microsoft.AspNetCore.Identity;

namespace PharmacyManagement.Identity;

/// <summary>Thông báo lỗi Identity bằng tiếng Việt.</summary>
public sealed class VietnameseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Đã có lỗi xảy ra, vui lòng thử lại.");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Dữ liệu tài khoản vừa bị thay đổi ở nơi khác, vui lòng tải lại trang.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Mật khẩu không đúng.");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "Mã xác nhận không hợp lệ hoặc đã hết hạn.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), $"Tên tài khoản \"{userName}\" (phần trước @ của email) chỉ được gồm chữ cái không dấu, chữ số và các dấu . _ - +. Dùng email khác.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "Email không hợp lệ.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), $"Tên tài khoản \"{userName}\" (phần trước @ của email) đã thuộc một tài khoản khác. Dùng email khác.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Email này đã được dùng cho một tài khoản khác.");
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "Tài khoản đã có mật khẩu.");
    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "Tài khoản không hỗ trợ khóa.");
    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), "Tài khoản đã có vai trò này.");
    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), "Tài khoản không có vai trò này.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"Mật khẩu phải có ít nhất {length} ký tự.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Mật khẩu phải có ít nhất một ký tự đặc biệt.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Mật khẩu phải có ít nhất một chữ số.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Mật khẩu phải có ít nhất một chữ thường.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Mật khẩu phải có ít nhất một chữ hoa.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"Mật khẩu phải có ít nhất {uniqueChars} ký tự khác nhau.");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
