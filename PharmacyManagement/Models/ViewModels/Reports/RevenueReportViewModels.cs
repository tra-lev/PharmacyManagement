using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Reports;

/// <summary>Bộ lọc báo cáo doanh thu, lợi nhuận (query string). Ngày dạng yyyy-MM-dd (ô type="date").</summary>
public sealed class RevenueFilter
{
    /// <summary>Khoảng tối đa xem theo ngày; dài hơn thì tự chuyển sang theo tháng.</summary>
    public const int MaxDailyDays = 92;

    /// <summary>Khoảng tối đa một lần xem.</summary>
    public const int MaxDays = 1100;

    public string? From { get; set; }
    public string? To { get; set; }

    /// <summary>"day" hoặc "month".</summary>
    public string? By { get; set; }

    /// <summary>Lọc theo người bán (hồ sơ nhân viên). Trang "Doanh số của tôi" luôn là chính người xem.</summary>
    public int? EmployeeId { get; set; }
}

/// <summary>Một mốc thời gian (ngày hoặc tháng) trên bảng và biểu đồ.</summary>
public sealed class RevenueBucket
{
    public DateOnly Start { get; init; }
    public string Label { get; init; } = string.Empty;
    public string ShortLabel { get; init; } = string.Empty;
    public int Invoices { get; set; }
    public decimal Revenue { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetRevenue => Revenue - Refunds;
    public decimal Cost { get; set; }
    public decimal RestockedCost { get; set; }
    public decimal GrossProfit => NetRevenue - Cost + RestockedCost;
}

public sealed record RevenueMedicineRow(int MedicineId, string Code, string Name, string BaseUnit, decimal Quantity, decimal Revenue, decimal Cost)
{
    public decimal Profit => Revenue - Cost;
}

public sealed record RevenueEmployeeRow(int EmployeeId, string Name, int Invoices, decimal Revenue, decimal Refunds, decimal Cost)
{
    public decimal NetRevenue => Revenue - Refunds;
}

public sealed record RevenueMethodRow(byte Method, string Name, int Count, decimal Amount);

/// <summary>Tổn thất hàng tồn kho trong kỳ, theo giá vốn lô (DEC-46).</summary>
public sealed class InventoryLossSummary
{
    /// <summary>Hàng hủy (sổ kho loại 4).</summary>
    public decimal Disposal { get; init; }

    /// <summary>Hàng thu hồi đã gom khỏi kệ (loại 5).</summary>
    public decimal Recall { get; init; }

    /// <summary>Kiểm kê: thiếu trừ thừa (loại 6); âm = thừa nhiều hơn thiếu.</summary>
    public decimal Stocktake { get; init; }

    /// <summary>Trả nhà cung cấp: giá vốn hàng trả − số tiền nhà cung cấp phải hoàn (loại 3).</summary>
    public decimal SupplierReturn { get; init; }

    public decimal Total => Disposal + Recall + Stocktake + SupplierReturn;
}

public sealed class RevenueReportViewModel
{
    public RevenueFilter Filter { get; init; } = new();
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public bool Monthly { get; init; }

    /// <summary>Đã tự đổi sang theo tháng vì khoảng quá dài.</summary>
    public bool ForcedMonthly { get; init; }

    public string? FilterError { get; init; }
    public string PharmacyName { get; init; } = string.Empty;
    public string? EmployeeName { get; init; }

    // ---- Tổng ----
    public int Invoices { get; init; }
    public decimal Goods { get; init; }
    public decimal Discount { get; init; }
    public decimal Revenue { get; init; }
    public int RefundCount { get; init; }
    public decimal Refunds { get; init; }
    public decimal NetRevenue => Revenue - Refunds;
    public decimal Cost { get; init; }
    public decimal RestockedCost { get; init; }
    public decimal GrossProfit => NetRevenue - Cost + RestockedCost;
    public decimal? GrossMargin => NetRevenue == 0 ? null : Math.Round(GrossProfit / NetRevenue * 100, 1);

    /// <summary>Chỉ có khi xem toàn nhà thuốc (không lọc người bán).</summary>
    public InventoryLossSummary? Losses { get; init; }

    public decimal? ProfitAfterLosses => Losses is null ? null : GrossProfit - Losses.Total;

    /// <summary>Giá trị (giá vốn) các lô đã hết hạn còn trong kho, chưa hủy — tại thời điểm xem.</summary>
    public decimal ExpiredStockValue { get; init; }
    public int ExpiredLots { get; init; }

    public IReadOnlyList<RevenueBucket> Buckets { get; init; } = Array.Empty<RevenueBucket>();
    public IReadOnlyList<RevenueMedicineRow> TopMedicines { get; init; } = Array.Empty<RevenueMedicineRow>();
    public IReadOnlyList<RevenueEmployeeRow> Employees { get; init; } = Array.Empty<RevenueEmployeeRow>();
    public IReadOnlyList<RevenueMethodRow> Methods { get; init; } = Array.Empty<RevenueMethodRow>();

    /// <summary>Người bán để lọc.</summary>
    public IReadOnlyList<LookupItem> Sellers { get; init; } = Array.Empty<LookupItem>();

    // ---- Quyền (controller điền) ----

    /// <summary>Xem giá vốn, lợi nhuận, tổn thất (F4.1.2).</summary>
    public bool ShowProfit { get; set; }

    /// <summary>Xem theo nhân viên, lọc người bán (toàn nhà thuốc).</summary>
    public bool ShowEmployees { get; set; }

    /// <summary>Trang "Doanh số của tôi".</summary>
    public bool IsMine { get; set; }
}

/// <summary>Mô hình partial biểu đồ cột SVG.</summary>
public sealed record RevenueChart(IReadOnlyList<RevenueBucket> Buckets, bool ShowProfit);
