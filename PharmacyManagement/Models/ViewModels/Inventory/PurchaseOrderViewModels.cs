using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Infrastructure;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>Tên và màu nhãn trạng thái đơn đặt hàng (PurchaseOrders.Status).</summary>
public static class PurchaseOrderStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)PurchaseOrderStatus.Draft => "Nháp",
        (byte)PurchaseOrderStatus.AwaitingDelivery => "Chờ giao",
        (byte)PurchaseOrderStatus.PartiallyDelivered => "Giao thiếu",
        (byte)PurchaseOrderStatus.Delivered => "Đã giao",
        (byte)PurchaseOrderStatus.Cancelled => "Đã hủy",
        _ => "Không rõ"
    };

    public static string Tone(byte status) => status switch
    {
        (byte)PurchaseOrderStatus.Draft => "mute",
        (byte)PurchaseOrderStatus.AwaitingDelivery => "wait",
        (byte)PurchaseOrderStatus.PartiallyDelivered => "warn",
        (byte)PurchaseOrderStatus.Delivered => "good",
        _ => "mute"
    };
}

// ------------------------------------------------------------------ Danh sách (2.1.3 theo dõi)

public sealed class PurchaseOrderFilter
{
    public const int PageSize = 20;

    /// <summary>Mã đơn hoặc tên nhà cung cấp.</summary>
    public string? Q { get; set; }

    /// <summary>"draft", "awaiting", "partial", "delivered", "cancelled" hoặc trống.</summary>
    public string? Status { get; set; }

    public int? SupplierId { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record PurchaseOrderRow(
    int PurchaseOrderId, string Code, DateTime OrderDate, DateOnly? ExpectedDeliveryDate, string SupplierName,
    int LineCount, int FullLines, decimal TotalAmount, byte Status);

public sealed class PurchaseOrderListViewModel
{
    public PurchaseOrderFilter Filter { get; init; } = new();
    public IReadOnlyList<PurchaseOrderRow> Rows { get; init; } = Array.Empty<PurchaseOrderRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }
    public IReadOnlyList<LookupItem> Suppliers { get; init; } = Array.Empty<LookupItem>();

    /// <summary>Số thuốc có tồn khả dụng dưới mức tối thiểu (gợi ý đặt hàng).</summary>
    public int LowStockCount { get; init; }

    public bool CanCreate { get; set; }
}

// ------------------------------------------------------------------ Lập / sửa đơn (2.1.1)

public sealed class PurchaseOrderLineForm
{
    public int? MedicineId { get; set; }

    public int? UnitId { get; set; }

    [Display(Name = "Số lượng đặt")]
    public decimal? Quantity { get; set; }

    [Money]
    [Display(Name = "Đơn giá dự kiến")]
    public decimal? UnitPrice { get; set; }
}

/// <summary>Đơn vị đặt được của một thuốc; <paramref name="LastPrice"/>: đơn giá nhập gần nhất theo đơn vị này (0 = chưa có).</summary>
public sealed record OrderUnitOption(int UnitId, string UnitName, decimal Factor, decimal LastPrice);

/// <summary>Thuốc đặt được (đang kinh doanh). <paramref name="StockBase"/>: tồn khả dụng; <paramref name="MinimumBase"/>: mức tối thiểu (null = chưa đặt).</summary>
public sealed record OrderMedicineOption(
    int MedicineId, string Label, string BaseUnitName, IReadOnlyList<OrderUnitOption> Units, string Keywords, decimal StockBase, decimal? MinimumBase);

public sealed class PurchaseOrderFormViewModel
{
    public const int MaxLines = 100;

    public int? PurchaseOrderId { get; set; }
    public string? Code { get; set; }
    public bool IsEdit => PurchaseOrderId is not null;

    [Required(ErrorMessage = "Chọn nhà cung cấp.")]
    [Display(Name = "Nhà cung cấp")]
    public int? SupplierId { get; set; }

    /// <summary>yyyy-MM-dd (ô type="date").</summary>
    [Display(Name = "Ngày dự kiến giao")]
    public string? ExpectedDeliveryDate { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    public List<PurchaseOrderLineForm> Lines { get; set; } = new();

    // ---- Hiển thị (server điền) ----
    public IReadOnlyList<LookupItem> SupplierOptions { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<OrderMedicineOption> MedicineOptions { get; set; } = Array.Empty<OrderMedicineOption>();
    public string Today { get; set; } = string.Empty;
    public int LowStockCount { get; set; }

    /// <summary>Đã thêm các thuốc dưới mức tồn tối thiểu (bấm "Gợi ý thuốc cần đặt").</summary>
    public int SuggestedCount { get; set; }
}

// ------------------------------------------------------------------ Chi tiết, in, gửi

public sealed record PurchaseOrderLineInfo(
    string MedicineCode, string MedicineName, string UnitName, decimal Factor, string BaseUnitName,
    decimal OrderedQuantity, decimal ReceivedQuantity, decimal UnitPrice)
{
    public decimal Remaining => Math.Max(0, OrderedQuantity - ReceivedQuantity);
    public decimal SubTotal => OrderedQuantity * UnitPrice;
}

public sealed record PurchaseOrderReceiptInfo(int GoodsReceiptId, string Code, DateTime ReceiptDate, decimal TotalAmount, byte Status);

public sealed class PurchaseOrderDetailsViewModel
{
    public int PurchaseOrderId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public DateTime OrderDate { get; init; }
    public DateOnly? ExpectedDeliveryDate { get; init; }
    public string? Notes { get; init; }
    public decimal TotalAmount { get; init; }
    public string CreatedByName { get; init; } = string.Empty;
    public int SupplierId { get; init; }
    public string SupplierCode { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string? SupplierPhone { get; init; }
    public string? SupplierEmail { get; init; }
    public string? SupplierAddress { get; init; }
    public string? SupplierContact { get; init; }
    public bool SupplierActive { get; init; }
    public IReadOnlyList<PurchaseOrderLineInfo> Lines { get; init; } = Array.Empty<PurchaseOrderLineInfo>();
    public IReadOnlyList<PurchaseOrderReceiptInfo> Receipts { get; init; } = Array.Empty<PurchaseOrderReceiptInfo>();

    /// <summary>Thời điểm đã gửi đơn / gửi email (đọc từ nhật ký hoạt động).</summary>
    public DateTime? SentAt { get; init; }
    public IReadOnlyList<DateTime> EmailedAt { get; init; } = Array.Empty<DateTime>();

    public string PharmacyName { get; set; } = string.Empty;

    public bool IsDraft => Status == (byte)PurchaseOrderStatus.Draft;
    public bool IsOpen => Status is (byte)PurchaseOrderStatus.AwaitingDelivery or (byte)PurchaseOrderStatus.PartiallyDelivered;

    // ---- Controller điền theo quyền ----
    public bool CanEdit { get; set; }
    public bool CanSend { get; set; }
    public bool CanEmail { get; set; }
    public bool CanCancel { get; set; }
    public bool CanReceive { get; set; }
    public bool EmailConfigured { get; set; }
}
