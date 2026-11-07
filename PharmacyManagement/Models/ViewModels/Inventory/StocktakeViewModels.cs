using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>Tên và màu nhãn trạng thái phiếu kiểm kê (Stocktakes.Status).</summary>
public static class StocktakeStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)StocktakeStatus.Counting => "Đang kiểm đếm",
        (byte)StocktakeStatus.PendingApproval => "Chờ duyệt",
        (byte)StocktakeStatus.Approved => "Đã duyệt, chờ điều chỉnh",
        (byte)StocktakeStatus.Adjusted => "Đã điều chỉnh",
        (byte)StocktakeStatus.Rejected => "Bị từ chối",
        _ => "Không rõ"
    };

    /// <summary>Lớp màu pm-badge.</summary>
    public static string Tone(byte status) => status switch
    {
        (byte)StocktakeStatus.Counting => "calm",
        (byte)StocktakeStatus.PendingApproval => "warn",
        (byte)StocktakeStatus.Approved => "wait",
        (byte)StocktakeStatus.Adjusted => "good",
        (byte)StocktakeStatus.Rejected => "bad",
        _ => "mute"
    };
}

// ------------------------------------------------------------------ Danh sách

public sealed class StocktakeFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã phiếu, ghi chú (phạm vi).</summary>
    public string? Q { get; set; }

    /// <summary>"counting", "pending", "approved", "adjusted", "rejected" hoặc trống.</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || Status is not null;
}

public sealed record StocktakeRow(
    int StocktakeId,
    string Code,
    DateTime StocktakeDate,
    string Scope,
    int LineCount,
    int DifferenceCount,
    string CreatedByName,
    byte Status);

public sealed class StocktakeListViewModel
{
    public StocktakeFilter Filter { get; init; } = new();
    public IReadOnlyList<StocktakeRow> Rows { get; init; } = Array.Empty<StocktakeRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }

    /// <summary>F2.4.1.1 hoặc F2.4.1.2 — hiện nút Kiểm kê mới.</summary>
    public bool CanCreate { get; set; }
}

// ------------------------------------------------------------------ Phạm vi kiểm kê (2.4.1.1)

/// <summary>Phạm vi kiểm kê: "location" (một tủ hoặc một kệ), "category" (một nhóm thuốc), "all" (toàn kho).</summary>
public sealed class StocktakeScope
{
    public string? Scope { get; set; }
    public int? LocationId { get; set; }
    public int? CategoryId { get; set; }
}

public sealed class StocktakeScopeViewModel
{
    public StocktakeScope Scope { get; init; } = new();

    /// <summary>Tủ và kệ ("Tủ 1", "Tủ 1 › Kệ giảm đau").</summary>
    public IReadOnlyList<LookupItem> Locations { get; init; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Categories { get; init; } = Array.Empty<LookupItem>();

    /// <summary>Phạm vi đã chọn hợp lệ: tên phạm vi và số lô còn tồn; lỗi nếu chọn chưa đủ.</summary>
    public string? ScopeLabel { get; init; }
    public int LotCount { get; init; }
    public string? ScopeError { get; init; }

    /// <summary>F2.4.1.1 — in phiếu kiểm kê.</summary>
    public bool CanPrint { get; set; }

    /// <summary>F2.4.1.2 — bắt đầu nhập số lượng thực tế (tạo phiếu).</summary>
    public bool CanStart { get; set; }
}

public sealed record CountSheetLine(string? Location, string MedicineCode, string MedicineName, string BatchNumber, DateOnly ExpiryDate, string BaseUnit);

/// <summary>Phiếu kiểm kê để in và đếm tay — không in số tồn hệ thống (DEC-43).</summary>
public sealed class CountSheetViewModel
{
    public string? StocktakeCode { get; init; }
    public string ScopeLabel { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public string PharmacyName { get; init; } = string.Empty;
    public DateTime PrintedAt { get; init; }
    public IReadOnlyList<CountSheetLine> Lines { get; init; } = Array.Empty<CountSheetLine>();
}

// ------------------------------------------------------------------ Nhập số thực tế (2.4.1.2) + lập biên bản (2.4.2)

public sealed class StocktakeLineInput
{
    public int StocktakeDetailId { get; set; }

    [Display(Name = "Số lượng thực tế")]
    public decimal? ActualQuantity { get; set; }

    [StringLength(500, ErrorMessage = "Lý do chênh lệch tối đa 500 ký tự.")]
    public string? Reason { get; set; }
}

public sealed class StocktakeCountForm
{
    public List<StocktakeLineInput> Lines { get; set; } = new();

    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }
}

// ------------------------------------------------------------------ Chi tiết / biên bản

public sealed record StocktakeLineView(
    int StocktakeDetailId,
    int BatchId,
    string? Location,
    string MedicineCode,
    string MedicineName,
    string BatchNumber,
    DateOnly ExpiryDate,
    string BaseUnit,
    decimal SystemQuantity,
    decimal ActualQuantity,
    decimal Difference,
    decimal UnitCost,
    string? Reason,
    decimal CurrentOnHand);

public sealed class StocktakeDetailsViewModel
{
    public int StocktakeId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public DateTime StocktakeDate { get; init; }
    public string WarehouseName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public string CreatedByName { get; init; } = string.Empty;
    public string? ApprovedByName { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? ApprovalNote { get; init; }
    public string? AdjustedByName { get; init; }
    public DateTime? AdjustedAt { get; init; }
    public IReadOnlyList<StocktakeLineView> Lines { get; init; } = Array.Empty<StocktakeLineView>();
    public string PharmacyName { get; init; } = string.Empty;

    public bool IsCounting => Status == (byte)StocktakeStatus.Counting;
    public int DifferenceCount => Lines.Count(l => l.Difference != 0);

    /// <summary>Giá trị hàng thiếu (số âm) và thừa theo giá vốn lô, làm tròn đồng.</summary>
    public decimal ShortageValue => Math.Round(Lines.Where(l => l.Difference < 0).Sum(l => l.Difference * l.UnitCost), 0, MidpointRounding.AwayFromZero);
    public decimal SurplusValue => Math.Round(Lines.Where(l => l.Difference > 0).Sum(l => l.Difference * l.UnitCost), 0, MidpointRounding.AwayFromZero);

    /// <summary>Số vừa nhập khi trang nhập số thực tế báo lỗi (theo StocktakeDetailId).</summary>
    public IReadOnlyDictionary<int, StocktakeLineInput> Entered { get; set; } = new Dictionary<int, StocktakeLineInput>();
    public string? EnteredNotes { get; set; }

    public bool CanCount { get; set; }
    public bool CanSubmit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanApprove { get; set; }
    public bool CanAdjust { get; set; }
}
