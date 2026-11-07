using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Models.ViewModels.Admin;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Nhật ký hoạt động (DEC-48). Chỉ đọc bảng AuditLogs.</summary>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private const string SystemActor = "Hệ thống";
    private const string WholeValue = "Giá trị";

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly StringComparer NameOrder = StringComparer.Create(Vietnamese, ignoreCase: true);

    private readonly PharmacyDbContext _db;

    public AuditLogQueryService(PharmacyDbContext db) => _db = db;

    public async Task<AuditLogListViewModel> GetListAsync(AuditLogFilter filter, CancellationToken ct = default)
    {
        string? error = null;
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        var from = ParseDate(filter.From, ref error);
        var to = ParseDate(filter.To, ref error);
        if (from is not null && to is not null && from > to) (from, to) = (to, from);
        filter.From = from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        filter.To = to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (from is not null)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(a => a.CreatedAt >= start);
        }
        if (to is not null)
        {
            var end = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(a => a.CreatedAt < end);
        }

        if (string.IsNullOrWhiteSpace(filter.User)) filter.User = null;
        else
        {
            var user = filter.User;
            query = query.Where(a => a.UserId == user);
        }

        if (filter.Group is not null && AuditText.Groups.Any(g => g.Key == filter.Group))
        {
            var codes = AuditText.ActionsOf(filter.Group);
            query = query.Where(a => codes.Contains(a.ActionType));
        }
        else filter.Group = null;

        var q = filter.Q?.Trim();
        filter.Q = string.IsNullOrEmpty(q) ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            query = query.Where(a => (a.EntityId != null && a.EntityId.Contains(text))
                                     || (a.NewValues != null && a.NewValues.Contains(text))
                                     || (a.OldValues != null && a.OldValues.Contains(text)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)AuditLogFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var raw = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.AuditLogId)
            .Skip((filter.Page - 1) * AuditLogFilter.PageSize).Take(AuditLogFilter.PageSize)
            .Select(a => new
            {
                a.AuditLogId, a.CreatedAt, a.ActionType, a.EntityName, a.EntityId, a.UserId,
                Employee = _db.Employees.Where(e => e.UserId == a.UserId).Select(e => e.FullName).FirstOrDefault(),
                UserName = _db.Users.Where(u => u.Id == a.UserId).Select(u => u.UserName).FirstOrDefault()
            })
            .ToListAsync(ct);

        var userIds = await _db.AuditLogs.AsNoTracking().Where(a => a.UserId != null).Select(a => a.UserId!).Distinct().ToListAsync(ct);
        var names = await NamesAsync(userIds, ct);

        return new AuditLogListViewModel
        {
            Filter = filter,
            Rows = raw.Select(r => new AuditLogRow(r.AuditLogId, r.CreatedAt, r.ActionType, r.EntityName, r.EntityId,
                Actor(r.UserId, r.Employee, r.UserName))).ToList(),
            Total = total,
            GrandTotal = await _db.AuditLogs.CountAsync(ct),
            PageCount = pageCount,
            Users = names.OrderBy(n => n.Name, NameOrder).ToList(),
            FilterError = error
        };
    }

    public async Task<AuditLogDetailsViewModel?> GetDetailsAsync(long auditLogId, CancellationToken ct = default)
    {
        var a = await _db.AuditLogs.AsNoTracking()
            .Where(x => x.AuditLogId == auditLogId)
            .Select(x => new
            {
                x.AuditLogId, x.CreatedAt, x.ActionType, x.EntityName, x.EntityId, x.UserId, x.OldValues, x.NewValues, x.IpAddress, x.UserAgent,
                Employee = _db.Employees.Where(e => e.UserId == x.UserId).Select(e => e.FullName).FirstOrDefault(),
                UserName = _db.Users.Where(u => u.Id == x.UserId).Select(u => u.UserName).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (a is null) return null;

        var before = Flatten(a.OldValues);
        var after = Flatten(a.NewValues);
        var keys = new List<string>();
        foreach (var k in (before?.Keys ?? Enumerable.Empty<string>()).Concat(after?.Keys ?? Enumerable.Empty<string>()))
            if (!keys.Contains(k)) keys.Add(k);

        return new AuditLogDetailsViewModel
        {
            AuditLogId = a.AuditLogId,
            CreatedAt = a.CreatedAt,
            ActionType = a.ActionType,
            EntityName = a.EntityName,
            EntityId = a.EntityId,
            Actor = Actor(a.UserId, a.Employee, a.UserName),
            IpAddress = a.IpAddress,
            UserAgent = a.UserAgent,
            HasBefore = before is not null,
            HasAfter = after is not null,
            Changes = keys.Select(k => new AuditChange(k == WholeValue ? k : AuditText.Field(k),
                before?.GetValueOrDefault(k), after?.GetValueOrDefault(k))).ToList()
        };
    }

    // ================================================================== Dùng chung

    private async Task<List<AuditUserOption>> NamesAsync(List<string> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new List<AuditUserOption>();
        var employees = await _db.Employees.AsNoTracking().Where(e => e.UserId != null && userIds.Contains(e.UserId))
            .Select(e => new { e.UserId, e.FullName }).ToListAsync(ct);
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync(ct);
        return userIds
            .Select(id => new AuditUserOption(id, Actor(id,
                employees.FirstOrDefault(e => e.UserId == id)?.FullName,
                users.FirstOrDefault(u => u.Id == id)?.UserName)))
            .ToList();
    }

    private static string Actor(string? userId, string? employee, string? userName)
    {
        if (userId is null) return SystemActor;
        if (!string.IsNullOrWhiteSpace(employee)) return userName is null ? employee : employee + " (" + userName + ")";
        return userName ?? "Tài khoản đã xóa";
    }

    /// <summary>JSON đối tượng → từng trường; giá trị lồng nhau giữ nguyên dạng JSON gọn.</summary>
    private static Dictionary<string, string?>? Flatten(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in doc.RootElement.EnumerateObject()) result[p.Name] = Text(p.Value);
            }
            else result[WholeValue] = Text(doc.RootElement);
            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?> { [WholeValue] = json };
        }
    }

    private static string? Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => "Có",
        JsonValueKind.False => "Không",
        _ => e.GetRawText()
    };

    private static DateOnly? ParseDate(string? text, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        error ??= "Ngày không hợp lệ nên đã bỏ qua.";
        return null;
    }
}
