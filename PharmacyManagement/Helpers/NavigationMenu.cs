using PharmacyManagement.Authorization;
using P = PharmacyManagement.Authorization.PermissionCodes;

namespace PharmacyManagement.Helpers;

/// <summary>Một mục menu. Hiện khi người dùng có ÍT NHẤT MỘT mã trong AnyOf (rỗng = ai đăng nhập cũng thấy).</summary>
public sealed record MenuItem(
    string Key,
    string Title,
    string Icon,
    string Url,
    IReadOnlyList<string> AnyOf,
    bool Implemented,
    IReadOnlyList<string>? OnlyRoles = null,
    string? HideIfHas = null);

public sealed record MenuGroup(string Key, string Title, IReadOnlyList<MenuItem> Items);

/// <summary>
/// Cấu trúc menu theo thiết kế đã duyệt (canvas mục 2). Mỗi mục gắn mã chức năng BFD; menu chỉ hiện mục người dùng có quyền,
/// server vẫn kiểm tra quyền ở controller. Mục chưa làm trỏ tới trang “Đang xây dựng”.
/// </summary>
public static class NavigationMenu
{
    public const string Dashboard = "dashboard";
    public const string Accounts = "accounts";

    private static string Soon(string key) => "/Home/ComingSoon?feature=" + key;

    private static IReadOnlyList<string> Any(params string[] codes) => codes;

    public static readonly MenuItem Home = new(Dashboard, "Tổng quan", "grid-1x2", "/", Array.Empty<string>(), true);

    public static readonly IReadOnlyList<MenuGroup> Groups = new[]
    {
        new MenuGroup("sales", "Bán hàng", new[]
        {
            new MenuItem("pos", "Bán lẻ", "cart3", "/Sales/Invoices/Create", Any(P.F3_1_4), true),
            new MenuItem("rx", "Bán theo đơn", "file-earmark-medical", "/Sales/Prescriptions", Any(P.F3_2_1, P.F3_2_4), true),
            new MenuItem("invoices", "Hóa đơn", "receipt", "/Sales/Invoices", Any(P.F3_1_4, P.F3_2_4, P.F3_3_2_1, P.F3_3_2_2, P.F3_3_2_3, P.F4_1_1), true),
            new MenuItem("returns", "Đổi trả", "arrow-counterclockwise", "/Sales/Returns", Any(P.F3_4_1, P.F3_4_2, P.F3_4_4), true),
            new MenuItem("customers", "Khách hàng", "people", "/Catalog/Customers", Any(P.F1_2_2), true),
            new MenuItem("shifts", "Ca làm việc", "clock", "/Sales/Shifts", Any(P.F1_4_1, P.F1_4_2, P.F1_4_3), true),
            new MenuItem("lookup", "Tra cứu thuốc & tồn", "search", "/Inventory/Stock", Any(P.F3_1_1), true, OnlyRoles: new[] { RoleNames.Pharmacist })
        }),
        new MenuGroup("stock", "Kho", new[]
        {
            new MenuItem("stock", "Tồn kho & lô", "box-seam", "/Inventory/Stock", Any(P.F2_2_3, P.F2_3_2, P.F2_3_3_2, P.F2_4_1_1), true),
            new MenuItem("po", "Đặt hàng", "clipboard", "/Inventory/PurchaseOrders", Any(P.F2_1_1, P.F2_1_3), true),
            new MenuItem("gr", "Nhập kho", "truck", "/Inventory/Receipts", Any(P.F2_2_2, P.F2_2_1_1), true),
            new MenuItem("sret", "Trả nhà cung cấp", "box-arrow-right", Soon("sret"), Any(P.F2_3_1), false),
            new MenuItem("disposal", "Hủy thuốc", "trash3", "/Inventory/Disposals", Any(P.F2_3_2), true),
            new MenuItem("recall", "Thu hồi", "lock", Soon("recall"), Any(P.F2_3_3_1, P.F2_3_3_3), false),
            new MenuItem("stocktake", "Kiểm kê", "clipboard-check", Soon("stocktake"), Any(P.F2_4_1_1, P.F2_4_2, P.F2_4_3_Approve), false),
            new MenuItem("env", "Nhiệt độ – độ ẩm", "thermometer-half", Soon("env"), Any(P.F2_5_3), false)
        }),
        new MenuGroup("catalog", "Danh mục", new[]
        {
            new MenuItem("meds", "Thuốc", "capsule", "/Catalog/Medicines", Any(P.F1_1_1, P.F1_1_2), true),
            new MenuItem("suppliers", "Nhà cung cấp", "building", "/Catalog/Suppliers", Any(P.F1_2_1), true),
            new MenuItem("locations", "Tủ, kệ", "grid-3x3", "/Catalog/Locations", Any(P.F1_5_1), true)
        }),
        new MenuGroup("reports", "Báo cáo", new[]
        {
            new MenuItem("revenue", "Doanh thu & lợi nhuận", "bar-chart", Soon("revenue"), Any(P.F4_1_1, P.F4_1_2), false),
            new MenuItem("mysales", "Doanh số của tôi", "bar-chart", Soon("mysales"), Any(P.F4_1_1_Own), false, HideIfHas: P.F4_1_1),
            new MenuItem("nxt", "Nhập – xuất – tồn", "arrow-left-right", Soon("nxt"), Any(P.F4_2_1, P.F4_2_2), false),
            new MenuItem("gpp", "Sổ GPP", "journal-text", Soon("gpp"), Any(P.F4_3_1, P.F4_3_2, P.F4_3_3), false),
            new MenuItem("sync", "Liên thông dữ liệu", "arrow-repeat", Soon("sync"), Any(P.F4_4_2_3), false)
        }),
        new MenuGroup("admin", "Quản trị", new[]
        {
            new MenuItem(Accounts, "Tài khoản", "person-badge", "/Admin/Users", Any(P.F1_3_1), true),
            new MenuItem("roles", "Phân quyền", "shield-lock", Soon("roles"), Any(P.F1_3_3), false),
            new MenuItem("settings", "Thiết lập", "sliders", "/Inventory/Policies", Any(P.F1_5_2, P.F1_5_3), true),
            new MenuItem("audit", "Nhật ký hoạt động", "clock-history", Soon("audit"), Any(P.Sys09ViewAuditLog), false)
        })
    };

    /// <summary>Tên mục theo key (trang “Đang xây dựng” dùng để hiện tiêu đề).</summary>
    public static MenuItem? Find(string? key)
        => key is null ? null : Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Key == key);
}
