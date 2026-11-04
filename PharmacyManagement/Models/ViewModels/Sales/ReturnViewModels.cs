using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Models.Enums;

namespace PharmacyManagement.Models.ViewModels.Sales;

/// <summary>Tên và màu nhãn trạng thái phiếu đổi trả (Returns.Status) và từng dòng (ReturnDetails.RestockStatus).</summary>
public static class ReturnStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)ReturnStatus.RefundedPendingRestock => "Đã hoàn tiền — chờ nhập kho",
        (byte)ReturnStatus.Completed => "Hoàn tất",
        (byte)ReturnStatus.Cancelled => "Đã hủy",
        _ => "Không rõ"
    };

    public static string Tone(byte status) => status switch
    {
        (byte)ReturnStatus.RefundedPendingRestock => "wait",
        (byte)ReturnStatus.Completed => "good",
        _ => "mute"
    };

    public static string RestockOf(byte status) => status switch
    {
        (byte)RestockStatus.Pending => "Chờ nhập",
        (byte)RestockStatus.Restocked => "Đã nhập lại",
        (byte)RestockStatus.Rejected => "Không nhập",
        _ => "Không rõ"
    };

    public static string RestockTone(byte status) => status switch
    {
        (byte)RestockStatus.Pending => "wait",
        (byte)RestockStatus.Restocked => "good",
        _ => "mute"
    };
}

// ------------------------------------------------------------------ 3.4.1 Tra hóa đơn gốc

public sealed record ReturnLookupRow(
    int InvoiceId, string Code, DateTime SoldAt, string? CustomerName, string? CustomerPhone, decimal TotalAmount, bool SameDay, bool HasReturnable);

public sealed class ReturnLookupViewModel
{
    /// <summary>Mã hóa đơn, tên khách (không dấu cũng được) hoặc số điện thoại khách.</summary>
    public string? Q { get; set; }

    public IReadOnlyList<ReturnLookupRow> Rows { get; init; } = Array.Empty<ReturnLookupRow>();
}

// ------------------------------------------------------------------ 3.4.2 + 3.4.3 Kiểm tra, hoàn tiền

/// <summary>Một dòng hóa đơn gốc và số còn trả được (theo đơn vị bán của dòng).</summary>
public sealed record ReturnInvoiceLine(
    int InvoiceDetailId, string MedicineCode, string MedicineName, string UnitName, decimal SoldQuantity, decimal ReturnedQuantity,
    decimal UnitPrice, decimal DiscountAmount, decimal LineTotal, decimal UnitRefundPrice, decimal RefundedAmount)
{
    public decimal Remaining => SoldQuantity - ReturnedQuantity;
}

public sealed class ReturnLineForm
{
    public int InvoiceDetailId { get; set; }

    /// <summary>Số lượng trả (nguyên, theo đơn vị bán của dòng); trống/0 = không trả dòng này.</summary>
    public int? Quantity { get; set; }

    [StringLength(300, ErrorMessage = "Tình trạng hàng tối đa 300 ký tự.")]
    public string? ConditionNote { get; set; }
}

public sealed class ReturnFormViewModel
{
    [Required(ErrorMessage = "Nhập lý do khách trả hàng.")]
    [StringLength(500, ErrorMessage = "Lý do tối đa 500 ký tự.")]
    [Display(Name = "Lý do trả")]
    public string? Reason { get; set; }

    /// <summary>PaymentMethod: 1 tiền mặt, 2 chuyển khoản, 3 thẻ.</summary>
    [Range(1, 3, ErrorMessage = "Chọn hình thức hoàn tiền.")]
    public byte RefundMethod { get; set; } = (byte)PaymentMethod.Cash;

    [StringLength(100, ErrorMessage = "Mã giao dịch tối đa 100 ký tự.")]
    [Display(Name = "Mã giao dịch hoàn tiền")]
    public string? RefundReference { get; set; }

    public List<ReturnLineForm> Lines { get; set; } = new();

    /// <summary>Yêu cầu đổi trả dược sĩ đã gửi (AuditLogs RETURN_REQUEST) mà quản lý đang xử lý; null = quản lý tự tiếp nhận.</summary>
    public long? RequestId { get; set; }

