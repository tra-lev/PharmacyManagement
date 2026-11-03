using System.Globalization;
using System.Net;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Helpers;

/// <summary>Nội dung email chứa mã 6 số (HTML đơn giản, style nội tuyến để hiện đúng trên Gmail).</summary>
public static class EmailTemplates
{
    /// <summary>Chỗ giữ tên nhà thuốc; SmtpEmailService thay bằng Pharmacy:Name lúc gửi.</summary>
    public const string PharmacyNameToken = "%TEN_NHA_THUOC%";

    private static readonly TimeZoneInfo VietNam = FindVietNamTimeZone();

    public static EmailMessage Invite(string toAddress, string fullName, string ownerName, string roleDisplay, IssuedCode code, string activateUrl)
        => Build(toAddress, fullName,
            subject: $"[{PharmacyNameToken}] Mã kích hoạt tài khoản của bạn: {code.Code}",
            intro: $"Chủ nhà thuốc <b>{E(ownerName)}</b> đã tạo tài khoản <b>{E(roleDisplay)}</b> cho bạn với email này. Nhập mã dưới đây ở trang kích hoạt để tự đặt mật khẩu:",
            code: code, actionUrl: activateUrl, actionText: "Mở trang kích hoạt");

    public static EmailMessage VerifyEmail(string toAddress, string fullName, IssuedCode code)
        => Build(toAddress, fullName,
            subject: $"[{PharmacyNameToken}] Mã xác minh email: {code.Code}",
            intro: "Bạn đang xác minh email cho tài khoản chủ nhà thuốc. Nhập mã dưới đây ở trang xác minh:",
            code: code, actionUrl: null, actionText: null);

    public static EmailMessage ResetPassword(string toAddress, string fullName, IssuedCode code, string resetUrl)
        => Build(toAddress, fullName,
            subject: $"[{PharmacyNameToken}] Mã đặt lại mật khẩu: {code.Code}",
            intro: "Có yêu cầu đặt lại mật khẩu cho tài khoản dùng email này. Nhập mã dưới đây để đặt mật khẩu mới. Nếu không phải bạn yêu cầu, hãy bỏ qua email và báo chủ nhà thuốc.",
            code: code, actionUrl: resetUrl, actionText: "Đặt mật khẩu mới");

    public static EmailMessage ChangeEmail(string toAddress, string fullName, string ownerName, IssuedCode code, string confirmUrl)
        => Build(toAddress, fullName,
            subject: $"[{PharmacyNameToken}] Mã xác nhận email mới: {code.Code}",
            intro: $"Chủ nhà thuốc <b>{E(ownerName)}</b> đã đổi email đăng nhập của bạn sang địa chỉ này. Email mới chỉ có hiệu lực khi bạn nhập mã dưới đây:",
            code: code, actionUrl: confirmUrl, actionText: "Xác nhận email mới");

    private static EmailMessage Build(string toAddress, string fullName, string subject, string intro, IssuedCode code, string? actionUrl, string? actionText)
    {
        var expires = TimeZoneInfo.ConvertTime(code.ExpiresAt, VietNam).ToString("HH:mm, dd/MM/yyyy", CultureInfo.InvariantCulture);
        var spaced = code.Code[..3] + " " + code.Code[3..];
        var action = actionUrl is null
            ? string.Empty
            : $"<p style=\"margin:0 0 16px\"><a href=\"{E(actionUrl)}\" style=\"display:inline-block;padding:12px 22px;border-radius:999px;background:#0B3B3E;color:#ffffff;text-decoration:none;font-weight:600\">{E(actionText ?? "Mở trang")}</a></p>"
              + $"<p style=\"margin:0 0 16px;color:#4E6664;font-size:13px\">Đường dẫn chỉ mở được từ máy tính trong nhà thuốc: {E(actionUrl)}</p>";

        var html = $@"<!doctype html>
<html lang=""vi""><body style=""margin:0;padding:24px;background:#E3EDEB;font-family:Segoe UI,Arial,sans-serif;color:#13302F"">
<div style=""max-width:560px;margin:0 auto;background:#ffffff;border-radius:20px;padding:32px"">
<p style=""margin:0 0 20px;font-size:18px;font-weight:700"">{PharmacyNameToken}</p>
<p style=""margin:0 0 12px"">Chào {E(fullName)},</p>
<p style=""margin:0 0 16px;line-height:1.6"">{intro}</p>
<p style=""margin:0 0 16px;display:inline-block;padding:14px 24px;border-radius:16px;background:#F1F6F5;font-size:30px;letter-spacing:6px;font-weight:700"">{spaced}</p>
<p style=""margin:0 0 16px;color:#4E6664;font-size:14px"">Mã có hiệu lực đến <b style=""color:#13302F"">{expires}</b> và chỉ dùng được một lần. Nhập sai 5 lần thì mã bị hủy.</p>
{action}
<p style=""margin:0 0 16px;padding:12px 16px;border-radius:14px;background:#FCEBD2;color:#8A4B0B;font-size:14px"">Không chia sẻ mã này với bất kỳ ai, kể cả người tự xưng là nhân viên nhà thuốc.</p>
<p style=""margin:0;color:#4E6664;font-size:12px"">Email gửi tự động từ hệ thống quản lý nhà thuốc, vui lòng không trả lời.</p>
</div></body></html>";

        return new EmailMessage(toAddress, fullName, subject, html);
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static TimeZoneInfo FindVietNamTimeZone()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh", "Asia/Bangkok" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }
}
