namespace PharmacyManagement.Services.Interfaces;

/// <summary>Một email cần gửi.</summary>
public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string HtmlBody);

/// <summary>Kết quả gửi. ErrorMessage là câu tiếng Việt để hiện cho người dùng.</summary>
public sealed record EmailSendResult(bool Succeeded, string? ErrorMessage)
{
    public static EmailSendResult Ok() => new(true, null);
    public static EmailSendResult Fail(string message) => new(false, message);
}

/// <summary>Gửi email thật qua Gmail SMTP; mỗi lần gửi ghi IntegrationLogs (không ghi mã xác minh).</summary>
public interface IEmailService
{
    /// <summary>Đã có đủ địa chỉ gửi và App Password chưa.</summary>
    bool IsConfigured { get; }

    /// <param name="purpose">Mục đích (invite, reset, verify-email, change-email) — ghi vào nhật ký.</param>
    /// <param name="employeeId">Nhân viên liên quan (ReferenceId của IntegrationLogs), 0 nếu không có.</param>
    Task<EmailSendResult> SendAsync(EmailMessage message, string purpose, int employeeId, CancellationToken ct = default);
}
