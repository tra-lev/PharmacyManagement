using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using PharmacyManagement.Data;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class AuditLogService : IAuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly PharmacyDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditLogService(PharmacyDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task WriteAsync(string actionType, string entityName, string? entityId, object? oldValues, object? newValues,
        string? actorUserId = null, CancellationToken ct = default)
    {
        var ctx = _http.HttpContext;
        var userAgent = ctx?.Request.Headers.UserAgent.ToString();
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId ?? ctx?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            ActionType = Truncate(actionType, 50)!,
            EntityName = Truncate(entityName, 100)!,
            EntityId = Truncate(entityId, 100),
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, JsonOptions),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, JsonOptions),
            IpAddress = Truncate(ctx?.Connection.RemoteIpAddress?.ToString(), 45),
            UserAgent = Truncate(string.IsNullOrEmpty(userAgent) ? null : userAgent, 500),
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];
}
