namespace PharmacyManagement.Configuration;

/// <summary>
/// Thông tin chung của nhà thuốc (không bí mật) — đặt trong appsettings.json, mục "Pharmacy".
/// Tên hiện ở logo, tiêu đề tab trình duyệt, tên người gửi và nội dung email.
/// </summary>
public sealed class PharmacyOptions
{
    public const string SectionName = "Pharmacy";

    public string Name { get; set; } = "Nhà thuốc";

    /// <summary>Tài khoản nhận chuyển khoản — dùng tạo mã VietQR có sẵn số tiền ở bước thu tiền (3.3.2.2).</summary>
    public BankTransferOptions BankTransfer { get; set; } = new();
}

/// <summary>
/// Tài khoản ngân hàng nhận tiền khách chuyển khoản (không bí mật). Để trống AccountNumber thì màn hình thu tiền không hiện mã QR.
/// BankBin: mã ngân hàng 6 số theo NAPAS (ví dụ 970422 = MB Bank), đọc được từ mã VietQR tĩnh của tài khoản.
/// </summary>
public sealed class BankTransferOptions
{
    public string BankBin { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;

    public bool IsConfigured =>
        BankBin.Length == 6 && BankBin.All(char.IsAsciiDigit)
        && AccountNumber.Length is > 0 and <= 19 && AccountNumber.All(char.IsAsciiDigit);
}
