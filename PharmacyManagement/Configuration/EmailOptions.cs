namespace PharmacyManagement.Configuration;

/// <summary>
/// Cấu hình gửi email qua Gmail SMTP (DEC-18). SenderAddress và AppPassword là bí mật:
/// đặt bằng User Secrets hoặc biến môi trường, KHÔNG ghi vào appsettings.json hay Git.
///   dotnet user-secrets set "Email:SenderAddress" "tai-khoan-gui@gmail.com" --project PharmacyManagement
///   dotnet user-secrets set "Email:AppPassword" "xxxx xxxx xxxx xxxx" --project PharmacyManagement
/// Biến môi trường tương ứng: Email__SenderAddress, Email__AppPassword.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 15;
    /// <summary>Tên người gửi hiện trong hộp thư. Để trống thì dùng Pharmacy:Name.</summary>
    public string? SenderName { get; set; }
    public string? SenderAddress { get; set; }
    public string? AppPassword { get; set; }

    /// <summary>Địa chỉ gốc của ứng dụng để ghi vào email, ví dụ http://192.168.1.10:5000. Trống thì lấy theo request.</summary>
    public string? PublicBaseUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SenderAddress) && !string.IsNullOrWhiteSpace(AppPassword);
}
