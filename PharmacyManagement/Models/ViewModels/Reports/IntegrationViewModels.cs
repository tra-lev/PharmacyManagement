using PharmacyManagement.Models.Enums;

namespace PharmacyManagement.Models.ViewModels.Reports;

/// <summary>Liên thông Dược quốc gia (mô phỏng, DEC-03, DEC-50): loại gói tin và chữ hiển thị.</summary>
public static class DqgText
{
    public const string ImportType = "DQG_NHAP_THUOC";
    public const string SaleType = "DQG_BAN_THUOC";
    public const string ImportRef = "GoodsReceipt";
    public const string SaleRef = "SalesInvoice";

    public static readonly IReadOnlyList<string> Types = new[] { ImportType, SaleType };

    public static string Type(string type) => type == ImportType ? "Dữ liệu nhập thuốc" : "Dữ liệu bán thuốc";

    public static string Status(byte status) => status switch
    {
        (byte)IntegrationStatus.Pending => "Chờ gửi",
        (byte)IntegrationStatus.Succeeded => "Đã gửi",
        (byte)IntegrationStatus.Failed => "Lỗi",
        _ => "Không rõ"
    };

    public static string Tone(byte status) => status switch
    {
        (byte)IntegrationStatus.Pending => "wait",
        (byte)IntegrationStatus.Succeeded => "good",
        (byte)IntegrationStatus.Failed => "bad",
        _ => "mute"
    };
}

/// <summary>Một dòng thuốc trong gói tin gửi Dược quốc gia.</summary>
public sealed class DqgLine
{
    public string MaThuoc { get; set; } = string.Empty;
    public string TenThuoc { get; set; } = string.Empty;
    public string? SoDangKy { get; set; }
    public string? SoLo { get; set; }
    public string? HanDung { get; set; }
    public decimal SoLuong { get; set; }
    public string DonViTinh { get; set; } = string.Empty;
    public decimal DonGia { get; set; }
    public bool KeDon { get; set; }
}

/// <summary>Đơn thuốc kèm hóa đơn bán theo đơn.</summary>
public sealed class DqgPrescription
{
    public string MaDon { get; set; } = string.Empty;
    public string? BacSi { get; set; }
    public string? CoSoKham { get; set; }
    public string? NgayKe { get; set; }
}

/// <summary>Gói tin (RequestPayload, JSON). Tên trường tiếng Việt không dấu như dữ liệu mẫu.</summary>
public sealed class DqgPacket
{
    public string Loai { get; set; } = string.Empty;
    public bool MoPhong { get; set; } = true;
    public string CoSo { get; set; } = string.Empty;
    public string SoChungTu { get; set; } = string.Empty;
    public string Ngay { get; set; } = string.Empty;

    /// <summary>Nhập: nhà cung cấp; bán: khách hàng (khách lẻ để trống).</summary>
    public string? DoiTac { get; set; }
    public string? MaSoThue { get; set; }
    public string? SoHoaDonNcc { get; set; }
    public DqgPrescription? DonThuoc { get; set; }
    public List<DqgLine> Thuoc { get; set; } = new();
}

public sealed class IntegrationFilter
{
    public const int PageSize = 50;

    /// <summary>"import" / "sale".</summary>
    public string? Type { get; set; }

    /// <summary>"pending" / "ok" / "failed".</summary>
    public string? Status { get; set; }

    /// <summary>Mã phiếu nhập / mã hóa đơn.</summary>
    public string? Q { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => Type is not null || Status is not null || Q is not null;
}

public sealed record IntegrationRow(
    long LogId,
    string Type,
    string DocumentCode,
    long ReferenceId,
    DateTime CreatedAt,
    DateTime? LastAttemptAt,
    byte Status,
    int RetryCount,
    string? Error);

public sealed class IntegrationListViewModel
{
    public IntegrationFilter Filter { get; init; } = new();
    public IReadOnlyList<IntegrationRow> Rows { get; init; } = Array.Empty<IntegrationRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }

    public int Pending { get; init; }
    public int Failed { get; init; }
    public int SucceededToday { get; init; }

    /// <summary>Chứng từ có thuốc liên thông chưa được tổng hợp (sẽ vào hàng chờ ở lần chạy tới).</summary>
    public int NotQueued { get; init; }

    public DateTime? LastRunAt { get; init; }
    public bool? Online { get; init; }

    public bool CanOpenReceipts { get; set; }
    public bool CanOpenInvoices { get; set; }
}

public sealed class IntegrationDetailsViewModel
{
    public long LogId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string DocumentCode { get; init; } = string.Empty;
    public long ReferenceId { get; init; }
    public byte Status { get; init; }
    public int RetryCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastAttemptAt { get; init; }
    public string? Error { get; init; }
    public string? ReceiptCode { get; init; }

    /// <summary>null nếu gói tin cũ không có chi tiết (dữ liệu mẫu) — bấm Gửi lại để dựng lại.</summary>
    public DqgPacket? Packet { get; init; }

    public bool CanResend { get; set; }
    public bool CanOpenDocument { get; set; }
}

/// <summary>Kết quả một lần chạy tổng hợp + gửi.</summary>
public sealed record DqgRunResult(bool Online, int Queued, int Sent, int Failed, int Waiting);
