using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>
/// Gửi email thật qua Gmail SMTP bằng thư viện có sẵn System.Net.Mail (không cài thêm gói).
/// Mọi lần gửi (thành công/thất bại) ghi IntegrationLogs: IntegrationType = EMAIL, ReferenceType = Employee.
/// Nội dung nhật ký chỉ có mục đích, người nhận, tiêu đề — KHÔNG ghi mã xác minh.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly EmailOptions _options;
    private readonly string _pharmacyName;
    private readonly PharmacyDbContext _db;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, IOptions<PharmacyOptions> pharmacy, PharmacyDbContext db, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _pharmacyName = string.IsNullOrWhiteSpace(pharmacy.Value.Name) ? "Nhà thuốc" : pharmacy.Value.Name.Trim();
        _db = db;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, string purpose, int employeeId, CancellationToken ct = default)
    {
        message = message with
        {
            Subject = message.Subject.Replace(EmailTemplates.PharmacyNameToken, _pharmacyName, StringComparison.Ordinal),
            HtmlBody = message.HtmlBody.Replace(EmailTemplates.PharmacyNameToken, WebUtility.HtmlEncode(_pharmacyName), StringComparison.Ordinal)
        };
        EmailSendResult result;
        string? technical = null;

        if (!_options.IsConfigured)
        {
            result = EmailSendResult.Fail("Chưa cấu hình tài khoản gửi email (Email:SenderAddress, Email:AppPassword trong User Secrets).");
        }
        else
        {
            try
            {
                using var mail = new MailMessage
                {
                    From = new MailAddress(_options.SenderAddress!, string.IsNullOrWhiteSpace(_options.SenderName) ? _pharmacyName : _options.SenderName, Encoding.UTF8),
                    Subject = message.Subject,
                    SubjectEncoding = Encoding.UTF8,
                    Body = message.HtmlBody,
                    BodyEncoding = Encoding.UTF8,
                    IsBodyHtml = true
                };
                mail.To.Add(new MailAddress(message.ToAddress, message.ToName, Encoding.UTF8));

                using var client = new SmtpClient(_options.Host, _options.Port)
                {
                    EnableSsl = _options.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_options.SenderAddress, _options.AppPassword),
                    Timeout = _options.TimeoutSeconds * 1000
                };

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                await client.SendMailAsync(mail, timeout.Token);
                result = EmailSendResult.Ok();
            }
            catch (SmtpFailedRecipientException ex)
            {
                technical = ex.ToString();
                result = EmailSendResult.Fail($"Gmail từ chối địa chỉ nhận {message.ToAddress}. Kiểm tra lại email.");
            }
            catch (SmtpException ex) when (IsAuthenticationError(ex))
            {
                technical = ex.ToString();
                result = EmailSendResult.Fail("Gmail từ chối đăng nhập. Kiểm tra Email:SenderAddress và App Password (tài khoản Gmail phải bật xác minh 2 bước).");
            }
            catch (SmtpException ex) when (ex.InnerException is SocketException or IOException || ex.StatusCode == SmtpStatusCode.GeneralFailure)
            {
                technical = ex.ToString();
                result = EmailSendResult.Fail($"Không kết nối được {_options.Host}. Kiểm tra Internet của máy chủ rồi gửi lại.");
            }
            catch (SmtpException ex)
            {
                technical = ex.ToString();
                result = EmailSendResult.Fail($"Máy chủ email báo lỗi ({(int)ex.StatusCode}). Thử gửi lại sau.");
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                technical = ex.ToString();
                result = EmailSendResult.Fail($"Quá {_options.TimeoutSeconds} giây chưa gửi được tới {_options.Host}. Kiểm tra Internet rồi gửi lại.");
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
            {
                technical = ex.ToString();
                result = EmailSendResult.Fail("Địa chỉ email hoặc cấu hình gửi không hợp lệ.");
            }
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Gửi email {Purpose} tới {To} thất bại: {Error} {Technical}", purpose, message.ToAddress, result.ErrorMessage, technical);
        }

        await LogAsync(message, purpose, employeeId, result, technical, ct);
        return result;
    }

    private static bool IsAuthenticationError(SmtpException ex)
    {
        var code = (int)ex.StatusCode;
        return code is 530 or 534 or 535
            || ex.Message.Contains("5.7.8", StringComparison.Ordinal)
            || ex.Message.Contains("Authentication", StringComparison.OrdinalIgnoreCase);
    }

    private async Task LogAsync(EmailMessage message, string purpose, int employeeId, EmailSendResult result, string? technical, CancellationToken ct)
    {
        var now = DateTime.Now;
        var error = result.ErrorMessage is null ? null : result.ErrorMessage + (technical is null ? "" : " | " + technical);
        var log = new IntegrationLog
        {
            IntegrationType = "EMAIL",
            ReferenceType = "Employee",
            ReferenceId = employeeId,
            // Tiêu đề có kèm mã cho tiện người nhận — che mã trước khi ghi nhật ký.
            RequestPayload = JsonSerializer.Serialize(new { purpose, to = message.ToAddress, subject = Regex.Replace(message.Subject, @"\d{6}", "******") }, JsonOptions),
            ResponsePayload = null,
            Status = (byte)(result.Succeeded ? 1 : 2),
            RetryCount = 0,
            CreatedAt = now,
            LastAttemptAt = now,
            ErrorMessage = error is null ? null : (error.Length <= 2000 ? error : error[..2000])
        };
        _db.IntegrationLogs.Add(log);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Không để lỗi ghi nhật ký che mất kết quả gửi email; gỡ bản ghi lỗi khỏi DbContext.
            _db.Entry(log).State = EntityState.Detached;
            _logger.LogError(ex, "Không ghi được IntegrationLogs cho email {Purpose}", purpose);
        }
    }
}
