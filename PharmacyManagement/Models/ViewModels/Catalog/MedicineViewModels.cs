using System.ComponentModel.DataAnnotations;

namespace PharmacyManagement.Models.ViewModels.Catalog;

/// <summary>Mục chọn trong danh sách thả xuống.</summary>
public sealed record LookupItem(int Id, string Name);

// ------------------------------------------------------------------ Danh sách

/// <summary>Bộ lọc danh sách thuốc (đọc từ query string).</summary>
public sealed class MedicineFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo tên, mã thuốc, mã vạch, tên hoạt chất.</summary>
    public string? Q { get; set; }

    public int? CategoryId { get; set; }

    /// <summary>"otc", "rx" hoặc trống (tất cả).</summary>
    public string? Type { get; set; }

    /// <summary>"active" (mặc định), "inactive", "all".</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
}

public sealed record MedicineRow(
    int MedicineId,
    string Code,
    string Name,
    string? Strength,
    string? DosageForm,
    string CategoryName,
    string BaseUnitName,
    decimal SellingPrice,
    bool IsPrescription,
    bool IsControlledDrug,
    bool IsActive,
    string Ingredients);

public sealed class MedicineListViewModel
{
    public MedicineFilter Filter { get; init; } = new();
    public IReadOnlyList<MedicineRow> Rows { get; init; } = Array.Empty<MedicineRow>();
    public int Total { get; init; }
    public int PageCount { get; init; }
    public IReadOnlyList<LookupItem> Categories { get; init; } = Array.Empty<LookupItem>();
    public bool CanCreate { get; set; }
}

// ------------------------------------------------------------------ Thêm / sửa

/// <summary>Một dòng hoạt chất: tên (chọn có sẵn hoặc gõ mới) + hàm lượng.</summary>
public sealed class IngredientLine
{
    [StringLength(200, ErrorMessage = "Tên hoạt chất tối đa 200 ký tự.")]
    public string? Name { get; set; }

    [StringLength(100, ErrorMessage = "Hàm lượng hoạt chất tối đa 100 ký tự.")]
    public string? Strength { get; set; }
}

/// <summary>Một dòng quy đổi: 1 [đơn vị] = ConversionToBase [đơn vị cơ sở].</summary>
public sealed class ConversionLine
{
    /// <summary>Có giá trị khi là dòng đã lưu.</summary>
    public int? ConversionId { get; set; }

    public int? UnitId { get; set; }

    public decimal? ConversionToBase { get; set; }

    /// <summary>Trống = tự tính theo giá đơn vị cơ sở × hệ số.</summary>
    public decimal? SellingPrice { get; set; }

    public bool IsDefaultSaleUnit { get; set; }

    // Chỉ để hiển thị (server tự tính lại, không đọc từ form)
    public bool Locked { get; set; }
    public bool PriceLocked { get; set; }
}

public sealed class MedicineFormViewModel
{
    public int? MedicineId { get; set; }

    /// <summary>Mã thuốc (tự sinh, chỉ hiển thị).</summary>
    public string? MedicineCode { get; set; }

