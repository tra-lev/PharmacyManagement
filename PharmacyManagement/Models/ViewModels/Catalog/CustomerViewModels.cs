using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Models.Enums;

namespace PharmacyManagement.Models.ViewModels.Catalog;

/// <summary>Tên hiển thị giới tính (Customers.Gender: 1 Nam, 2 Nữ, 3 Khác — ENUMS.md).</summary>
public static class GenderText
{
    public static readonly IReadOnlyList<(byte Value, string Name)> Options = new[]
    {
        ((byte)Gender.Male, "Nam"), ((byte)Gender.Female, "Nữ"), ((byte)Gender.Other, "Khác")
    };

    public static string? Of(byte? value) => value switch
    {
        (byte)Gender.Male => "Nam",
        (byte)Gender.Female => "Nữ",
        (byte)Gender.Other => "Khác",
        _ => null
    };
}

// ------------------------------------------------------------------ Danh sách

/// <summary>Bộ lọc danh sách khách hàng (đọc từ query string).</summary>
public sealed class CustomerFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo họ tên, mã khách hàng, số điện thoại.</summary>
    public string? Q { get; set; }

    /// <summary>"active" (mặc định), "inactive", "all".</summary>
    public string? Status { get; set; }

    /// <summary>"allergy" = có dị ứng, "chronic" = có bệnh nền, trống = tất cả.</summary>
    public string? Health { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record CustomerRow(
    int CustomerId,
    string Code,
    string FullName,
    string? Phone,
    int? Age,
    byte? Gender,
    int AllergyCount,
    bool HasChronicConditions,
    bool IsActive);

public sealed class CustomerListViewModel
{
    public CustomerFilter Filter { get; init; } = new();
    public IReadOnlyList<CustomerRow> Rows { get; init; } = Array.Empty<CustomerRow>();
    public int Total { get; init; }
    public int PageCount { get; init; }
}

// ------------------------------------------------------------------ Thêm / sửa

/// <summary>Một dòng dị ứng: tên hoạt chất (chọn từ danh mục có sẵn) + mô tả phản ứng.</summary>
public sealed class AllergyLine
{
    [StringLength(200, ErrorMessage = "Tên hoạt chất tối đa 200 ký tự.")]
    public string? IngredientName { get; set; }

    [StringLength(500, ErrorMessage = "Mô tả phản ứng tối đa 500 ký tự.")]
    public string? Reaction { get; set; }
}

public sealed class CustomerFormViewModel
{
    public const string DatePattern = @"^\d{4}-\d{2}-\d{2}$";

    public int? CustomerId { get; set; }

    /// <summary>Mã khách hàng (tự sinh, chỉ hiển thị).</summary>
    public string? CustomerCode { get; set; }

    [Required(ErrorMessage = "Nhập họ tên khách hàng.")]
    [StringLength(150, ErrorMessage = "Họ tên tối đa 150 ký tự.")]
    [Display(Name = "Họ tên")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(30, ErrorMessage = ContactRules.PhoneMessage)]
    [RegularExpression(ContactRules.PhonePattern, ErrorMessage = ContactRules.PhoneMessage)]
    [Display(Name = "Số điện thoại")]
    public string? Phone { get; set; }

    [StringLength(256, ErrorMessage = "Email tối đa 256 ký tự.")]
    [EmailAddress(ErrorMessage = ContactRules.EmailMessage)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    /// <summary>
    /// Ngày sinh dạng yyyy-MM-dd (giá trị của &lt;input type="date"&gt;). Giữ kiểu chuỗi và tự đọc theo InvariantCulture
    /// ở service, vì văn hóa vi-VN của ứng dụng đọc ngày theo dd/MM/yyyy.
    /// </summary>
    [RegularExpression(DatePattern, ErrorMessage = "Ngày sinh chưa đúng dạng.")]
    [Display(Name = "Ngày sinh")]
    public string? DateOfBirth { get; set; }

    [Range(1, 3, ErrorMessage = "Chọn giới tính trong danh sách.")]
    [Display(Name = "Giới tính")]
    public byte? Gender { get; set; }

    [StringLength(300, ErrorMessage = "Địa chỉ tối đa 300 ký tự.")]
    [Display(Name = "Địa chỉ")]
    public string? Address { get; set; }

    /// <summary>Bệnh nền, mỗi bệnh cách nhau dấu ; hoặc xuống dòng — dùng so từ khóa chống chỉ định khi bán theo đơn (DEC-08).</summary>
    [StringLength(500, ErrorMessage = "Bệnh nền tối đa 500 ký tự.")]
    [Display(Name = "Bệnh nền")]
    public string? ChronicConditions { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    public List<AllergyLine> Allergies { get; set; } = new();

    // ---- Hiển thị (server tính, không đọc từ form) ----
    public bool IsEdit => CustomerId is not null;

    /// <summary>Tên hoạt chất trong danh mục để gợi ý ở dòng dị ứng.</summary>
    public IReadOnlyList<string> IngredientNames { get; set; } = Array.Empty<string>();

    /// <summary>Giới hạn cho ô ngày sinh (yyyy-MM-dd).</summary>
    public string MinBirthDate { get; set; } = string.Empty;
    public string MaxBirthDate { get; set; } = string.Empty;
}

// ------------------------------------------------------------------ Chi tiết

public sealed record AllergyInfo(string IngredientName, string? Reaction, DateTime RecordedAt);

/// <summary>Một hóa đơn đã thanh toán trong lịch sử mua (3.5.1).</summary>
public sealed record PurchaseRow(string InvoiceCode, DateTime InvoiceDate, decimal TotalAmount, string SellerName, bool ByPrescription);

public sealed class CustomerDetailsViewModel
{
    public const int PurchaseLimit = 20;

    public int CustomerId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public int? Age { get; init; }
    public byte? Gender { get; init; }
    public string? Address { get; init; }
    public IReadOnlyList<string> ChronicConditions { get; init; } = Array.Empty<string>();
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<AllergyInfo> Allergies { get; init; } = Array.Empty<AllergyInfo>();

    public int PaidInvoiceCount { get; init; }
    public decimal TotalSpent { get; init; }
    public DateTime? LastPurchaseAt { get; init; }
    public int PrescriptionCount { get; init; }
    public IReadOnlyList<PurchaseRow> RecentPurchases { get; init; } = Array.Empty<PurchaseRow>();
}
