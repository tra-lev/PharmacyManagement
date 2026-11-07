using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>Tên và màu nhãn trạng thái phiếu trả nhà cung cấp (SupplierReturns.Status).</summary>
public static class SupplierReturnStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)SupplierReturnStatus.Draft => "Nháp",
        (byte)SupplierReturnStatus.Issued => "Đã xuất trả, chờ tiền hoàn",
        (byte)SupplierReturnStatus.Settled => "Đã quyết toán",
        (byte)SupplierReturnStatus.Cancelled => "Đã hủy",
        _ => "Không rõ"
    };

    public static string Tone(byte status) => status switch
    {
        (byte)SupplierReturnStatus.Draft => "wait",
        (byte)SupplierReturnStatus.Issued => "warn",
        (byte)SupplierReturnStatus.Settled => "good",
        _ => "mute"
    };
}

// ------------------------------------------------------------------ Danh sách

public sealed class SupplierReturnFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã phiếu, tên nhà cung cấp, lý do, tên thuốc, số lô.</summary>
    public string? Q { get; set; }

    /// <summary>"draft", "issued", "settled", "cancelled" hoặc trống.</summary>
    public string? Status { get; set; }

    public int? SupplierId { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || Status is not null || SupplierId is not null;
}

public sealed record SupplierReturnRow(
    int SupplierReturnId,
    string Code,
    DateTime ReturnDate,
    string SupplierName,
    string? GoodsReceiptCode,
    int LineCount,
    decimal TotalAmount,
    decimal Refunded,
    byte Status);

public sealed class SupplierReturnListViewModel
{
    public SupplierReturnFilter Filter { get; init; } = new();
    public IReadOnlyList<SupplierReturnRow> Rows { get; init; } = Array.Empty<SupplierReturnRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }
    public IReadOnlyList<LookupItem> Suppliers { get; init; } = Array.Empty<LookupItem>();
    public bool CanCreate { get; set; }
}

// ------------------------------------------------------------------ Lập / sửa phiếu (Nháp)

public sealed class SupplierReturnLineForm
{
    public int? BatchId { get; set; }

    [Display(Name = "Số lượng trả")]
    public decimal? Quantity { get; set; }

    /// <summary>Đơn giá trả trên đơn vị cơ sở; để trống = giá vốn lô.</summary>
    [Display(Name = "Đơn giá trả")]
    public decimal? UnitCost { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú dòng tối đa 500 ký tự.")]
    public string? Reason { get; set; }
}

/// <summary>Lô chọn được: còn hàng, không đang thu hồi, nhập từ chính nhà cung cấp này.</summary>
public sealed record SupplierReturnBatchOption(int BatchId, string Label, string Keywords, decimal Quantity, decimal UnitCost, string BaseUnit);

public sealed class SupplierReturnFormViewModel
{
    public const int MaxLines = 100;

    public int? SupplierReturnId { get; set; }
    public string? Code { get; set; }

    [Required(ErrorMessage = "Chọn nhà cung cấp.")]
    [Display(Name = "Nhà cung cấp")]
    public int? SupplierId { get; set; }

    /// <summary>Phiếu nhập gốc (không bắt buộc); chọn thì chỉ trả các lô của phiếu đó.</summary>
    [Display(Name = "Phiếu nhập gốc")]
    public int? GoodsReceiptId { get; set; }

    [Required(ErrorMessage = "Nhập lý do trả hàng.")]
    [StringLength(500, ErrorMessage = "Lý do trả hàng tối đa 500 ký tự.")]
    [Display(Name = "Lý do trả hàng")]
    public string? Reason { get; set; }

    public List<SupplierReturnLineForm> Lines { get; set; } = new();

    // ---- Hiển thị (server điền) ----
    public bool IsEdit => SupplierReturnId is not null;

    [BindNever]
    public bool IsDraft { get; set; } = true;

    public string? SupplierName { get; set; }
    public string? GoodsReceiptCode { get; set; }
    public IReadOnlyList<LookupItem> Suppliers { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Receipts { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<SupplierReturnBatchOption> BatchOptions { get; set; } = Array.Empty<SupplierReturnBatchOption>();
}

// ------------------------------------------------------------------ Chi tiết

public sealed record SupplierReturnLineView(
    int BatchId,
    string MedicineCode,
    string MedicineName,
    string BatchNumber,
    DateOnly ExpiryDate,
    decimal Quantity,
    string BaseUnit,
    decimal UnitCost,
    decimal LineTotal,
    string? Reason,
    decimal BatchOnHand,
    byte LotStatus);

public sealed class SupplierReturnDetailsViewModel
{
    public int SupplierReturnId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public DateTime ReturnDate { get; init; }
    public int SupplierId { get; init; }
    public string SupplierCode { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string? SupplierPhone { get; init; }
    public string? SupplierAddress { get; init; }
    public int? GoodsReceiptId { get; init; }
    public string? GoodsReceiptCode { get; init; }
    public string CreatedByName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }

    /// <summary>Thời điểm xuất trả (lấy từ sổ kho).</summary>
    public DateTime? IssuedAt { get; init; }

    public IReadOnlyList<SupplierReturnLineView> Lines { get; init; } = Array.Empty<SupplierReturnLineView>();

    /// <summary>Phiếu thu tiền hoàn (gồm cả phiếu đã hủy).</summary>
    public IReadOnlyList<SupplierPaymentLine> Refunds { get; init; } = Array.Empty<SupplierPaymentLine>();

    public decimal Refunded { get; init; }
    public decimal Remaining => Math.Max(0, TotalAmount - Refunded);
    public string PharmacyName { get; init; } = string.Empty;

    public bool IsDraft => Status == (byte)SupplierReturnStatus.Draft;
    public bool CanEdit { get; set; }
    public bool CanRefund { get; set; }

    /// <summary>Mở được trang phiếu chi / thu (F2.2.5 hoặc F2.3.5).</summary>
    public bool CanOpenPayments { get; set; }
}
