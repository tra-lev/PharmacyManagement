using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Infrastructure;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Sales;

/// <summary>Tên và màu nhãn trạng thái hóa đơn (SalesInvoices.Status).</summary>
public static class InvoiceStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)InvoiceStatus.PendingPayment => "Chờ thanh toán",
        (byte)InvoiceStatus.Paid => "Đã thanh toán",
        (byte)InvoiceStatus.Cancelled => "Đã hủy",
        _ => "Không rõ"
    };

    /// <summary>Lớp màu pm-badge.</summary>
    public static string Tone(byte status) => status switch
    {
        (byte)InvoiceStatus.PendingPayment => "wait",
        (byte)InvoiceStatus.Paid => "good",
        _ => "mute"
    };
}

/// <summary>Tên phương thức thanh toán (Payments.PaymentMethod).</summary>
public static class PaymentMethodText
{
    public static string Of(byte method) => method switch
    {
        (byte)PaymentMethod.Cash => "Tiền mặt",
        (byte)PaymentMethod.BankTransfer => "Chuyển khoản/QR",
        (byte)PaymentMethod.Card => "Thẻ",
        _ => "Không rõ"
    };
}

// ------------------------------------------------------------------ Lập hóa đơn bán lẻ (3.1.4)

/// <summary>Một dòng thuốc bán. Số lượng nguyên theo đơn vị bán; tồn trừ theo đơn vị cơ sở = số lượng × quy đổi.</summary>
public sealed class SaleLineForm
{
    public int? MedicineId { get; set; }

    public int? UnitId { get; set; }

    [Display(Name = "Số lượng")]
    public int? Quantity { get; set; }
}

/// <summary>Đơn vị bán được của một thuốc kèm giá bán hiện hành (DB-13).</summary>
public sealed record SaleUnitOption(int UnitId, string UnitName, decimal Factor, decimal Price, bool IsDefault);

/// <summary>Khuyến mãi đang hiệu lực áp dụng được cho một thuốc. <paramref name="AmountPerBase"/>: số tiền giảm mỗi đơn vị cơ sở.</summary>
public sealed record SalePromotionOption(int PromotionId, string Name, decimal? Percent, decimal? AmountPerBase);

/// <summary>
/// Thuốc bán lẻ được (đang kinh doanh, KHÔNG kê đơn). <paramref name="AvailableBase"/>: tồn khả dụng (lô Khả dụng, còn hạn) theo đơn vị cơ sở.
/// <paramref name="Keywords"/>: tên hoạt chất, dùng cho ô tìm thuốc (js/combobox.js).
/// </summary>
public sealed record SaleMedicineOption(
    int MedicineId,
    string Label,
    string BaseUnitName,
    decimal AvailableBase,
    IReadOnlyList<SaleUnitOption> Units,
    IReadOnlyList<SalePromotionOption> Promotions,
    string Keywords);

/// <summary>Khách hàng chọn được khi bán (đang hoạt động). <paramref name="Allergies"/>: tên hoạt chất dị ứng đã ghi.</summary>
public sealed record SaleCustomerOption(int CustomerId, string Label, string Keywords, string? Allergies);

/// <summary>Cảnh báo an toàn (3.1.3) — chỉ cảnh báo, dược sĩ quyết định.</summary>
public sealed record SafetyWarning(string Tone, string Text);

public sealed class SaleFormViewModel
{
    public const int MaxLines = 50;
    public const int MaxQuantity = 10_000;

    /// <summary>Hóa đơn đang sửa (lấy từ đường dẫn, không đọc từ form); null = lập mới.</summary>
    public int? InvoiceId { get; set; }

    public bool IsEdit => InvoiceId is not null;

    [Display(Name = "Khách hàng")]
    public int? CustomerId { get; set; }

