using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>
/// Tình trạng một lô khi xem tồn kho (DEC-39) — tính từ số tồn, hạn dùng, trạng thái lô và ngưỡng cận hạn; không lưu CSDL.
/// Chỉ Available và NearExpiry bán được (giống điều kiện xuất bán: lô Khả dụng, HSD sau hôm nay).
/// </summary>
public enum BatchCondition : byte
{
    Available = 1,
    NearExpiry = 2,
    Expired = 3,
    Blocked = 4,
    Empty = 5
}

/// <summary>Chữ và màu nhãn dùng chung cho các trang Tồn kho.</summary>
public static class StockText
{
    public static string Condition(BatchCondition condition, byte status) => condition switch
    {
        BatchCondition.Available => "Khả dụng",
        BatchCondition.NearExpiry => "Cận hạn",
        BatchCondition.Expired => "Hết hạn",
        BatchCondition.Blocked => status switch
        {
            (byte)BatchStatus.Locked => "Bị khóa",
            (byte)BatchStatus.Recalled => "Đang thu hồi",
            (byte)BatchStatus.Discontinued => "Ngừng sử dụng",
            _ => "Không bán được"
        },
        BatchCondition.Empty => "Đã hết hàng",
        _ => "Không rõ"
    };

    /// <summary>Lớp màu pm-badge.</summary>
    public static string Tone(BatchCondition condition) => condition switch
    {
        BatchCondition.Available => "good",
        BatchCondition.NearExpiry => "warn",
        BatchCondition.Expired => "bad",
        BatchCondition.Blocked => "wait",
        _ => "mute"
    };

    /// <summary>"Còn 25 ngày", "Hết hạn hôm nay", "Quá hạn 3 ngày".</summary>
    public static string DaysLeft(int days)
        => days > 0 ? "Còn " + Format.Number(days) + " ngày" : days == 0 ? "Hết hạn hôm nay" : "Quá hạn " + Format.Number(-days) + " ngày";

    public static string Transaction(byte type) => type switch
    {
        (byte)InventoryTransactionType.PurchaseReceipt => "Nhập mua",
        (byte)InventoryTransactionType.SaleIssue => "Xuất bán",
        (byte)InventoryTransactionType.SupplierReturn => "Trả nhà cung cấp",
        (byte)InventoryTransactionType.Disposal => "Hủy thuốc",
        (byte)InventoryTransactionType.Recall => "Thu hồi",
        (byte)InventoryTransactionType.StocktakeAdjustment => "Điều chỉnh kiểm kê",
        (byte)InventoryTransactionType.CustomerReturnRestock => "Khách trả, nhập lại",
        _ => "Khác"
    };

    /// <summary>Số lượng theo đơn vị cơ sở: "1.250 viên".</summary>
    public static string Quantity(decimal quantity, string baseUnit) => Format.Number(quantity) + " " + baseUnit;

    /// <summary>Quy ra đơn vị lớn nhất của thuốc: "= 12 hộp + 50 viên"; null khi không có đơn vị lớn hơn hoặc chưa đủ 1 đơn vị.</summary>
    public static string? Breakdown(decimal quantity, string baseUnit, UnitFactor? largest)
    {
        if (largest is null || largest.Factor <= 1 || quantity < largest.Factor) return null;
        var whole = Math.Floor(quantity / largest.Factor);
        var rest = quantity - whole * largest.Factor;
        return "= " + Format.Number(whole) + " " + largest.UnitName + (rest > 0 ? " + " + Format.Number(rest) + " " + baseUnit : string.Empty);
    }
}

/// <summary>Đơn vị quy đổi (tên + số đơn vị cơ sở).</summary>
public sealed record UnitFactor(string UnitName, decimal Factor);

/// <summary>Số cảnh báo kho (2.5.1 cận hạn, 2.5.2 dưới định mức) — dùng cho trang Tồn kho, menu và trang Tổng quan.</summary>
public sealed record StockAlertCounts(int BelowMinimum, int NearExpiry, int Expired, int Blocked)
{
    public static readonly StockAlertCounts None = new(0, 0, 0, 0);

    /// <summary>Số việc cần xử lý (hiện cạnh mục menu): thuốc dưới mức tối thiểu + lô cận hạn + lô hết hạn còn tồn.</summary>
    public int ActionCount => BelowMinimum + NearExpiry + Expired;
}

/// <summary>Quyền xem chứng từ liên quan — quyết định có hiện liên kết hay chỉ hiện mã.</summary>
public sealed record StockLinks(bool Receipts, bool Invoices, bool Returns, bool Catalog)
{
    public static readonly StockLinks None = new(false, false, false, false);
}

// ------------------------------------------------------------------ Tồn theo thuốc

