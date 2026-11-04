using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Infrastructure;
using PharmacyManagement.Models.Enums;

namespace PharmacyManagement.Models.ViewModels.Sales;

/// <summary>Tên và màu nhãn trạng thái ca (Shifts.Status).</summary>
public static class ShiftStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)ShiftStatus.Open => "Đang mở",
        (byte)ShiftStatus.HandedOver => "Đã bàn giao",
        (byte)ShiftStatus.Closed => "Đã chốt",
        _ => "Không rõ"
    };

    /// <summary>Lớp màu pm-badge.</summary>
    public static string Tone(byte status) => status switch
    {
        (byte)ShiftStatus.Open => "good",
        (byte)ShiftStatus.HandedOver => "wait",
        _ => "mute"
    };
}

// ------------------------------------------------------------------ Form

/// <summary>1.4.1 Mở ca: tiền mặt đầu ca (đồng).</summary>
public sealed class OpenShiftForm
{
    [Required(ErrorMessage = "Nhập tiền mặt đầu ca (không có thì nhập 0).")]
    [Money]
    [Display(Name = "Tiền mặt đầu ca")]
    public decimal? OpeningCash { get; set; }
}

/// <summary>1.4.2 Bàn giao ca: ghi chú bàn giao.</summary>
public sealed class HandoverForm
{
    [Required(ErrorMessage = "Nhập ghi chú bàn giao.")]
    [StringLength(1000, ErrorMessage = "Ghi chú bàn giao tối đa 1000 ký tự.")]
    [Display(Name = "Ghi chú bàn giao")]
    public string? HandoverNote { get; set; }
}

/// <summary>1.4.3 Chốt tiền cuối ca: tiền mặt thực đếm + giải trình chênh lệch.</summary>
public sealed class CloseShiftForm
{
    [Required(ErrorMessage = "Nhập số tiền mặt thực đếm trong két.")]
    [Money]
    [Display(Name = "Tiền mặt thực đếm")]
    public decimal? ActualCash { get; set; }

    [StringLength(1000, ErrorMessage = "Giải trình tối đa 1000 ký tự.")]
    [Display(Name = "Giải trình chênh lệch")]
    public string? Notes { get; set; }
}

// ------------------------------------------------------------------ Hiển thị

/// <summary>Số liệu tiền của một ca, tính từ CSDL (DEC-11 / CHANGELOG_DB mục 8).</summary>
public sealed record ShiftTotals(
    decimal OpeningCash,
    int PaidInvoiceCount,
    int PendingInvoiceCount,
    decimal CashSales,
    decimal NonCashSales,
    decimal CashRefunds)
{
    /// <summary>Tiền mặt theo sổ = đầu ca + tiền mặt bán − tiền mặt hoàn (CK_ShiftClosings_Expected).</summary>
    public decimal ExpectedCash => OpeningCash + CashSales - CashRefunds;
}

/// <summary>Ca chưa chốt (đang mở hoặc đã bàn giao) của người đang đăng nhập.</summary>
public sealed class CurrentShiftInfo
{
    public int ShiftId { get; init; }
    public string Code { get; init; } = string.Empty;
    public DateTime OpenedAt { get; init; }
    public byte Status { get; init; }
    public string? HandoverNote { get; init; }
    public DateTime? HandedOverAt { get; init; }
    public ShiftTotals Totals { get; init; } = new(0, 0, 0, 0, 0, 0);
}

public sealed record ShiftHistoryRow(
    int ShiftId,
    string Code,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    decimal ExpectedCash,
    decimal ActualCash,
    decimal Difference);

/// <summary>Trang "Ca của tôi".</summary>
public sealed class MyShiftViewModel
{
    public const int HistoryLimit = 20;

    /// <summary>Tài khoản đã gắn hồ sơ nhân viên đang làm việc (mới mở ca được).</summary>
    public bool HasEmployeeProfile { get; init; }
    public string? EmployeeName { get; init; }

