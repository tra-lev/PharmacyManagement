using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Reports;

// ------------------------------------------------------------------ Nhập – xuất – tồn (4.2.1)

public sealed class StockMovementFilter
{
    public const int PageSize = 50;

    /// <summary>yyyy-MM-dd.</summary>
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Q { get; set; }
    public int? CategoryId { get; set; }

    /// <summary>"moved" = chỉ thuốc có nhập hoặc xuất trong kỳ.</summary>
    public string? Show { get; set; }

    public int Page { get; set; } = 1;
}

/// <summary>Số lượng (đơn vị cơ sở) và giá trị (giá vốn lô).</summary>
public readonly record struct QtyValue(decimal Quantity, decimal Value)
{
    public static QtyValue operator +(QtyValue a, QtyValue b) => new(a.Quantity + b.Quantity, a.Value + b.Value);
    public static QtyValue operator -(QtyValue a, QtyValue b) => new(a.Quantity - b.Quantity, a.Value - b.Value);
    public bool IsZero => Quantity == 0 && Value == 0;
}

public sealed class StockMovementRow
{
    public int MedicineId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string BaseUnit { get; init; } = string.Empty;
    public QtyValue Opening { get; init; }

    /// <summary>Nhập mua (sổ kho loại 1).</summary>
    public QtyValue Purchased { get; init; }

    /// <summary>Nhập khác: khách trả nhập lại (7), kiểm kê thừa (6, dương).</summary>
    public QtyValue OtherIn { get; init; }

    /// <summary>Xuất bán (2), số dương.</summary>
    public QtyValue Sold { get; init; }

    /// <summary>Xuất khác: trả NCC (3), hủy (4), thu hồi (5), kiểm kê thiếu (6, âm) — số dương.</summary>
    public QtyValue OtherOut { get; init; }

    public QtyValue In => Purchased + OtherIn;
    public QtyValue Out => Sold + OtherOut;
    public QtyValue Closing => Opening + In - Out;
    public bool Moved => !In.IsZero || !Out.IsZero;
}

/// <summary>Tổng giá trị theo từng loại biến động trong kỳ.</summary>
public sealed record MovementTypeRow(string Name, bool IsIn, decimal Value);

public sealed class StockMovementViewModel
{
    public StockMovementFilter Filter { get; init; } = new();
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string? FilterError { get; init; }
    public string PharmacyName { get; init; } = string.Empty;

    /// <summary>Trang hiện tại (bản in: toàn bộ).</summary>
    public IReadOnlyList<StockMovementRow> Rows { get; init; } = Array.Empty<StockMovementRow>();
    public int Total { get; init; }
    public int PageCount { get; init; }

    // ---- Tổng giá trị (mọi dòng khớp bộ lọc) ----
    public decimal OpeningValue { get; init; }
    public decimal InValue { get; init; }
    public decimal OutValue { get; init; }
    public decimal ClosingValue => OpeningValue + InValue - OutValue;

    public IReadOnlyList<MovementTypeRow> ByType { get; init; } = Array.Empty<MovementTypeRow>();
    public IReadOnlyList<LookupItem> Categories { get; init; } = Array.Empty<LookupItem>();
    public string? CategoryName { get; init; }

    public bool CanOpenMedicine { get; set; }
    public bool CanSeeExpiry { get; set; }
}

// ------------------------------------------------------------------ Thuốc cận hạn (4.2.2)

public sealed class ExpiryFilter
{
    public string? Q { get; set; }
    public int? CategoryId { get; set; }

    /// <summary>"expired" chỉ lô đã hết hạn, "near" chỉ lô cận hạn; trống = cả hai.</summary>
    public string? Show { get; set; }
}

public sealed record ExpiryRow(
    int BatchId,
    int MedicineId,
    string MedicineCode,
    string MedicineName,
    string BaseUnit,
    string BatchNumber,
    DateOnly ExpiryDate,
    int DaysLeft,
    int WarningDays,
    decimal Quantity,
    decimal UnitCost,
    byte Status,
    string? Location,
    string? SupplierName)
{
    public bool Expired => DaysLeft <= 0;
    public decimal Value => Quantity * UnitCost;
}

public sealed class ExpiryReportViewModel
{
    public ExpiryFilter Filter { get; init; } = new();
    public DateOnly Today { get; init; }
    public int DefaultWarningDays { get; init; }
    public string PharmacyName { get; init; } = string.Empty;
    public IReadOnlyList<ExpiryRow> Rows { get; init; } = Array.Empty<ExpiryRow>();
    public IReadOnlyList<LookupItem> Categories { get; init; } = Array.Empty<LookupItem>();
    public string? CategoryName { get; init; }

    public int ExpiredLots => Rows.Count(r => r.Expired);
    public decimal ExpiredValue => Rows.Where(r => r.Expired).Sum(r => r.Value);
    public int NearLots => Rows.Count(r => !r.Expired);
    public decimal NearValue => Rows.Where(r => !r.Expired).Sum(r => r.Value);

    public bool CanOpenBatch { get; set; }
    public bool CanDispose { get; set; }
    public bool CanSeeMovement { get; set; }
}
