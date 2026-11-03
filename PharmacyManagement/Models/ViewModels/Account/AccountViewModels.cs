using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Models.Validation;

namespace PharmacyManagement.Models.ViewModels.Account;

public sealed class LoginViewModel
{
    /// <summary>Email đầy đủ hoặc tên tài khoản (phần trước @).</summary>
    [Required(ErrorMessage = "Nhập email hoặc tên tài khoản.")]
    [StringLength(256)]
    [Display(Name = "Email hoặc tên tài khoản")]
    public string UserNameOrEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Nhập email đăng nhập.")]
    [EmailAddress(ErrorMessage = "Email chưa đúng dạng.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required(ErrorMessage = "Nhập email đăng nhập.")]
    [EmailAddress(ErrorMessage = "Email chưa đúng dạng.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mã 6 số trong email.")]
    [RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "Mã gồm đúng 6 chữ số.")]
    [Display(Name = "Mã xác minh")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mật khẩu mới.")]
    [StringLength(PasswordRules.MaxLength, ErrorMessage = "Mật khẩu tối đa 100 ký tự.")]
    [PasswordPolicy]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu mới")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập lại mật khẩu mới.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Hai mật khẩu chưa khớp.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nhập lại mật khẩu mới")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>Kích hoạt bước 1: nhập email và mã mời.</summary>
public sealed class ActivateCodeViewModel
{
    [Required(ErrorMessage = "Nhập email được mời.")]
    [EmailAddress(ErrorMessage = "Email chưa đúng dạng.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mã 6 số trong email.")]
    [RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "Mã gồm đúng 6 chữ số.")]
    [Display(Name = "Mã xác minh")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Kích hoạt bước 2: nhân viên tự đặt mật khẩu.</summary>
public sealed class ActivatePasswordViewModel
{
    [Required]
    public string Ticket { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string RoleDisplay { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mật khẩu mới.")]
    [StringLength(PasswordRules.MaxLength, ErrorMessage = "Mật khẩu tối đa 100 ký tự.")]
    [PasswordPolicy]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu mới")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập lại mật khẩu mới.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Hai mật khẩu chưa khớp.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nhập lại mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>Owner đăng nhập lần đầu bằng mật khẩu tạm: khai báo họ tên đầy đủ và bắt buộc đặt mật khẩu mới.</summary>
public sealed class FirstLoginViewModel
{
    /// <summary>Tên tài khoản (chỉ hiển thị).</summary>
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập họ và tên đầy đủ.")]
    [StringLength(150, ErrorMessage = "Họ tên tối đa 150 ký tự.")]
    [Display(Name = "Họ và tên đầy đủ")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mật khẩu mới.")]
    [StringLength(PasswordRules.MaxLength, ErrorMessage = "Mật khẩu tối đa 100 ký tự.")]
    [PasswordPolicy]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu mới")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập lại mật khẩu mới.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Hai mật khẩu chưa khớp.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nhập lại mật khẩu mới")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class VerifyEmailViewModel
{
    public string Email { get; set; } = string.Empty;
    public bool CodeSent { get; set; }
    public string? ExpiresAtText { get; set; }
    public bool EmailConfigured { get; set; }

    [RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "Mã gồm đúng 6 chữ số.")]
    [Display(Name = "Mã xác minh")]
    public string? Code { get; set; }
}

public sealed class ConfirmEmailChangeViewModel
{
    [Required(ErrorMessage = "Nhập email mới.")]
    [EmailAddress(ErrorMessage = "Email chưa đúng dạng.")]
    [Display(Name = "Email mới")]
    public string NewEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mã 6 số trong email.")]
    [RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "Mã gồm đúng 6 chữ số.")]
    [Display(Name = "Mã xác minh")]
    public string Code { get; set; } = string.Empty;
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Nhập mật khẩu hiện tại.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu hiện tại")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mật khẩu mới.")]
    [StringLength(PasswordRules.MaxLength, ErrorMessage = "Mật khẩu tối đa 100 ký tự.")]
    [PasswordPolicy]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu mới")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập lại mật khẩu mới.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Hai mật khẩu chưa khớp.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nhập lại mật khẩu mới")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>Owner sửa họ và tên đầy đủ (trang Tài khoản của tôi).</summary>
public sealed class EditProfileViewModel
{
    [Required(ErrorMessage = "Nhập họ và tên đầy đủ.")]
    [StringLength(150, ErrorMessage = "Họ tên tối đa 150 ký tự.")]
    [Display(Name = "Họ và tên đầy đủ")]
    public string FullName { get; set; } = string.Empty;
}

/// <summary>Tài khoản của tôi — ba thông tin độc lập: tên tài khoản, email, họ tên (DEC-21).</summary>
public sealed class ManageViewModel
{
    /// <summary>Tên tài khoản = phần trước @ của email.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Email đăng nhập đầy đủ.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Họ và tên trong hồ sơ nhân viên (hóa đơn, báo cáo, chứng từ, GPP); null nếu chưa có hồ sơ.</summary>
    public string? FullName { get; set; }

    /// <summary>Họ tên đang trống hoặc không phải họ tên thật (ví dụ đang là tên vai trò "Chủ nhà thuốc").</summary>
    public bool FullNameNeedsUpdate { get; set; }

    public string RoleDisplay { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public bool IsOwner { get; set; }

    /// <summary>Đang mở khung sửa họ tên.</summary>
    public bool EditingProfile { get; set; }
    public EditProfileViewModel Profile { get; set; } = new();
    public ChangePasswordViewModel ChangePassword { get; set; } = new();
}