    public CurrentShiftInfo? Current { get; init; }

    /// <summary>Lần chốt gần nhất của người này (gợi ý tiền đầu ca).</summary>
    public decimal? LastActualCash { get; init; }
    public DateTime? LastClosedAt { get; init; }

    public IReadOnlyList<ShiftHistoryRow> History { get; init; } = Array.Empty<ShiftHistoryRow>();

    public OpenShiftForm Open { get; set; } = new();
    public HandoverForm Handover { get; set; } = new();
    public CloseShiftForm Close { get; set; } = new();

    // Quyền (controller điền theo mã BFD)
    /// <summary>F4.1.1 (Owner, Manager): xem ca của toàn nhà thuốc.</summary>
    public bool CanViewAll { get; set; }
    public bool CanOpen { get; set; }
    public bool CanHandover { get; set; }
    public bool CanClose { get; set; }
}

/// <summary>Một hóa đơn lập trong ca. <paramref name="Methods"/>: hình thức đã thu (Payments thành công).</summary>
public sealed record ShiftInvoiceRow(
    int InvoiceId, string Code, DateTime InvoiceDate, string? CustomerName, string? PrescriptionCode,
    decimal TotalAmount, byte Status, IReadOnlyList<byte> Methods);

/// <summary>Chi tiết / biên bản một ca.</summary>
public sealed class ShiftDetailsViewModel
{
    public int ShiftId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public byte Status { get; init; }
    public DateTime OpenedAt { get; init; }
    public DateTime? HandedOverAt { get; init; }
    public string? HandoverNote { get; init; }
    public DateTime? ClosedAt { get; init; }
    public string? ClosedByName { get; init; }

    /// <summary>Ca đã chốt: số liệu lưu trong biên bản (ShiftClosings); chưa chốt: tính tại thời điểm xem.</summary>
    public ShiftTotals Totals { get; init; } = new(0, 0, 0, 0, 0, 0);

    /// <summary>Có giá trị khi ca đã chốt.</summary>
    public decimal? ActualCash { get; init; }
    public decimal? Difference { get; init; }
    public string? ClosingNotes { get; init; }

    /// <summary>Ca của chính người đang xem (false = Owner/Manager xem ca của người khác — chỉ xem).</summary>
    public bool IsOwnShift { get; init; }

    /// <summary>Mọi hóa đơn lập trong ca, mới nhất trước.</summary>
    public IReadOnlyList<ShiftInvoiceRow> Invoices { get; init; } = Array.Empty<ShiftInvoiceRow>();
}

// ------------------------------------------------------------------ Ca toàn nhà thuốc (F4.1.1)

/// <summary>Bộ lọc danh sách ca toàn nhà thuốc (query string).</summary>
public sealed class ShiftFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã ca hoặc họ tên nhân viên.</summary>
    public string? Q { get; set; }

    public int? EmployeeId { get; set; }

    /// <summary>"open", "handedover", "closed" hoặc trống (tất cả).</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record ShiftListRow(
    int ShiftId,
    string Code,
    string EmployeeName,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    byte Status,
    decimal? ExpectedCash,
    decimal? ActualCash)
{
    public decimal? Difference => ExpectedCash is null || ActualCash is null ? null : ActualCash - ExpectedCash;
}

public sealed record EmployeeOption(int EmployeeId, string Name);

public sealed class ShiftListViewModel
{
    public ShiftFilter Filter { get; init; } = new();
    public IReadOnlyList<ShiftListRow> Rows { get; init; } = Array.Empty<ShiftListRow>();
    public int Total { get; init; }

    /// <summary>Tổng số ca trong CSDL.</summary>
    public int GrandTotal { get; init; }

    public int PageCount { get; init; }

    /// <summary>Nhân viên đã từng đứng ca.</summary>
    public IReadOnlyList<EmployeeOption> Employees { get; init; } = Array.Empty<EmployeeOption>();
}
