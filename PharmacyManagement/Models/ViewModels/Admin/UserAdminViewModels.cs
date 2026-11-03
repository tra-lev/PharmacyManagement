using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Authorization;

namespace PharmacyManagement.Models.ViewModels.Admin;

/// <summary>Trạng thái tài khoản hiển thị ở danh sách (suy ra từ AspNetUsers, token mã mời, nhật ký gửi email).</summary>
public enum AccountState
{
    NoAccount,
    FormerEmployee,
    Owner,
    OwnerUnverified,
    Active,
    TemporarilyLocked,
    Locked,
    Pending,
    PendingExpired,
    SendFailed
}

public sealed class UserRowViewModel
{
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }

    /// <summary>Tên tài khoản (phần trước @); null khi nhân viên chưa có tài khoản.</summary>
    public string? UserName { get; init; }
    public string? RoleName { get; init; }
    public AccountState State { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string? PendingNewEmail { get; init; }

    public bool CanInvite => State == AccountState.NoAccount;
    public bool CanResend => State is AccountState.Pending or AccountState.PendingExpired or AccountState.SendFailed;
    public bool CanCancelInvite => CanResend;
    public bool CanChangeEmail => State is AccountState.Active or AccountState.TemporarilyLocked;
    public bool CanLock => State is AccountState.Active or AccountState.TemporarilyLocked;
    public bool CanUnlock => State is AccountState.Locked or AccountState.TemporarilyLocked;

    /// <summary>Có tài khoản: chữ viết tắt từ tên tài khoản (giống góc trên). Chưa có tài khoản: từ họ tên.</summary>
    public string Initials
    {
        get
        {
            if (!string.IsNullOrEmpty(UserName)) return PharmacyManagement.Helpers.AccountHelpers.Initials(UserName);
            var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            var first = parts.Length > 1 ? parts[^2][0] : parts[0][0];
            return string.Concat(char.ToUpperInvariant(first), char.ToUpperInvariant(parts[^1][0]));
        }
    }
}

public sealed record EmployeeOptionViewModel(int EmployeeId, string Label, string? Email);

public sealed class InviteUserViewModel
{
    /// <summary>true: tạo hồ sơ nhân viên mới; false: chọn nhân viên đã có.</summary>
    public bool NewEmployee { get; set; }

    [Display(Name = "Nhân viên")]
    public int? EmployeeId { get; set; }

    [StringLength(150, ErrorMessage = "Họ tên tối đa 150 ký tự.")]
    [Display(Name = "Họ tên")]
    public string? FullName { get; set; }

    [StringLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    [RegularExpression(@"^[0-9+\s.-]*$", ErrorMessage = "Số điện thoại chỉ gồm chữ số.")]
    [Display(Name = "Số điện thoại")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Nhập email của nhân viên.")]
    [EmailAddress(ErrorMessage = "Email chưa đúng dạng, ví dụ ten@gmail.com.")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Chọn vai trò.")]
    [Display(Name = "Vai trò")]
    public string Role { get; set; } = RoleNames.Pharmacist;
}

public sealed class ChangeEmailViewModel
{
    [Required]
    public int EmployeeId { get; set; }

    [Required(ErrorMessage = "Nhập email mới.")]
    [EmailAddress(ErrorMessage = "Email chưa đúng dạng.")]
    [StringLength(256)]
    [Display(Name = "Email mới")]
    public string NewEmail { get; set; } = string.Empty;
}

public sealed class LockUserViewModel
{
    [Required]
    public int EmployeeId { get; set; }

    [Required(ErrorMessage = "Nhập lý do khóa tài khoản.")]
    [StringLength(300, ErrorMessage = "Lý do tối đa 300 ký tự.")]
    [Display(Name = "Lý do khóa")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class UserListViewModel
{
    public IReadOnlyList<UserRowViewModel> Rows { get; init; } = Array.Empty<UserRowViewModel>();
    public IReadOnlyList<EmployeeOptionViewModel> InvitableEmployees { get; init; } = Array.Empty<EmployeeOptionViewModel>();
    public InviteUserViewModel Invite { get; init; } = new();
    public bool EmailConfigured { get; init; }

    /// <summary>Mở sẵn khung mời (khi vừa gửi form lỗi hoặc bấm “Mời” ở một dòng).</summary>
    public bool OpenInvite { get; init; }
}
