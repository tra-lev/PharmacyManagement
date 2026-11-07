using PharmacyManagement.Models.ViewModels.Reports;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Báo cáo nhập – xuất – tồn 4.2.1 (từ sổ kho InventoryTransactions, giá trị theo giá vốn lô) và thuốc cận hạn 4.2.2 — DEC-49. Chỉ đọc.
/// </summary>
public interface IStockReportService
{
    /// <param name="all">true = mọi dòng (bản in), false = theo trang.</param>
    Task<StockMovementViewModel> GetMovementAsync(StockMovementFilter filter, bool all, CancellationToken ct = default);

    Task<ExpiryReportViewModel> GetExpiryAsync(ExpiryFilter filter, CancellationToken ct = default);
}
