using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Infrastructure;
using PharmacyManagement.Models.Enums;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>Chữ cho phiếu chi (trả tiền nhà cung cấp) / phiếu thu (nhà cung cấp hoàn tiền).</summary>
public static class SupplierPaymentText
{
    public static string Type(byte type) => type == (byte)SupplierPaymentType.RefundFromSupplier ? "Phiếu thu tiền hoàn" : "Phiếu chi";

    public static string Method(byte method) => method == (byte)SupplierPaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản";

    public static string Status(byte status) => status == (byte)PaymentStatus.Voided ? "Đã hủy" : "Đã ghi nhận";

    public static string Tone(byte status) => status == (byte)PaymentStatus.Voided ? "mute" : "good";
}

/// <summary>Số tiền chi / thu với nhà cung cấp (2.2.5, 2.3.5).</summary>
public sealed class SupplierMoneyForm
{
    [Required(ErrorMessage = "Nhập số tiền.")]
    [Money]
    [Display(Name = "Số tiền")]
    public decimal? Amount { get; set; }

    /// <summary>1 Tiền mặt, 2 Chuyển khoản.</summary>
    public byte Method { get; set; } = (byte)SupplierPaymentMethod.BankTransfer;

    [StringLength(100, ErrorMessage = "Mã giao dịch tối đa 100 ký tự.")]
    [RegularExpression(@"^[\x20-\x7E]*$", ErrorMessage = "Mã giao dịch chỉ gồm chữ không dấu, chữ số và ký hiệu.")]
    [Display(Name = "Mã giao dịch")]
    public string? Reference { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }
}

/// <summary>Một phiếu chi / thu hiển thị trong danh sách nhỏ (chi tiết NCC, phiếu nhập, phiếu trả).</summary>
public sealed record SupplierPaymentLine(
    long SupplierPaymentId,
    string Code,
    DateTime PaidAt,
    byte Type,
    byte Method,
    decimal Amount,
    string? Reference,
    byte Status,
    string EmployeeName,
    string? DocumentCode,
    string? Notes);

// ------------------------------------------------------------------ Công nợ theo nhà cung cấp

public sealed class SupplierDebtFilter
{
    public const int PageSize = 20;

    public string? Q { get; set; }

    /// <summary>"all" = cả nhà cung cấp không còn nợ; trống = chỉ nhà cung cấp còn nợ hoặc còn tiền hoàn chờ nhận.</summary>
    public string? Show { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || Show is not null;
}

public sealed record SupplierDebtRow(
    int SupplierId,
    string Code,
    string Name,
    int OpenReceipts,
    decimal Purchased,
    decimal Paid,
    decimal Remaining,
    decimal RefundPending);

public sealed class SupplierDebtListViewModel
{
    public SupplierDebtFilter Filter { get; init; } = new();
    public IReadOnlyList<SupplierDebtRow> Rows { get; init; } = Array.Empty<SupplierDebtRow>();
    public int Total { get; init; }

    /// <summary>Số nhà cung cấp đã có phiếu nhập kho.</summary>
    public int GrandTotal { get; init; }

    public int PageCount { get; init; }
    public decimal TotalRemaining { get; init; }
    public decimal TotalRefundPending { get; init; }
}

public sealed record ReceiptDebtRow(int GoodsReceiptId, string Code, DateTime ReceiptDate, string? InvoiceNumber, decimal Total, decimal Paid, decimal Remaining);

public sealed record ReturnRefundRow(int SupplierReturnId, string Code, DateTime ReturnDate, decimal Total, decimal Refunded, decimal Remaining);

public sealed class SupplierDebtViewModel
{
    public int SupplierId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? ContactPerson { get; init; }

    /// <summary>Phiếu nhập đã nhập kho còn nợ.</summary>
    public IReadOnlyList<ReceiptDebtRow> OpenReceipts { get; init; } = Array.Empty<ReceiptDebtRow>();

    /// <summary>Phiếu trả đã xuất, chưa nhận đủ tiền hoàn.</summary>
    public IReadOnlyList<ReturnRefundRow> OpenReturns { get; init; } = Array.Empty<ReturnRefundRow>();

    /// <summary>Phiếu chi / thu gần nhất (tối đa 50).</summary>
    public IReadOnlyList<SupplierPaymentLine> Payments { get; init; } = Array.Empty<SupplierPaymentLine>();

    public decimal Purchased { get; init; }
    public decimal Paid { get; init; }
    public decimal Remaining { get; init; }
    public decimal RefundPending { get; init; }

    public bool CanPay { get; set; }
    public bool CanRefund { get; set; }

    /// <summary>Được mở trang phiếu nhập / phiếu trả (để liên kết không dẫn tới trang bị chặn).</summary>
    public bool CanOpenReceipts { get; set; }
    public bool CanOpenReturns { get; set; }
}

// ------------------------------------------------------------------ Phiếu chi / thu

public sealed class SupplierPaymentFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã phiếu, mã giao dịch, tên nhà cung cấp, mã phiếu nhập / phiếu trả.</summary>
    public string? Q { get; set; }

    /// <summary>"pay" phiếu chi, "refund" phiếu thu tiền hoàn.</summary>
    public string? Type { get; set; }

    /// <summary>"ok" đã ghi nhận, "void" đã hủy.</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || Type is not null || Status is not null;
}

public sealed record SupplierPaymentRow(
    long SupplierPaymentId,
    string Code,
    DateTime PaidAt,
    byte Type,
    string SupplierName,
    string? DocumentCode,
    byte Method,
    decimal Amount,
    byte Status);

public sealed class SupplierPaymentListViewModel
{
    public SupplierPaymentFilter Filter { get; init; } = new();
    public IReadOnlyList<SupplierPaymentRow> Rows { get; init; } = Array.Empty<SupplierPaymentRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }
}

public sealed class SupplierPaymentDetailsViewModel
{
    public long SupplierPaymentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Type { get; init; }
    public byte Method { get; init; }
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public DateTime PaidAt { get; init; }
    public byte Status { get; init; }
    public string? Notes { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public int? GoodsReceiptId { get; init; }
    public string? GoodsReceiptCode { get; init; }
    public int? SupplierReturnId { get; init; }
    public string? SupplierReturnCode { get; init; }

    public bool IsRefund => Type == (byte)SupplierPaymentType.RefundFromSupplier;
    public bool CanVoid { get; set; }
    public bool CanOpenReceipts { get; set; }
    public bool CanOpenReturns { get; set; }
}

/// <summary>Trang lập phiếu chi cho một phiếu nhập (2.2.5).</summary>
public sealed class PayReceiptViewModel
{
    public int GoodsReceiptId { get; init; }
    public string ReceiptCode { get; init; } = string.Empty;
    public DateTime ReceiptDate { get; init; }
    public string? InvoiceNumber { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
    public decimal Remaining { get; init; }
    public SupplierMoneyForm Form { get; set; } = new();
}