/// <summary>Bộ lọc trang Tồn kho theo thuốc (query string).</summary>
public sealed class StockFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo tên thuốc, mã thuốc, mã vạch, tên hoạt chất.</summary>
    public string? Q { get; set; }

    public int? CategoryId { get; set; }

    /// <summary>"below" dưới mức tối thiểu, "out" hết hàng, "near" có lô cận hạn, "expired" có lô hết hạn, "blocked" có lô khóa / thu hồi.</summary>
    public string? Status { get; set; }

    /// <summary>"all" = gồm cả thuốc ngừng kinh doanh; trống = chỉ thuốc đang kinh doanh.</summary>
    public string? Show { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || CategoryId is not null || Status is not null || Show is not null;
}

public sealed record StockMedicineRow(
    int MedicineId,
    string Code,
    string Name,
    string? Ingredients,
    string BaseUnit,
    bool IsActive,
    bool IsPrescription,
    decimal Available,
    string? AvailableBreakdown,
    decimal Unsellable,
    int BatchCount,
    DateOnly? NearestExpiry,
    int? NearestDays,
    decimal Minimum,
    decimal OnOrder,
    bool IsBelowMinimum,
    bool HasNearExpiry,
    bool HasExpired,
    bool HasBlocked);

public sealed class StockListViewModel
{
    public StockFilter Filter { get; init; } = new();
    public IReadOnlyList<StockMedicineRow> Rows { get; init; } = Array.Empty<StockMedicineRow>();
    public int Total { get; init; }

    /// <summary>Tổng số thuốc trong danh mục (mọi trạng thái).</summary>
    public int GrandTotal { get; init; }

    public int PageCount { get; init; }
    public IReadOnlyList<LookupItem> Categories { get; init; } = Array.Empty<LookupItem>();
    public StockAlertCounts Alerts { get; init; } = StockAlertCounts.None;
    public int WarningDays { get; init; }

    /// <summary>Quản lý / chủ nhà thuốc (có quyền kho): thấy số đang chờ giao, lô bị khóa. Dược sĩ chỉ tra cứu.</summary>
    public bool FullView { get; set; }
}

// ------------------------------------------------------------------ Tồn theo lô

/// <summary>Bộ lọc trang Tồn kho theo lô (query string).</summary>
public sealed class BatchFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo số lô, tên thuốc, mã thuốc.</summary>
    public string? Q { get; set; }

    /// <summary>"sellable" bán được, "near" cận hạn, "expired" hết hạn, "blocked" khóa / thu hồi.</summary>
    public string? Status { get; set; }

    /// <summary>Tủ hoặc kệ (chọn tủ thì gồm mọi kệ trong tủ).</summary>
    public int? LocationId { get; set; }

    /// <summary>"all" = gồm cả lô đã hết hàng; trống = chỉ lô còn hàng.</summary>
    public string? Show { get; set; }

    /// <summary>Chỉ các lô của một thuốc (mở từ trang chi tiết thuốc).</summary>
    public int? MedicineId { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || Status is not null || LocationId is not null || Show is not null || MedicineId is not null;
}

public sealed record StockBatchRow(
    int BatchId,
    int MedicineId,
    string MedicineCode,
    string MedicineName,
    string BatchNumber,
    DateOnly ExpiryDate,
    int DaysLeft,
    decimal Quantity,
    string BaseUnit,
    string? Breakdown,
    string? Location,
    DateOnly ReceivedDate,
    int? GoodsReceiptId,
    string? GoodsReceiptCode,
    string? SupplierName,
    byte Status,
    BatchCondition Condition);

public sealed class BatchListViewModel
{
    public BatchFilter Filter { get; init; } = new();
    public IReadOnlyList<StockBatchRow> Rows { get; init; } = Array.Empty<StockBatchRow>();
    public int Total { get; init; }

    /// <summary>Tổng số lô trong CSDL (kể cả lô đã hết hàng).</summary>
    public int GrandTotal { get; init; }

    public int PageCount { get; init; }

    /// <summary>Tủ và kệ ("Tủ 1", "Tủ 1 › Kệ A").</summary>
    public IReadOnlyList<LookupItem> Locations { get; init; } = Array.Empty<LookupItem>();

    public StockAlertCounts Alerts { get; init; } = StockAlertCounts.None;
    public int WarningDays { get; init; }

    /// <summary>Tên thuốc khi lọc theo một thuốc.</summary>
    public string? MedicineName { get; init; }

    public bool FullView { get; set; }
    public StockLinks Links { get; set; } = StockLinks.None;
}

// ------------------------------------------------------------------ Chi tiết thuốc (tra cứu 3.1.1)

public sealed record StockIngredient(string Name, string? Strength);

public sealed record StockSaleUnit(string UnitName, decimal Factor, decimal Price, bool IsBase);

public sealed record StockBatchLine(
    int BatchId,
    string BatchNumber,
    DateOnly ExpiryDate,
    int DaysLeft,
    decimal Quantity,
    string? Breakdown,
    string? Location,
    DateOnly ReceivedDate,
    int? GoodsReceiptId,
    string? GoodsReceiptCode,
    decimal UnitCost,
    byte Status,
    BatchCondition Condition,
    bool SellsNext);