    [Required(ErrorMessage = "Nhập tên thuốc.")]
    [StringLength(250, ErrorMessage = "Tên thuốc tối đa 250 ký tự.")]
    [Display(Name = "Tên thuốc")]
    public string MedicineName { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Mã vạch tối đa 50 ký tự.")]
    [RegularExpression(@"^\s*[0-9A-Za-z\-]*\s*$", ErrorMessage = "Mã vạch chỉ gồm chữ số, chữ cái không dấu và dấu -.")]
    [Display(Name = "Mã vạch")]
    public string? Barcode { get; set; }

    [Required(ErrorMessage = "Chọn nhóm thuốc.")]
    [Display(Name = "Nhóm thuốc")]
    public int? CategoryId { get; set; }

    [Display(Name = "Hãng sản xuất")]
    public int? ManufacturerId { get; set; }

    [StringLength(200, ErrorMessage = "Tên hãng tối đa 200 ký tự.")]
    [Display(Name = "Hãng sản xuất mới")]
    public string? NewManufacturerName { get; set; }

    [StringLength(100)]
    [Display(Name = "Dạng bào chế")]
    public string? DosageForm { get; set; }

    [StringLength(100)]
    [Display(Name = "Hàm lượng")]
    public string? Strength { get; set; }

    [StringLength(80)]
    [Display(Name = "Số đăng ký")]
    public string? RegistrationNumber { get; set; }

    [StringLength(100)]
    [Display(Name = "Xuất xứ")]
    public string? CountryOfOrigin { get; set; }

    [Display(Name = "Thuốc kê đơn")]
    public bool IsPrescription { get; set; }

    [Display(Name = "Thuốc kiểm soát đặc biệt")]
    public bool IsControlledDrug { get; set; }

    [Display(Name = "Liên thông Dược quốc gia")]
    public bool IsInterconnected { get; set; }

    [Display(Name = "Đơn vị cơ sở")]
    public int? BaseUnitId { get; set; }

    [Display(Name = "Giá nhập")]
    public decimal? DefaultPurchasePrice { get; set; }

    [Display(Name = "Giá bán")]
    public decimal? DefaultSellingPrice { get; set; }

    public List<ConversionLine> Conversions { get; set; } = new();
    public List<IngredientLine> Ingredients { get; set; } = new();

    /// <summary>Từ khóa chống chỉ định, mỗi dòng một từ khóa (đối chiếu với chẩn đoán, bệnh mạn tính khi bán theo đơn).</summary>
    [StringLength(3000)]
    [Display(Name = "Từ khóa chống chỉ định")]
    public string? ContraindicationKeywords { get; set; }

    [StringLength(1000)]
    [Display(Name = "Mô tả chống chỉ định")]
    public string? Contraindications { get; set; }

    [StringLength(1000)]
    [Display(Name = "Mô tả tương tác")]
    public string? Interactions { get; set; }

    [StringLength(1000)]
    [Display(Name = "Hướng dẫn sử dụng")]
    public string? UsageInstructions { get; set; }

    [Display(Name = "Liều tối đa 1 lần")]
    public decimal? MaxSingleDose { get; set; }

    [Display(Name = "Liều tối đa 1 ngày")]
    public decimal? MaxDailyDose { get; set; }

    [Range(0, 120, ErrorMessage = "Tuổi tối thiểu từ 0 đến 120.")]
    [Display(Name = "Tuổi tối thiểu")]
    public byte? MinPatientAgeYears { get; set; }

    // ---- Trạng thái hiển thị (server tính, không đọc từ form) ----
    public bool IsEdit => MedicineId is not null;

    /// <summary>Giá bán đã khai báo: chỉ chủ nhà thuốc đổi ở Chính sách giá (1.5.4, DEC-09).</summary>
    public bool PricesLocked { get; set; }

    /// <summary>Thuốc đã có lô, giao dịch hoặc chứng từ: không đổi đơn vị cơ sở, không sửa/xóa dòng quy đổi cũ.</summary>
    public bool IsUsed { get; set; }

    public bool CanEditCategory { get; set; } = true;
    public bool CanEditConversions { get; set; } = true;

    public IReadOnlyList<LookupItem> CategoryOptions { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> UnitOptions { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> ManufacturerOptions { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<string> IngredientNames { get; set; } = Array.Empty<string>();
}

// ------------------------------------------------------------------ Chi tiết

public sealed record ConversionInfo(string UnitName, decimal ConversionToBase, decimal Price, bool PriceIsAuto, bool IsDefaultSaleUnit);

public sealed record PriceHistoryRow(DateTime EffectiveFrom, string UnitName, decimal? OldSellingPrice, decimal SellingPrice,
    decimal? PurchasePrice, string? ChangedBy, string? Reason);

public sealed class MedicineDetailsViewModel
{
    public int MedicineId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Barcode { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string? ManufacturerName { get; init; }
    public string? DosageForm { get; init; }
    public string? Strength { get; init; }
    public string? RegistrationNumber { get; init; }
    public string? CountryOfOrigin { get; init; }
    public bool IsPrescription { get; init; }
    public bool IsControlledDrug { get; init; }
    public bool IsInterconnected { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public string BaseUnitName { get; init; } = string.Empty;
    public decimal DefaultPurchasePrice { get; init; }
    public decimal DefaultSellingPrice { get; init; }
    public IReadOnlyList<ConversionInfo> Conversions { get; init; } = Array.Empty<ConversionInfo>();
    public IReadOnlyList<(string Name, string? Strength)> Ingredients { get; init; } = Array.Empty<(string, string?)>();
    public IReadOnlyList<string> ContraindicationKeywords { get; init; } = Array.Empty<string>();
    public string? Contraindications { get; init; }
    public string? Interactions { get; init; }
    public string? UsageInstructions { get; init; }
    public decimal? MaxSingleDose { get; init; }
    public decimal? MaxDailyDose { get; init; }
    public byte? MinPatientAgeYears { get; init; }
    public decimal QuantityOnHand { get; init; }
    public int BatchCount { get; init; }
    public IReadOnlyList<PriceHistoryRow> PriceHistory { get; init; } = Array.Empty<PriceHistoryRow>();
    public bool CanEdit { get; set; }
}
