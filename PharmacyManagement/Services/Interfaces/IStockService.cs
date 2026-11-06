using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Tồn kho &amp; lô (DEC-39) — chỉ đọc D1, D2, D4, trừ đổi vị trí lô (2.2.4). Phục vụ 2.5.1 cảnh báo cận hạn, 2.5.2 cảnh báo dưới định mức
/// (tác vụ Hệ thống), 3.1.1 tra cứu thuốc (Pharmacist: tồn, lô, vị trí) và 2.3.3.2 tra cứu lô đang tồn, đã bán (Manager).
/// Tồn khả dụng = lô Khả dụng, HSD sau hôm nay (cùng điều kiện xuất bán). Cận hạn: HSD trong N ngày,
/// N = InventoryPolicies.ExpiryWarningDays của thuốc + kho, trống thì PharmacySettings.ExpiryWarningDays (CHANGELOG_DB G-02).
/// </summary>
public interface IStockService
{
    Task<StockListViewModel> GetMedicineListAsync(StockFilter filter, CancellationToken ct = default);

    Task<BatchListViewModel> GetBatchListAsync(BatchFilter filter, CancellationToken ct = default);

    /// <param name="includeHistory">Có lấy sổ kho của thuốc (chỉ người có quyền kho).</param>
    Task<StockMedicineViewModel?> GetMedicineAsync(int medicineId, bool includeHistory, int historyPage, CancellationToken ct = default);

    /// <param name="includeSales">Có lấy danh sách hóa đơn đã xuất từ lô (quyền 2.3.3.2).</param>
    Task<StockBatchViewModel?> GetBatchAsync(int batchId, bool includeSales, CancellationToken ct = default);

    /// <summary>Số thuốc dưới mức tối thiểu, số lô cận hạn / hết hạn còn tồn / khóa hoặc thu hồi.</summary>
    Task<StockAlertCounts> GetAlertCountsAsync(CancellationToken ct = default);

    /// <summary>2.2.4 xếp thuốc vào vị trí: chuyển lô sang kệ khác cùng kho; không đổi số tồn, ghi AuditLogs BATCH_MOVE.</summary>
    Task<OperationResult> MoveBatchAsync(int batchId, int? locationId, string actorUserId, CancellationToken ct = default);
}