    /// <summary>Giảm thêm theo % tiền hàng (0–100, tối đa 1 chữ số thập phân); tiền giảm làm tròn xuống 100 đ.</summary>
    [Range(typeof(decimal), "0", "100", ErrorMessage = "Giảm thêm từ 0 đến 100%.")]
    [Display(Name = "Giảm thêm (%)")]
    public decimal? ExtraDiscountPercent { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    /// <summary>Đã xem các cảnh báo an toàn và vẫn bán.</summary>
    public bool AcknowledgeWarnings { get; set; }

    /// <summary>Dấu của bộ cảnh báo người dùng đã xem (ô ẩn). Bộ cảnh báo đổi (thêm thuốc, đổi khách) thì phải xem và xác nhận lại.</summary>
    public string? WarningsKey { get; set; }

    public List<SaleLineForm> Lines { get; set; } = new();

    // ---- Hiển thị (server điền, không đọc từ form) ----
    public bool HasEmployeeProfile { get; set; }
    public string? ShiftCode { get; set; }
    public bool HasOpenShift => ShiftCode is not null;

    /// <summary>Mã hóa đơn dự kiến (tự sinh khi lưu).</summary>
    public string? Code { get; set; }

    public IReadOnlyList<SaleMedicineOption> MedicineOptions { get; set; } = Array.Empty<SaleMedicineOption>();
    public IReadOnlyList<SaleCustomerOption> CustomerOptions { get; set; } = Array.Empty<SaleCustomerOption>();

    /// <summary>Giảm tối đa (%) trên tổng tiền hàng theo chính sách giá đang hiệu lực (1.5.4); 0 nếu không có chính sách.</summary>
    public decimal MaxDiscountPercent { get; set; }

    public string? PricePolicyName { get; set; }

    /// <summary>Có quyền SYS09.DiscountOverLimit (giảm vượt mức chính sách giá) — controller điền.</summary>
    public bool CanExceedDiscountLimit { get; set; }

    /// <summary>Có quyền F1.2.2 — được thêm khách hàng ngay tại quầy — controller điền.</summary>
    public bool CanCreateCustomer { get; set; }

    public IReadOnlyList<SafetyWarning> Warnings { get; set; } = Array.Empty<SafetyWarning>();

    /// <summary>Dấu của <see cref="Warnings"/> đang hiển thị — ghi vào ô ẩn WarningsKey.</summary>
    public string? ShownWarningsKey { get; set; }
}

/// <summary>Thêm nhanh khách hàng tại quầy (1.2.2) — các trường tối thiểu; dị ứng, bệnh nền ghi ở hồ sơ khách.</summary>
public sealed class QuickCustomerForm
{
    [Required(ErrorMessage = "Nhập họ tên khách hàng.")]
    [StringLength(150, ErrorMessage = "Họ tên tối đa 150 ký tự.")]
    [Display(Name = "Họ tên")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(30, ErrorMessage = ContactRules.PhoneMessage)]
    [RegularExpression(ContactRules.PhonePattern, ErrorMessage = ContactRules.PhoneMessage)]
    [Display(Name = "Số điện thoại")]
    public string? Phone { get; set; }

    /// <summary>yyyy-MM-dd (ô type="date").</summary>
    [RegularExpression(CustomerFormViewModel.DatePattern, ErrorMessage = "Ngày sinh chưa đúng dạng.")]
    [Display(Name = "Ngày sinh")]
    public string? DateOfBirth { get; set; }

    [Range(1, 3, ErrorMessage = "Chọn giới tính trong danh sách.")]
    [Display(Name = "Giới tính")]
    public byte? Gender { get; set; }
}

// ------------------------------------------------------------------ Thanh toán (3.3.2)

/// <summary>Thanh toán một lần cho đủ tiền: tiền mặt / chuyển khoản / thẻ, kết hợp được (3.3.2.3).</summary>
public sealed class PaymentForm
{
    [Money]
    [Display(Name = "Tiền mặt")]
    public decimal? CashAmount { get; set; }

    [Money]
    [Display(Name = "Tiền khách đưa")]
    public decimal? TenderedAmount { get; set; }

    [Money]
    [Display(Name = "Chuyển khoản/QR")]
    public decimal? TransferAmount { get; set; }

    [StringLength(100, ErrorMessage = "Mã giao dịch tối đa 100 ký tự.")]
    [Display(Name = "Mã giao dịch chuyển khoản")]
    public string? TransferReference { get; set; }

    [Money]
    [Display(Name = "Thẻ")]
    public decimal? CardAmount { get; set; }

    [StringLength(100, ErrorMessage = "Mã giao dịch tối đa 100 ký tự.")]
    [Display(Name = "Mã giao dịch thẻ")]
    public string? CardReference { get; set; }
}

/// <summary>Tài khoản nhận chuyển khoản để tạo mã VietQR (appsettings Pharmacy:BankTransfer).</summary>
public sealed record BankTransferInfo(string BankBin, string BankName, string AccountNumber, string AccountName);

/// <summary>Quyền thu tiền của người đang đăng nhập — controller lấy từ RolePermissions, service kiểm tra lại.</summary>
public sealed record PaymentPermissions(bool Cash, bool NonCash, bool Combined)
{
    public bool Any => Cash || NonCash;
}

// ------------------------------------------------------------------ Danh sách

public sealed class InvoiceFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã hóa đơn, tên hoặc số điện thoại khách.</summary>
    public string? Q { get; set; }

