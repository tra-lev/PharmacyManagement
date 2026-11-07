using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>Bộ lọc trang Định mức tồn &amp; ngưỡng cận hạn (query string).</summary>
public sealed class PolicyFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo tên thuốc, mã thuốc, mã vạch, hoạt chất.</summary>
    public string? Q { get; set; }

    public int? CategoryId { get; set; }

    /// <summary>"unset" chưa đặt mức tối thiểu, "custom" có số ngày cận hạn riêng, "below" dưới mức tối thiểu.</summary>
    public string? Status { get; set; }

    /// <summary>"all" = gồm cả thuốc ngừng kinh doanh.</summary>
    public string? Show { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || CategoryId is not null || Status is not null || Show is not null;
}

/// <summary>Một thuốc trên trang thiết lập. Minimum = 0 nghĩa là chưa đặt; WarningDays null = dùng ngưỡng chung.</summary>
public sealed record PolicyRow(
    int MedicineId,
    string Code,
    string Name,
    string BaseUnit,
    bool IsActive,
    decimal Available,
    decimal Minimum,
    int? WarningDays);

public sealed class PolicyListViewModel
{
    public PolicyFilter Filter { get; init; } = new();
    public IReadOnlyList<PolicyRow> Rows { get; set; } = Array.Empty<PolicyRow>();
    public int Total { get; init; }

    /// <summary>Tổng số thuốc trong danh mục (mọi trạng thái).</summary>
    public int GrandTotal { get; init; }

    public int PageCount { get; init; }
    public IReadOnlyList<LookupItem> Categories { get; init; } = Array.Empty<LookupItem>();

    /// <summary>Ngưỡng cận hạn chung (PharmacySettings.ExpiryWarningDays).</summary>
    public int DefaultWarningDays { get; set; }

    /// <summary>F1.5.2 — sửa mức tồn tối thiểu.</summary>
    public bool CanEditMinimum { get; set; }

    /// <summary>F1.5.3 — sửa số ngày cận hạn (chung và riêng từng thuốc).</summary>
    public bool CanEditWarning { get; set; }
}

/// <summary>Một dòng gửi lên khi bấm Lưu trang này.</summary>
public sealed class PolicyRowInput
{
    public int MedicineId { get; set; }

    /// <summary>Đơn vị cơ sở; để trống = 0 (không cảnh báo dưới định mức).</summary>
    public decimal? Minimum { get; set; }

    /// <summary>Để trống = dùng ngưỡng chung.</summary>
    public int? WarningDays { get; set; }
}

/// <summary>Form lưu một trang thuốc; kèm bộ lọc để quay lại đúng trang.</summary>
public sealed class PolicySaveForm
{
    public const int MaxDays = 3650;
    public const decimal MaxMinimum = 1_000_000m;

    public List<PolicyRowInput> Rows { get; set; } = new();

    public string? Q { get; set; }
    public int? CategoryId { get; set; }
    public string? Status { get; set; }
    public string? Show { get; set; }
    public int Page { get; set; } = 1;
}
