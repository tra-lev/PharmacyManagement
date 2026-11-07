using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Reports;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Liên thông Dược quốc gia — mô phỏng (DEC-03, DEC-50). 4.4.1 tổng hợp chứng từ có thuốc liên thông thành gói tin (lưu IntegrationLogs, Chờ gửi);
/// 4.4.2.1 / 4.4.2.2 gửi dữ liệu nhập / bán (tác vụ chạy ngầm mỗi phút); mất mạng thì gói tin nằm chờ, có mạng lại tự gửi (SYS-11);
/// 4.4.2.3 Quản lý xử lý lỗi gửi: sửa dữ liệu rồi gửi lại.
/// </summary>
public interface IDqgSyncService
{
    /// <summary>Một lần chạy: tổng hợp chứng từ mới rồi gửi các gói tin đang chờ (nếu có mạng).</summary>
    Task<DqgRunResult> RunAsync(CancellationToken ct = default);

    Task<IntegrationListViewModel> GetListAsync(IntegrationFilter filter, CancellationToken ct = default);

    Task<IntegrationDetailsViewModel?> GetDetailsAsync(long logId, CancellationToken ct = default);

    /// <summary>Dựng lại gói tin từ dữ liệu hiện tại và gửi lại (chỉ gói tin lỗi).</summary>
    Task<OperationResult> ResendAsync(long logId, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> ResendAllFailedAsync(string actorUserId, CancellationToken ct = default);

    /// <summary>Số gói tin lỗi cần xử lý (số cạnh mục menu).</summary>
    Task<int> FailedCountAsync(CancellationToken ct = default);
}
