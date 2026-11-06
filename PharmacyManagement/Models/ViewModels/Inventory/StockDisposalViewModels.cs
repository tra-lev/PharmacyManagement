using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PharmacyManagement.Models.Enums;

namespace PharmacyManagement.Models.ViewModels.Inventory;

/// <summary>Tên và màu nhãn trạng thái biên bản hủy (StockDisposals.Status).</summary>
public static class DisposalStatusText
{
    public static string Of(byte status) => status switch
    {
        (byte)DisposalStatus.Draft => "Nháp",
        (byte)DisposalStatus.Posted => "Đã ghi sổ",
        (byte)DisposalStatus.Cancelled => "Đã hủy",
        _ => "Không rõ"
    };

    /// <summary>Lớp màu pm-badge.</summary>
    public static string Tone(byte status) => status switch
    {
        (byte)DisposalStatus.Draft => "wait",
        (byte)DisposalStatus.Posted => "good",
        _ => "mute"
    };
}

/// <summary>Lý do hủy từng lô (StockDisposalDetails.ReasonType).</summary>
public static class DisposalReasonText
{
    public static readonly IReadOnlyList<byte> All = new[]
    {
        (byte)DisposalReason.Expired, (byte)DisposalReason.Damaged, (byte)DisposalReason.Dented, (byte)DisposalReason.Other
    };

    public static string Of(byte reason) => reason switch
    {
        (byte)DisposalReason.Expired => "Hết hạn",
        (byte)DisposalReason.Damaged => "Hỏng",
        (byte)DisposalReason.Dented => "Móp méo",
        (byte)DisposalReason.Other => "Khác",
        _ => "Không rõ"
    };
}

// ------------------------------------------------------------------ Danh sách

/// <summary>Bộ lọc danh sách biên bản hủy (query string).</summary>
public sealed class DisposalFilter
{
    public const int PageSize = 20;

    /// <summary>Tìm theo mã biên bản, lý do, tên thuốc.</summary>
    public string? Q { get; set; }

    /// <summary>"draft", "posted", "cancelled" hoặc trống (tất cả).</summary>
    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Q is not null || Status is not null;
}

public sealed record DisposalRow(
    int DisposalId,
    string Code,
    DateTime DisposalDate,
    string Reason,
    int LineCount,
    decimal TotalValue,
    string CreatedByName,
    byte Status);

public sealed class DisposalListViewModel
{
    public DisposalFilter Filter { get; init; } = new();
    public IReadOnlyList<DisposalRow> Rows { get; init; } = Array.Empty<DisposalRow>();
    public int Total { get; init; }

    /// <summary>Tổng số biên bản trong CSDL (mọi trạng thái).</summary>
    public int GrandTotal { get; init; }

    public int PageCount { get; init; }
}

// ------------------------------------------------------------------ Lập / sửa biên bản (Nháp)

/// <summary>Một dòng hủy: một lô, số lượng theo đơn vị cơ sở.</summary>
public sealed class DisposalLineForm
{
    public int? BatchId { get; set; }

    [Display(Name = "Số lượng hủy")]
    public decimal? Quantity { get; set; }

    /// <summary>1 Hết hạn, 2 Hỏng, 3 Móp méo, 4 Khác.</summary>
    [Display(Name = "Lý do")]
    public byte? ReasonType { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú dòng tối đa 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? ReasonNote { get; set; }
}

/// <summary>Lô chọn được trong form: còn hàng, không đang thu hồi.</summary>
public sealed record DisposalBatchOption(
    int BatchId,
    string Label,
    string Keywords,
    decimal Quantity,
    decimal UnitCost,
    string BaseUnit,
    bool Expired);

public sealed class DisposalFormViewModel
{
    public const int MaxLines = 100;

    public int? DisposalId { get; set; }

    /// <summary>Mã biên bản (tự sinh, chỉ hiển thị).</summary>
    public string? Code { get; set; }

    [Required(ErrorMessage = "Nhập lý do hủy.")]
    [StringLength(500, ErrorMessage = "Lý do hủy tối đa 500 ký tự.")]
    [Display(Name = "Lý do hủy")]
    public string? Reason { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Notes { get; set; }

    public List<DisposalLineForm> Lines { get; set; } = new();

    // ---- Hiển thị (server điền) ----
    public bool IsEdit => DisposalId is not null;

    /// <summary>Biên bản còn ở trạng thái Nháp (form sửa chỉ mở khi đúng); không nhận từ form gửi lên.</summary>
    [BindNever]
    public bool IsDraft { get; set; } = true;
    public IReadOnlyList<DisposalBatchOption> BatchOptions { get; set; } = Array.Empty<DisposalBatchOption>();

    /// <summary>Thông báo khi điền sẵn (ví dụ có nhiều lô hết hạn hơn số dòng tối đa).</summary>
    public string? PrefillNote { get; set; }
}

// ------------------------------------------------------------------ Chi tiết

public sealed record DisposalLineView(
    int BatchId,
    string MedicineCode,
    string MedicineName,
    string BatchNumber,
    DateOnly ExpiryDate,
    string? Location,
    decimal Quantity,
    string BaseUnit,
    decimal UnitCost,
    decimal Value,
    byte ReasonType,
    string? ReasonNote,
    decimal BatchOnHand,
    byte LotStatus);

public sealed class DisposalDetailsViewModel
{
    public int DisposalId { get; init; }
    public string Code { get; init; } = string.Empty;
    public byte Status { get; init; }
    public DateTime DisposalDate { get; init; }
    public string WarehouseName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public string CreatedByName { get; init; } = string.Empty;

    /// <summary>Thời điểm ghi sổ (lấy từ sổ kho); null khi chưa ghi sổ.</summary>
    public DateTime? PostedAt { get; init; }

    public IReadOnlyList<DisposalLineView> Lines { get; init; } = Array.Empty<DisposalLineView>();
    public decimal TotalValue { get; init; }
    public string PharmacyName { get; init; } = string.Empty;

    public bool IsDraft => Status == (byte)DisposalStatus.Draft;
    public bool CanEdit { get; set; }
}
