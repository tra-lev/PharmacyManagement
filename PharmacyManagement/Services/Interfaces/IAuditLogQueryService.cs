using PharmacyManagement.Models.ViewModels.Admin;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>Xem nhật ký hoạt động (SYS-09, DEC-48) — chỉ đọc; không có sửa hay xóa.</summary>
public interface IAuditLogQueryService
{
    Task<AuditLogListViewModel> GetListAsync(AuditLogFilter filter, CancellationToken ct = default);

    Task<AuditLogDetailsViewModel?> GetDetailsAsync(long auditLogId, CancellationToken ct = default);
}
