using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Helpers;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.ViewComponents;

/// <summary>
/// <paramref name="Badge"/>: số việc đang chờ hiện cạnh mục menu (ví dụ yêu cầu đổi trả chờ quản lý, cảnh báo tồn kho);
/// <paramref name="BadgeTitle"/>: chữ hiện khi rê chuột.
/// </summary>
public sealed record SidebarItemViewModel(MenuItem Item, bool Active, bool NeedsVerifiedEmail, int Badge = 0, string? BadgeTitle = null);

public sealed record SidebarGroupViewModel(string Key, string Title, IReadOnlyList<SidebarItemViewModel> Items, bool Open);

public sealed record SidebarViewModel(SidebarItemViewModel Home, IReadOnlyList<SidebarGroupViewModel> Groups);

/// <summary>Sidebar dựng theo quyền của người đăng nhập (RolePermissions + khóa cứng SYS-13, DEC-18).</summary>
public sealed class SidebarViewComponent : ViewComponent
{
    private readonly IPermissionService _permissions;
    private readonly IReturnService _returns;
    private readonly IStockService _stock;

    public SidebarViewComponent(IPermissionService permissions, IReturnService returns, IStockService stock)
    {
        _permissions = permissions;
        _returns = returns;
        _stock = stock;
    }

    public async Task<IViewComponentResult> InvokeAsync(string? activeKey)
    {
        var user = UserClaimsPrincipal;
        var role = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var codes = await _permissions.GetCodesAsync(role);
        var emailVerified = user.HasClaim(AppClaimTypes.EmailVerified, "true");
        var primaryGroup = role == RoleNames.Pharmacist ? "sales" : "stock";
        // Yêu cầu đổi trả dược sĩ gửi, chờ người có quyền kiểm tra + hoàn tiền
        var returnRequests = codes.Contains(PermissionCodes.F3_4_2) && codes.Contains(PermissionCodes.F3_4_3)
            ? await _returns.PendingRequestCountAsync()
            : 0;
        // Cảnh báo kho (2.5.1, 2.5.2): thuốc dưới mức tối thiểu + lô cận hạn + lô hết hạn còn tồn — cho người thấy mục "Tồn kho & lô"
        var stockItem = NavigationMenu.Find("stock");
        var stockAlerts = stockItem is not null && stockItem.AnyOf.Any(codes.Contains)
            ? (await _stock.GetAlertCountsAsync()).ActionCount
            : 0;

        var groups = new List<SidebarGroupViewModel>();
        foreach (var group in NavigationMenu.Groups)
        {
            var items = group.Items
                .Where(i => i.OnlyRoles is null || i.OnlyRoles.Contains(role))
                .Where(i => i.HideIfHas is null || !codes.Contains(i.HideIfHas))
                .Where(i => i.AnyOf.Count == 0 || i.AnyOf.Any(codes.Contains))
                .Select(i => new SidebarItemViewModel(i, i.Key == activeKey,
                    !emailVerified && i.AnyOf.Count > 0 && i.AnyOf.All(PermissionCodes.RequiresVerifiedEmail),
                    i.Key switch { "returns" => returnRequests, "stock" => stockAlerts, _ => 0 },
                    i.Key == "stock" ? "Thuốc dưới mức tối thiểu, lô cận hạn và lô hết hạn" : null))
                .ToList();
            if (items.Count == 0) continue;
            var open = group.Key == primaryGroup || items.Any(i => i.Active);
            groups.Add(new SidebarGroupViewModel(group.Key, group.Title, items, open));
        }

        var home = new SidebarItemViewModel(NavigationMenu.Home, activeKey is null or NavigationMenu.Dashboard, false);
        return View(new SidebarViewModel(home, groups));
    }
}
