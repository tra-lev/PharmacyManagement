using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>Chữ và tông màu cho trạng thái phiếu thu hồi (ProductRecalls.Status).</summary>
public static class RecallStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)RecallStatus.Received => "Mới tiếp nhận",
        (byte)RecallStatus.InProgress => "Đang thu hồi",
        (byte)RecallStatus.Completed => "Hoàn tất",
        _ => "Không rõ"
    };

    public static string Tone(byte status) => status switch
    {
        (byte)RecallStatus.Received => "bad",
        (byte)RecallStatus.InProgress => "warn",
        (byte)RecallStatus.Completed => "good",
        _ => "mute"
    };
}

// ------------------------------------------------------------------ Danh sách

public sealed class RecallFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã phiếu, số công văn, tên / mã thuốc, số lô.</summary>
    public string? Q { get; set; }

    /// <summary>"open" chưa hoàn tất, "received", "progress", "done".</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || Status is not null;
}

public sealed record RecallRow(
    int RecallId,
    string Code,
    DateTime CreatedAt,
    string MedicineName,
    string MedicineCode,
    string? NoticeNumber,
    DateOnly? NoticeDate,
    int BatchCount,
    string BatchNumbers,
    byte Status);

public sealed class RecallListViewModel
{
    public RecallFilter Filter { get; init; } = new();
    public IReadOnlyList<RecallRow> Rows { get; init; } = Array.Empty<RecallRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }
    public bool CanCreate { get; set; }
}

// ------------------------------------------------------------------ Tiếp nhận thông báo (2.3.3.1)

/// <summary>Một lô của thuốc bị thu hồi để chọn khi tiếp nhận.</summary>
public sealed record RecallBatchOption(
    int BatchId,
    string BatchNumber,
    DateOnly ExpiryDate,
    decimal QuantityOnHand,
    byte Status,
    string? SupplierName,
    decimal SoldQuantity);

public sealed class RecallFormViewModel
{
    [Required(ErrorMessage = "Chọn thuốc bị thu hồi.")]
    [Display(Name = "Thuốc")]
    public int? MedicineId { get; set; }

    [StringLength(80, ErrorMessage = "Số công văn tối đa 80 ký tự.")]
    [Display(Name = "Số công văn")]
    public string? NoticeNumber { get; set; }

    /// <summary>yyyy-MM-dd (ô type="date"), đọc theo InvariantCulture ở service.</summary>
    [Display(Name = "Ngày công văn")]
    public string? NoticeDate { get; set; }

    [Required(ErrorMessage = "Nhập lý do thu hồi.")]
    [StringLength(1000, ErrorMessage = "Lý do tối đa 1000 ký tự.")]
    [Display(Name = "Lý do thu hồi")]
    public string? Reason { get; set; }

    /// <summary>Lô được chọn để thu hồi.</summary>
    public List<int> BatchIds { get; set; } = new();

    // ---- Hiển thị ----
    public IReadOnlyList<LookupItem> Medicines { get; set; } = Array.Empty<LookupItem>();
    public string? MedicineName { get; set; }
    public string? BaseUnit { get; set; }
    public IReadOnlyList<RecallBatchOption> Batches { get; set; } = Array.Empty<RecallBatchOption>();
}

// ------------------------------------------------------------------ Chi tiết, thu hồi (2.3.3.2, 2.3.3.3)

public sealed record RecallBatchView(
    int RecallBatchId,
    int BatchId,
    string BatchNumber,
    DateOnly ExpiryDate,
    string? Location,
    decimal QuantityAtRecall,
    decimal Recovered,
    decimal OnHand,
    string? Notes)
{
    /// <summary>Còn thu hồi được: không vượt tồn lúc thu hồi và tồn hiện tại.</summary>
    public decimal Recoverable => Math.Max(0, Math.Min(QuantityAtRecall - Recovered, OnHand));

    public bool Done => Recovered >= QuantityAtRecall || OnHand <= 0;
}

/// <summary>Khách đã mua lô bị thu hồi (từ phân bổ xuất kho theo hóa đơn, bỏ hóa đơn đã hủy).</summary>
public sealed record RecallBuyerRow(
    DateTime When,
    int InvoiceId,
    string InvoiceCode,
    string BatchNumber,
    string CustomerName,
    string? Phone,
    decimal Quantity);

public sealed class RecallDetailsViewModel
{
    public const int BuyerLimit = 500;

    public int RecallId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public int MedicineId { get; init; }
    public string MedicineCode { get; init; } = string.Empty;
    public string MedicineName { get; init; } = string.Empty;
    public string BaseUnit { get; init; } = string.Empty;
    public string? NoticeNumber { get; init; }
    public DateOnly? NoticeDate { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string CreatedByName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string PharmacyName { get; init; } = string.Empty;
    public IReadOnlyList<RecallBatchView> Batches { get; init; } = Array.Empty<RecallBatchView>();

    public IReadOnlyList<RecallBuyerRow> Buyers { get; init; } = Array.Empty<RecallBuyerRow>();
    public int BuyerCount { get; init; }

    public decimal TotalAtRecall => Batches.Sum(b => b.QuantityAtRecall);
    public decimal TotalRecovered => Batches.Sum(b => b.Recovered);
    public bool AllDone => Batches.All(b => b.Done);
    public bool IsCompleted => Status == (byte)RecallStatus.Completed;

    /// <summary>Xem danh sách khách đã mua (F2.3.3.2).</summary>
    public bool CanSeeBuyers { get; set; }

    /// <summary>Lập phiếu thu hồi / hoàn tất (F2.3.3.3).</summary>
    public bool CanRecover { get; set; }

    public bool CanOpenInvoices { get; set; }

    /// <summary>Mở trang chi tiết lô ở Tồn kho.</summary>
    public bool CanOpenBatches { get; set; }
}

public sealed class RecoverLineInput
{
    public int RecallBatchId { get; set; }

    public decimal? Quantity { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Notes { get; set; }
}

public sealed class RecoverForm
{
    public List<RecoverLineInput> Lines { get; set; } = new();
}