    // ---- Hiển thị (server điền) ----
    public ReturnRequestInfo? Request { get; set; }
    public int InvoiceId { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public DateTime SoldAt { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? PrescriptionCode { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public IReadOnlyList<PaymentInfo> Payments { get; set; } = Array.Empty<PaymentInfo>();
    public IReadOnlyList<byte> PaidMethods { get; set; } = Array.Empty<byte>();
    public IReadOnlyList<ReturnInvoiceLine> InvoiceLines { get; set; } = Array.Empty<ReturnInvoiceLine>();
    public string? ReturnConditions { get; set; }

    /// <summary>null = trả được; có nội dung = lý do không trả được (quá ngày, chưa thanh toán…).</summary>
    public string? BlockedReason { get; set; }

    public bool HasReturnable => InvoiceLines.Any(l => l.Remaining > 0);

    /// <summary>Có quyền kiểm tra + hoàn tiền (F3.4.2 + F3.4.3) — controller điền.</summary>
    public bool CanProcess { get; set; }

    /// <summary>Chỉ có quyền tiếp nhận (F3.4.1): ghi số lượng, lý do rồi gửi yêu cầu cho quản lý — controller điền.</summary>
    public bool CanRequest { get; set; }

    /// <summary>Hóa đơn đang có yêu cầu chờ quản lý xử lý (không gửi thêm).</summary>
    public ReturnRequestInfo? PendingRequest { get; set; }

    public bool HasOpenShift { get; set; }
}

// ------------------------------------------------------------------ Danh sách, chi tiết, 3.4.4 nhập lại

public sealed class ReturnFilter
{
    public const int PageSize = 20;

    /// <summary>Mã phiếu, mã hóa đơn, tên hoặc SĐT khách.</summary>
    public string? Q { get; set; }

    /// <summary>"pending", "completed" hoặc trống.</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record ReturnRow(
    int ReturnId, string Code, DateTime ReturnDate, string InvoiceCode, string? CustomerName, decimal RefundAmount, byte RefundMethod,
    byte Status, int PendingLines);

public sealed class ReturnListViewModel
{
    public ReturnFilter Filter { get; init; } = new();
    public IReadOnlyList<ReturnRow> Rows { get; init; } = Array.Empty<ReturnRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }
    public bool CanLookup { get; set; }

    /// <summary>Yêu cầu đổi trả hôm nay: quản lý thấy tất cả, dược sĩ thấy yêu cầu mình gửi — controller điền.</summary>
    public IReadOnlyList<ReturnRequestInfo> Requests { get; set; } = Array.Empty<ReturnRequestInfo>();
    public bool CanProcess { get; set; }
}

// ------------------------------------------------------------------ Yêu cầu đổi trả (dược sĩ → quản lý), lưu ở AuditLogs

public static class ReturnRequestState
{
    public const string Pending = "pending";
    public const string Done = "done";
    public const string Rejected = "rejected";
}

/// <summary>Một dòng dược sĩ ghi trong yêu cầu (lưu dạng JSON trong AuditLogs.NewValues).</summary>
public sealed record ReturnRequestLine(int InvoiceDetailId, int Quantity, string? ConditionNote);

/// <summary>Nội dung yêu cầu (AuditLogs RETURN_REQUEST, EntityName ReturnRequests, EntityId = InvoiceId).</summary>
public sealed record ReturnRequestPayload(string InvoiceCode, string Reason, string RequestedBy, List<ReturnRequestLine> Lines);

public sealed record ReturnRequestInfo(
    long RequestId, int InvoiceId, string InvoiceCode, string? CustomerName, string RequestedBy, string? RequestedByUserId, DateTime RequestedAt,
    string Reason, IReadOnlyList<ReturnRequestLine> Lines, string State, string? ResultText, int? ReturnId);

/// <summary>Lô mà dòng hóa đơn đã xuất (SalesIssueAllocations) — nơi nhập lại hàng khách trả.</summary>
public sealed record RestockBatchOption(int BatchId, string BatchNumber, DateOnly ExpiryDate, decimal CapacityBase, bool Usable, string? Problem);

public sealed record ReturnDetailLine(
    int ReturnDetailId, string MedicineCode, string MedicineName, string UnitName, decimal Quantity, decimal BaseQuantity, string BaseUnitName,
    decimal UnitRefundPrice, decimal RefundAmount, byte RestockStatus, string? BatchNumber, string? ConditionNote,
    IReadOnlyList<RestockBatchOption> Batches);

public sealed class ReturnDetailsViewModel
{
    public int ReturnId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public DateTime ReturnDate { get; init; }
    public int InvoiceId { get; init; }
    public string InvoiceCode { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public string ProcessedByName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public decimal RefundAmount { get; init; }
    public byte RefundMethod { get; init; }
    public string? RefundReference { get; init; }
    public string? ShiftCode { get; init; }
    public IReadOnlyList<ReturnDetailLine> Lines { get; init; } = Array.Empty<ReturnDetailLine>();

    /// <summary>F3.4.4 — controller điền.</summary>
    public bool CanRestock { get; set; }
}

/// <summary>3.4.4: xử lý một dòng chờ nhập.</summary>
public sealed class RestockForm
{
    public int ReturnDetailId { get; set; }

    /// <summary>"restock" = nhập lại vào lô; "reject" = không nhập.</summary>
    public string? Decision { get; set; }

    public int? BatchId { get; set; }

    [StringLength(300, ErrorMessage = "Ghi chú tối đa 300 ký tự.")]
    public string? Note { get; set; }
}

/// <summary>Các lần trả của một hóa đơn (hiện ở trang hóa đơn).</summary>
public sealed record InvoiceReturnInfo(int ReturnId, string Code, DateTime ReturnDate, decimal RefundAmount, byte Status);
