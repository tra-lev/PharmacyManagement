using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Infrastructure;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>Tên và màu nhãn trạng thái phiếu nhập (GoodsReceipts.Status).</summary>
public static class ReceiptStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)GoodsReceiptStatus.Inspecting => "Đang kiểm nhận",
        (byte)GoodsReceiptStatus.Posted => "Đã nhập kho",
        (byte)GoodsReceiptStatus.Cancelled => "Đã hủy",
        _ => "Không rõ"
    };

    /// <summary>Lớp màu pm-badge.</summary>
    public static string Tone(byte status) => status switch
    {
        (byte)GoodsReceiptStatus.Inspecting => "wait",
        (byte)GoodsReceiptStatus.Posted => "good",
        _ => "mute"
    };
}

// ------------------------------------------------------------------ Danh sách

/// <summary>Bộ lọc danh sách phiếu nhập (query string).</summary>
public sealed class ReceiptFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã phiếu, số hóa đơn NCC, mã đơn đặt hàng, tên NCC.</summary>
    public string? Q { get; set; }

    /// <summary>"inspecting", "posted", "cancelled" hoặc trống (tất cả).</summary>
    public string? Status { get; set; }

    public int? SupplierId { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record ReceiptRow(
    int GoodsReceiptId,
    string Code,
    DateTime ReceiptDate,
    string SupplierName,
    string? SupplierInvoiceNumber,
    string? PurchaseOrderCode,
    int LineCount,
    decimal TotalAmount,
    byte Status);

public sealed class ReceiptListViewModel
{
    public ReceiptFilter Filter { get; init; } = new();
    public IReadOnlyList<ReceiptRow> Rows { get; init; } = Array.Empty<ReceiptRow>();
    public int Total { get; init; }

    /// <summary>Tổng số phiếu trong CSDL (mọi trạng thái).</summary>
    public int GrandTotal { get; init; }

    public int PageCount { get; init; }
    public IReadOnlyList<LookupItem> Suppliers { get; init; } = Array.Empty<LookupItem>();
    public bool CanCreate { get; set; }
}

// ------------------------------------------------------------------ Lập / sửa phiếu (kiểm nhận)

/// <summary>Một dòng hàng nhận. Số lượng theo đơn vị nhập; tồn lô = (nhận − hỏng) × hệ số quy đổi (đơn vị cơ sở).</summary>
public sealed class ReceiptLineForm
{
    /// <summary>Dòng của đơn đặt hàng (null = hàng ngoài đơn). Có giá trị thì thuốc và đơn vị theo đơn, không đổi.</summary>
    public int? PurchaseOrderDetailId { get; set; }

    public int? MedicineId { get; set; }

    public int? UnitId { get; set; }

    [Display(Name = "Số lượng nhận")]
    public decimal? QuantityReceived { get; set; }

    [Display(Name = "Số lượng hỏng")]
    public decimal? DamagedQuantity { get; set; }

    [Money]
    [Display(Name = "Đơn giá nhập")]
    public decimal? UnitCost { get; set; }

    [StringLength(60, ErrorMessage = "Số lô tối đa 60 ký tự.")]
    public string? BatchNumber { get; set; }

    /// <summary>yyyy-MM-dd (ô type="date"), đọc theo InvariantCulture ở service.</summary>
    public string? ManufactureDate { get; set; }

    /// <summary>yyyy-MM-dd (ô type="date").</summary>
    public string? ExpiryDate { get; set; }

    /// <summary>Kệ xếp thuốc (StorageLocations.LocationType = Kệ).</summary>
    public int? LocationId { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú chất lượng tối đa 500 ký tự.")]
    public string? QualityNote { get; set; }

    // ---- Hiển thị (server điền, không đọc từ form) ----
    public string? MedicineLabel { get; set; }
    public string? UnitName { get; set; }
    public decimal? OrderedQuantity { get; set; }
    public decimal? AlreadyReceived { get; set; }
}

public sealed record UnitOption(int UnitId, string UnitName, decimal Factor);

/// <summary>
/// Thuốc chọn được khi nhập (đang kinh doanh) + các đơn vị nhập được.
/// <paramref name="Keywords"/>: tên hoạt chất, dùng cho ô tìm thuốc (js/combobox.js).
/// </summary>
public sealed record MedicineOption(int MedicineId, string Label, string BaseUnitName, decimal PurchasePricePerBase, IReadOnlyList<UnitOption> Units, string Keywords);

public sealed record PurchaseOrderOption(int PurchaseOrderId, string Label, int SupplierId);

public sealed class ReceiptFormViewModel
{
    public int? GoodsReceiptId { get; set; }

    /// <summary>Mã phiếu (tự sinh, chỉ hiển thị).</summary>
    public string? Code { get; set; }

    [Required(ErrorMessage = "Chọn nhà cung cấp.")]
    [Display(Name = "Nhà cung cấp")]
    public int? SupplierId { get; set; }

    [Display(Name = "Đơn đặt hàng")]
    public int? PurchaseOrderId { get; set; }

    [StringLength(50, ErrorMessage = "Số hóa đơn tối đa 50 ký tự.")]
    [RegularExpression(@"^[\x20-\x7E]*$", ErrorMessage = "Số hóa đơn chỉ gồm chữ không dấu, chữ số và ký hiệu.")]
    [Display(Name = "Số hóa đơn của nhà cung cấp")]
    public string? SupplierInvoiceNumber { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    public List<ReceiptLineForm> Lines { get; set; } = new();

    // ---- Hiển thị (server điền) ----
    public bool IsEdit => GoodsReceiptId is not null;
    public string? PurchaseOrderCode { get; set; }
    public string? SupplierName { get; set; }
    public IReadOnlyList<LookupItem> SupplierOptions { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<PurchaseOrderOption> PurchaseOrderOptions { get; set; } = Array.Empty<PurchaseOrderOption>();
    public IReadOnlyList<MedicineOption> MedicineOptions { get; set; } = Array.Empty<MedicineOption>();
    public IReadOnlyList<LookupItem> LocationOptions { get; set; } = Array.Empty<LookupItem>();
    public int ExpiryWarningDays { get; set; }
    public string Today { get; set; } = string.Empty;
}

// ------------------------------------------------------------------ Chi tiết

public sealed record ReceiptLineInfo(
    string MedicineCode,
    string MedicineName,
    string UnitName,
    decimal Factor,
    string BaseUnitName,
    decimal QuantityReceived,
    decimal DamagedQuantity,
    decimal UnitCost,
    decimal SubTotal,
    string? QualityNote,
    string? BatchNumber,
    DateOnly? ManufactureDate,
    DateOnly? ExpiryDate,
    string? LocationName,
    decimal? OrderedQuantity,
    decimal? OrderReceivedQuantity,
    int? BatchId,
    decimal? BatchOnHand,
    int? DaysToExpiry)
{
    public decimal GoodQuantity => QuantityReceived - DamagedQuantity;
}

public sealed class ReceiptDetailsViewModel
{
    public int GoodsReceiptId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public DateTime ReceiptDate { get; init; }
    public string SupplierCode { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string? SupplierInvoiceNumber { get; init; }
    public string? PurchaseOrderCode { get; init; }
    public string ReceivedByName { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public decimal TotalAmount { get; init; }
    public int ExpiryWarningDays { get; init; }
    public IReadOnlyList<ReceiptLineInfo> Lines { get; init; } = Array.Empty<ReceiptLineInfo>();

    public bool CanEdit { get; set; }
    public bool CanPost { get; set; }
}
