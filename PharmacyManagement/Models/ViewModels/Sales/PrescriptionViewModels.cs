using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Models.Enums;

namespace PharmacyManagement.Models.ViewModels.Sales;

/// <summary>Tên và màu nhãn trạng thái đơn thuốc (Prescriptions.Status).</summary>
public static class PrescriptionStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)PrescriptionStatus.Saved => "Chưa bán",
        (byte)PrescriptionStatus.Dispensed => "Đã bán",
        (byte)PrescriptionStatus.Cancelled => "Đã hủy",
        _ => "Không rõ"
    };

    public static string Tone(byte status) => status switch
    {
        (byte)PrescriptionStatus.Saved => "wait",
        (byte)PrescriptionStatus.Dispensed => "good",
        _ => "mute"
    };
}

/// <summary>Quy định dùng chung cho đơn thuốc.</summary>
public static class PrescriptionRules
{
    /// <summary>Đơn còn hạn mua đến hết ngày kê + số ngày này (nhóm chốt 04/11: 5 ngày).</summary>
    public const int ValidDays = 5;

    public const int MaxLines = 30;
}

// ------------------------------------------------------------------ Tiếp nhận + kiểm tra + lưu đơn (3.2.1–3.2.3)

public sealed class PrescriptionLineForm
{
    public int? MedicineId { get; set; }

    public int? UnitId { get; set; }

    [Display(Name = "Số lượng kê")]
    public int? Quantity { get; set; }

    /// <summary>Liều mỗi lần, theo đơn vị cơ sở của thuốc (ví dụ 1 Viên).</summary>
    [Display(Name = "Liều mỗi lần")]
    public decimal? DosePerTime { get; set; }

    [Display(Name = "Số lần mỗi ngày")]
    public int? TimesPerDay { get; set; }

    [Display(Name = "Số ngày dùng")]
    public int? DurationDays { get; set; }

    [StringLength(500, ErrorMessage = "Cách dùng tối đa 500 ký tự.")]
    public string? Instructions { get; set; }
}

public sealed record RxUnitOption(int UnitId, string UnitName, decimal Factor);

/// <summary>Thuốc chọn được khi tiếp nhận đơn: đang kinh doanh, không thuộc nhóm kiểm soát đặc biệt.</summary>
/// <summary>Gợi ý tên bác sĩ đã dùng ở các đơn trước, kèm cơ sở khám gần nhất của bác sĩ đó.</summary>
public sealed record DoctorSuggestion(string Name, string Clinic);

public sealed record RxMedicineOption(int MedicineId, string Label, string BaseUnitName, IReadOnlyList<RxUnitOption> Units, string Keywords);

public sealed class PrescriptionFormViewModel
{
    [Required(ErrorMessage = "Chọn khách hàng của đơn thuốc.")]
    [Display(Name = "Khách hàng")]
    public int? CustomerId { get; set; }

    [Required(ErrorMessage = "Nhập tên bác sĩ kê đơn.")]
    [StringLength(150, ErrorMessage = "Tên bác sĩ tối đa 150 ký tự.")]
    [Display(Name = "Bác sĩ")]
    public string? DoctorName { get; set; }

    [Required(ErrorMessage = "Nhập cơ sở khám bệnh.")]
    [StringLength(200, ErrorMessage = "Cơ sở khám bệnh tối đa 200 ký tự.")]
    [Display(Name = "Cơ sở khám bệnh")]
    public string? ClinicName { get; set; }

    [StringLength(500, ErrorMessage = "Chẩn đoán tối đa 500 ký tự.")]
    [Display(Name = "Chẩn đoán")]
    public string? Diagnosis { get; set; }

    /// <summary>yyyy-MM-dd (ô type="date").</summary>
    [Required(ErrorMessage = "Nhập ngày kê đơn.")]
    [Display(Name = "Ngày kê đơn")]
    public string? PrescriptionDate { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Notes { get; set; }

    public bool AcknowledgeWarnings { get; set; }
    public string? WarningsKey { get; set; }

    public List<PrescriptionLineForm> Lines { get; set; } = new();

    // ---- Hiển thị (server điền) ----
    public IReadOnlyList<RxMedicineOption> MedicineOptions { get; set; } = Array.Empty<RxMedicineOption>();
    public IReadOnlyList<SaleCustomerOption> CustomerOptions { get; set; } = Array.Empty<SaleCustomerOption>();
    public IReadOnlyList<SafetyWarning> Warnings { get; set; } = Array.Empty<SafetyWarning>();
    public string? ShownWarningsKey { get; set; }
    public string Today { get; set; } = string.Empty;
    public string MinDate { get; set; } = string.Empty;
    public bool CanCreateCustomer { get; set; }

    /// <summary>Gợi ý khi gõ tên bác sĩ / cơ sở khám (từ các đơn đã lưu, dùng nhiều xếp trước).</summary>
    public IReadOnlyList<DoctorSuggestion> DoctorSuggestions { get; set; } = Array.Empty<DoctorSuggestion>();
    public IReadOnlyList<string> ClinicSuggestions { get; set; } = Array.Empty<string>();

    /// <summary>Đã bấm "Kiểm tra đơn" và đơn hợp lệ.</summary>
    public bool Checked { get; set; }
}

// ------------------------------------------------------------------ Danh sách

public sealed class PrescriptionFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã đơn, tên hoặc SĐT khách, tên bác sĩ.</summary>
    public string? Q { get; set; }

    /// <summary>"saved", "dispensed", "cancelled" hoặc trống.</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record PrescriptionRow(
    int PrescriptionId, string Code, DateOnly PrescriptionDate, string CustomerName, string DoctorName, string ClinicName,
    int LineCount, byte Status, bool Expired);

public sealed class PrescriptionListViewModel
{
    public PrescriptionFilter Filter { get; init; } = new();
    public IReadOnlyList<PrescriptionRow> Rows { get; init; } = Array.Empty<PrescriptionRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }
    public bool CanCreate { get; set; }
}

// ------------------------------------------------------------------ Chi tiết

public sealed record PrescriptionLineInfo(
    string MedicineCode, string MedicineName, bool IsPrescription, string UnitName, decimal Quantity,
    string? Dosage, string? Frequency, int? DurationDays, string? Instructions);

public sealed record PrescriptionInvoiceInfo(int InvoiceId, string Code, DateTime InvoiceDate, decimal TotalAmount, byte Status);

public sealed class PrescriptionDetailsViewModel
{
    public int PrescriptionId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerCode { get; init; } = string.Empty;
    public string? CustomerPhone { get; init; }
    public string DoctorName { get; init; } = string.Empty;
    public string ClinicName { get; init; } = string.Empty;
    public string? Diagnosis { get; init; }
    public DateOnly PrescriptionDate { get; init; }
    public DateOnly ValidUntil { get; init; }
    public bool Expired { get; init; }
    public string CheckedByName { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public IReadOnlyList<PrescriptionLineInfo> Lines { get; init; } = Array.Empty<PrescriptionLineInfo>();
    public IReadOnlyList<PrescriptionInvoiceInfo> Invoices { get; init; } = Array.Empty<PrescriptionInvoiceInfo>();

    /// <summary>Hóa đơn chờ thanh toán đang lập cho đơn này (nếu có).</summary>
    public int? PendingInvoiceId => Invoices.FirstOrDefault(i => i.Status == (byte)InvoiceStatus.PendingPayment)?.InvoiceId;

    public bool IsSaved => Status == (byte)PrescriptionStatus.Saved;

    // ---- Controller điền ----
    public bool CanSell { get; set; }
    public bool CanCancel { get; set; }
}
