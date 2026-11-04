using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace PharmacyManagement.Models.ViewModels.Catalog;

/// <summary>
/// Quy tắc dùng chung cho số điện thoại, email, mã số thuế của nhà cung cấp và khách hàng.
/// Mẫu regex dùng cho cả kiểm tra trên trình duyệt (DataAnnotations) và kiểm tra lại ở service.
/// Các cột Phone/Email/TaxCode là varchar → chỉ nhận ký tự không dấu.
/// </summary>
public static class ContactRules
{
    /// <summary>Bắt đầu bằng 0, tổng 10–11 chữ số; cho phép khoảng trắng, dấu chấm, dấu gạch giữa các số.</summary>
    public const string PhonePattern = @"^\s*0(?:[ .\-]?[0-9]){9,10}\s*$";

    public const string PhoneMessage = "Số điện thoại gồm 10–11 chữ số, bắt đầu bằng 0.";

    /// <summary>Mã số thuế Việt Nam: 10 số, hoặc 10 số - 3 số (chi nhánh).</summary>
    public const string TaxCodePattern = @"^\s*[0-9]{10}(?:-[0-9]{3})?\s*$";

    public const string TaxCodeMessage = "Mã số thuế gồm 10 chữ số, hoặc 10 chữ số - 3 chữ số (đơn vị phụ thuộc).";

    public const string EmailMessage = "Email chưa đúng dạng.";

    private static readonly EmailAddressAttribute EmailCheck = new();

    /// <summary>Chỉ giữ chữ số; null nếu trống.</summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    public static bool IsValidPhone(string? digits)
        => digits is null || (digits.Length is 10 or 11 && digits[0] == '0' && digits.All(char.IsAsciiDigit));

    /// <summary>Bỏ khoảng trắng, chữ thường; null nếu trống.</summary>
    public static string? NormalizeEmail(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.ToLowerInvariant();
    }

    public static bool IsValidEmail(string? email)
        => email is null || (email.Length <= 256 && email.All(char.IsAscii) && !email.Any(char.IsWhiteSpace) && EmailCheck.IsValid(email));

    public static string? NormalizeTaxCode(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static bool IsValidTaxCode(string? taxCode)
        => taxCode is null || System.Text.RegularExpressions.Regex.IsMatch(taxCode, TaxCodePattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Hiển thị số điện thoại dễ đọc: 0912 345 678 / 0283 812 3456.</summary>
    public static string? DisplayPhone(string? digits)
    {
        if (string.IsNullOrEmpty(digits) || !digits.All(char.IsAsciiDigit)) return digits;
        return digits.Length is 10 or 11
            ? string.Create(CultureInfo.InvariantCulture, $"{digits[..4]} {digits[4..7]} {digits[7..]}")
            : digits;
    }
}

// ------------------------------------------------------------------ Danh sách

/// <summary>Bộ lọc danh sách nhà cung cấp (đọc từ query string).</summary>
public sealed class SupplierFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo tên, mã, mã số thuế, số điện thoại, người liên hệ.</summary>
    public string? Q { get; set; }

    /// <summary>"active" (mặc định), "inactive", "all".</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record SupplierRow(
    int SupplierId,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    string? TaxCode,
    string? ContactPerson,
    bool IsActive);

public sealed class SupplierListViewModel
{
    public SupplierFilter Filter { get; init; } = new();
    public IReadOnlyList<SupplierRow> Rows { get; init; } = Array.Empty<SupplierRow>();
    public int Total { get; init; }
    /// <summary>Tổng số bản ghi trong CSDL (mọi trạng thái, không theo bộ lọc) — hiện "Tìm thấy X / Y".</summary>
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }
}

// ------------------------------------------------------------------ Thêm / sửa

public sealed class SupplierFormViewModel
{
    public int? SupplierId { get; set; }

    /// <summary>Mã nhà cung cấp (tự sinh, chỉ hiển thị).</summary>
    public string? SupplierCode { get; set; }

    [Required(ErrorMessage = "Nhập tên nhà cung cấp.")]
    [StringLength(200, ErrorMessage = "Tên nhà cung cấp tối đa 200 ký tự.")]
    [Display(Name = "Tên nhà cung cấp")]
    public string SupplierName { get; set; } = string.Empty;

    [StringLength(30, ErrorMessage = "Mã số thuế tối đa 30 ký tự.")]
    [RegularExpression(ContactRules.TaxCodePattern, ErrorMessage = ContactRules.TaxCodeMessage)]
    [Display(Name = "Mã số thuế")]
    public string? TaxCode { get; set; }

    [StringLength(30, ErrorMessage = ContactRules.PhoneMessage)]
    [RegularExpression(ContactRules.PhonePattern, ErrorMessage = ContactRules.PhoneMessage)]
    [Display(Name = "Số điện thoại")]
    public string? Phone { get; set; }

    [StringLength(256, ErrorMessage = "Email tối đa 256 ký tự.")]
    [EmailAddress(ErrorMessage = ContactRules.EmailMessage)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(150, ErrorMessage = "Tên người liên hệ tối đa 150 ký tự.")]
    [Display(Name = "Người liên hệ")]
    public string? ContactPerson { get; set; }

    [StringLength(300, ErrorMessage = "Địa chỉ tối đa 300 ký tự.")]
    [Display(Name = "Địa chỉ")]
    public string? Address { get; set; }

    public bool IsEdit => SupplierId is not null;
}

// ------------------------------------------------------------------ Chi tiết

public sealed class SupplierDetailsViewModel
{
    public int SupplierId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? TaxCode { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? ContactPerson { get; init; }
    public string? Address { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Tổng số đơn đặt hàng (mọi trạng thái).</summary>
    public int PurchaseOrderCount { get; init; }

    /// <summary>Đơn đặt hàng đang mở: Nháp, Chờ giao, Giao thiếu.</summary>
    public int OpenPurchaseOrderCount { get; init; }

    /// <summary>Phiếu nhập đã nhập kho (không tính phiếu hủy, phiếu đang kiểm nhận).</summary>
    public int PostedReceiptCount { get; init; }

    public DateTime? LastReceiptDate { get; init; }
}
