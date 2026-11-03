namespace PharmacyManagement.Configuration;

/// <summary>
/// Khởi tạo tài khoản Owner đầu tiên — CHỈ chạy ở môi trường Development (DEC-18).
/// Giá trị đặt bằng User Secrets, KHÔNG ghi vào mã nguồn, appsettings.json hay script SQL:
///   dotnet user-secrets set "Bootstrap:OwnerEmail" "email-chu-nha-thuoc@gmail.com" --project PharmacyManagement
///   dotnet user-secrets set "Bootstrap:OwnerInitialPassword" "mat-khau-tam" --project PharmacyManagement
///   dotnet user-secrets set "Bootstrap:OwnerEmployeeCode" "NV01" --project PharmacyManagement   (tùy chọn)
///   dotnet user-secrets set "Bootstrap:OwnerFullName" "Họ và tên thật" --project PharmacyManagement   (bắt buộc nếu không có OwnerEmployeeCode)
/// Tên tài khoản tự sinh = phần trước @ của OwnerEmail. Không bao giờ dùng tên vai trò "Chủ nhà thuốc" làm họ tên.
/// Mật khẩu tạm được băm bằng Identity; tài khoản bị bắt đổi mật khẩu và xác minh email ở lần đăng nhập đầu.
/// Sau khi tạo, mốc PharmacySettings.OwnerSetupCompletedAt đóng cơ chế này vĩnh viễn.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? OwnerEmail { get; set; }
    public string? OwnerInitialPassword { get; set; }
    public string? OwnerEmployeeCode { get; set; }
    public string? OwnerFullName { get; set; }
}
