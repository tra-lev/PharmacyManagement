namespace PharmacyManagement.Authorization;

/// <summary>Claim riêng của ứng dụng (thêm vào cookie đăng nhập).</summary>
public static class AppClaimTypes
{
    /// <summary>"true" khi email của tài khoản đã xác minh (AspNetUsers.EmailConfirmed).</summary>
    public const string EmailVerified = "pm:email_verified";

    /// <summary>"true" khi tài khoản phải đổi mật khẩu trước khi dùng hệ thống. Lưu trong AspNetUserClaims.</summary>
    public const string MustChangePassword = "pm:must_change_password";

    /// <summary>Employees.EmployeeId của người đăng nhập.</summary>
    public const string EmployeeId = "pm:employee_id";

    /// <summary>Họ tên nhân viên.</summary>
    public const string FullName = "pm:full_name";
}
