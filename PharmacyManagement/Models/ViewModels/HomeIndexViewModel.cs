namespace PharmacyManagement.Models.ViewModels;

public sealed class HomeIndexViewModel
{
    public string FullName { get; init; } = string.Empty;

    /// <summary>Tên tài khoản (phần trước @ của email đăng nhập) — dùng để chào, không dùng họ tên.</summary>
    public string UserName { get; init; } = string.Empty;
    public string? RoleName { get; init; }
    public bool IsOwner { get; init; }
    public bool EmailVerified { get; init; }

    /// <summary>Owner chưa có họ tên thật trong hồ sơ nhân viên.</summary>
    public bool NeedsFullName { get; init; }

    /// <summary>Cảnh báo kho; null khi người dùng không có quyền xem tồn kho.</summary>
    public Inventory.StockAlertCounts? StockAlerts { get; init; }

    /// <summary>
    /// Số liệu bán hàng hôm nay (cùng cách tính báo cáo doanh thu, DEC-46): toàn nhà thuốc nếu có F4.1.1 / F4.1.2,
    /// của chính người xem nếu chỉ có F4.1.1-OWN; null khi không có quyền xem → ẩn ô.
    /// </summary>
    public Reports.RevenueReportViewModel? Today { get; init; }

    /// <summary>Ô số liệu là doanh số của chính người xem.</summary>
    public bool TodayIsMine { get; init; }

    /// <summary>Hiện lợi nhuận gộp (F4.1.2).</summary>
    public bool TodayShowProfit { get; init; }
}