/// <summary>Một dòng sổ kho. <see cref="DocumentId"/>: khóa chứng từ (phiếu nhập, hóa đơn, phiếu trả…) để dựng liên kết.</summary>
public sealed record StockLedgerRow(
    DateTime When,
    byte Type,
    int BatchId,
    string BatchNumber,
    decimal Change,
    decimal? Balance,
    string? DocumentCode,
    int? DocumentId,
    string Actor,
    string? Notes);

/// <summary>Mô hình của partial _Ledger (bảng sổ kho dùng chung cho chi tiết thuốc và chi tiết lô).</summary>
public sealed record StockLedgerTable(IReadOnlyList<StockLedgerRow> Rows, StockLinks Links, string BaseUnit, bool ShowBatch, bool ShowBalance);

/// <summary>Mô hình của partial _StockAlerts.</summary>
public sealed record StockAlertPanel(StockAlertCounts Counts, int WarningDays);

public sealed class StockMedicineViewModel
{
    public int MedicineId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Barcode { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string? ManufacturerName { get; init; }
    public string? Strength { get; init; }
    public string? DosageForm { get; init; }
    public string? RegistrationNumber { get; init; }
    public bool IsPrescription { get; init; }
    public bool IsControlledDrug { get; init; }
    public bool IsActive { get; init; }
    public string? UsageInstructions { get; init; }
    public string? Contraindications { get; init; }
    public string? Interactions { get; init; }
    public string BaseUnit { get; init; } = string.Empty;
    public IReadOnlyList<StockIngredient> Ingredients { get; init; } = Array.Empty<StockIngredient>();
    public IReadOnlyList<StockSaleUnit> SaleUnits { get; init; } = Array.Empty<StockSaleUnit>();

    public decimal Available { get; init; }
    public string? AvailableBreakdown { get; init; }
    public decimal Unsellable { get; init; }
    public decimal Minimum { get; init; }
    public decimal OnOrder { get; init; }
    public int WarningDays { get; init; }

    /// <summary>Lô còn hàng, theo thứ tự xuất bán (hạn dùng gần trước).</summary>
    public IReadOnlyList<StockBatchLine> Batches { get; init; } = Array.Empty<StockBatchLine>();

    /// <summary>Số lô đã hết hàng (xem ở trang Theo lô).</summary>
    public int EmptyBatchCount { get; init; }

    public IReadOnlyList<StockLedgerRow> History { get; init; } = Array.Empty<StockLedgerRow>();
    public int HistoryPage { get; init; } = 1;
    public int HistoryPageCount { get; init; } = 1;
    public int HistoryTotal { get; init; }

    public bool FullView { get; set; }
    public StockLinks Links { get; set; } = StockLinks.None;
}

// ------------------------------------------------------------------ Chi tiết lô

/// <summary>Hóa đơn đã xuất từ lô (tra cứu lô đã bán — 2.3.3.2).</summary>
public sealed record BatchSaleRow(DateTime When, int InvoiceId, string InvoiceCode, string CustomerName, string? Phone, decimal Quantity);

public sealed class StockBatchViewModel
{
    public const int LedgerLimit = 100;
    public const int SalesLimit = 200;

    public int BatchId { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public int MedicineId { get; init; }
    public string MedicineCode { get; init; } = string.Empty;
    public string MedicineName { get; init; } = string.Empty;
    public string BaseUnit { get; init; } = string.Empty;
    public DateOnly? ManufactureDate { get; init; }
    public DateOnly ExpiryDate { get; init; }
    public int DaysLeft { get; init; }
    public DateOnly ReceivedDate { get; init; }
    public int? GoodsReceiptId { get; init; }
    public string? GoodsReceiptCode { get; init; }
    public string? SupplierName { get; init; }
    public decimal UnitCost { get; init; }
    public string WarehouseName { get; init; } = string.Empty;
    public int? LocationId { get; init; }
    public string? Location { get; init; }
    public byte Status { get; init; }
    public BatchCondition Condition { get; init; }
    public string? Notes { get; init; }
    public decimal Quantity { get; init; }
    public string? Breakdown { get; init; }
    public int WarningDays { get; init; }

    /// <summary>Sổ kho của lô, mới nhất trước, có số tồn sau từng biến động.</summary>
    public IReadOnlyList<StockLedgerRow> Ledger { get; init; } = Array.Empty<StockLedgerRow>();
    public int LedgerTotal { get; init; }

    public IReadOnlyList<BatchSaleRow> Sales { get; init; } = Array.Empty<BatchSaleRow>();
    public int SalesCount { get; init; }
    public decimal SoldQuantity { get; init; }

    /// <summary>Kệ cùng kho để đổi vị trí.</summary>
    public IReadOnlyList<LookupItem> ShelfOptions { get; init; } = Array.Empty<LookupItem>();

    /// <summary>Lô còn hàng và chưa ngừng sử dụng (điều kiện để đổi vị trí).</summary>
    public bool Movable => Quantity > 0 && Status != (byte)BatchStatus.Discontinued;

    public bool CanSeeSales { get; set; }
    public bool CanMove { get; set; }
    public StockLinks Links { get; set; } = StockLinks.None;
}
