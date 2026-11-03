namespace PharmacyManagement.Services.Interfaces;

/// <summary>SYS-09: ghi nhật ký thao tác nhạy cảm vào bảng AuditLogs.</summary>
public interface IAuditLogService
{
    /// <param name="actionType">Mã thao tác, ví dụ ACCOUNT_INVITE, ACCOUNT_LOCK.</param>
    /// <param name="entityName">Bảng/đối tượng bị tác động.</param>
    /// <param name="entityId">Khóa của đối tượng.</param>
    /// <param name="oldValues">Giá trị trước (sẽ chuyển sang JSON), có thể null.</param>
    /// <param name="newValues">Giá trị sau (sẽ chuyển sang JSON), có thể null.</param>
    /// <param name="actorUserId">Người thực hiện; null thì lấy người đang đăng nhập.</param>
    Task WriteAsync(string actionType, string entityName, string? entityId, object? oldValues, object? newValues,
        string? actorUserId = null, CancellationToken ct = default);
}