    /// <summary>"pending", "paid", "cancelled" hoặc trống (tất cả).</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record InvoiceRow(
    int InvoiceId,
    string Code,
    DateTime InvoiceDate,
    string? CustomerName,
    string EmployeeName,
    int LineCount,
    decimal TotalAmount,
    byte Status,
    bool HasPrescription);

public sealed class InvoiceListViewModel
{
    public InvoiceFilter Filter { get; init; } = new();
    public IReadOnlyList<InvoiceRow> Rows { get; init; } = Array.Empty<InvoiceRow>();
    public int Total { get; init; }

    /// <summary>Tổng số hóa đơn trong phạm vi được xem (của tôi hoặc toàn nhà thuốc), mọi trạng thái.</summary>
    public int GrandTotal { get; init; }

    public int PageCount { get; init; }

    /// <summary>true = xem hóa đơn toàn nhà thuốc (F4.1.1); false = chỉ hóa đơn của tôi.</summary>
    public bool ViewAll { get; init; }

    public bool CanSell { get; set; }
}

// ------------------------------------------------------------------ Chi tiết

/// <summary>Lô đã xuất cho một dòng (2.3.4 FEFO).</summary>
public sealed record AllocationInfo(string BatchNumber, DateOnly ExpiryDate, decimal QuantityBase);

public sealed record InvoiceLineInfo(
    string MedicineCode,
    string MedicineName,
    string UnitName,
    decimal Factor,
    string BaseUnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal,
    string? PromotionName,
    IReadOnlyList<AllocationInfo> Allocations);

public sealed record PaymentInfo(byte Method, decimal Amount, decimal? TenderedAmount, string? Reference, DateTime PaidAt, byte Status)
{
    public decimal? Change => TenderedAmount is null ? null : TenderedAmount - Amount;
}

public sealed class InvoiceDetailsViewModel
{
    public int InvoiceId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public DateTime InvoiceDate { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerCode { get; init; }
    public string? CustomerPhone { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string ShiftCode { get; init; } = string.Empty;
    public string? PrescriptionCode { get; init; }
    public decimal SubTotal { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal TotalAmount { get; init; }

    /// <summary>% giảm thêm dạng hiển thị ("10", "2,5"); null nếu không có hoặc không đọc được từ ghi chú.</summary>
    public string? ExtraDiscountPercent { get; init; }

    /// <summary>Tiền giảm thêm cho hóa đơn (đã gồm trong DiscountAmount).</summary>
    public decimal ExtraDiscountAmount { get; init; }

    /// <summary>Giảm do khuyến mãi = DiscountAmount − ExtraDiscountAmount.</summary>
    public decimal PromotionDiscount { get; init; }

    /// <summary>Giảm của hóa đơn chưa chia vào dòng (hóa đơn lập trước bản sửa chia giảm thêm theo dòng); 0 với hóa đơn mới.</summary>
    public decimal UnallocatedDiscount { get; init; }

    public string? Notes { get; init; }
    public string? CancelledByName { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string? CancelReason { get; init; }
    public IReadOnlyList<InvoiceLineInfo> Lines { get; init; } = Array.Empty<InvoiceLineInfo>();
    public IReadOnlyList<PaymentInfo> Payments { get; init; } = Array.Empty<PaymentInfo>();

    /// <summary>Hóa đơn do chính người đang xem lập.</summary>
    public bool IsOwn { get; init; }

    /// <summary>Ca của hóa đơn còn Đang mở (chưa bàn giao) — điều kiện để thu tiền.</summary>
    public bool ShiftOpen { get; init; }

    public bool IsPending => Status == (byte)InvoiceStatus.PendingPayment;

    // ---- Controller điền theo quyền ----
    public bool CanPay { get; set; }

    /// <summary>Quay lại sửa thuốc: hóa đơn chờ thanh toán của chính người dùng, ca còn mở, có quyền lập hóa đơn.</summary>
    public bool CanEdit { get; set; }
    public bool CanCancel { get; set; }
    public PaymentPermissions Permissions { get; set; } = new(false, false, false);

    /// <summary>Dữ liệu ô thanh toán (giữ lại khi báo lỗi).</summary>
    public PaymentForm Payment { get; set; } = new();

    /// <summary>Tài khoản nhận chuyển khoản; null = chưa cấu hình, không hiện mã QR.</summary>
    public BankTransferInfo? Bank { get; set; }
}
