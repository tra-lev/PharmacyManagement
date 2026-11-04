using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Helpers;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.ViewComponents;

/// <summary><paramref name="Badge"/>: số việc đang chờ hiện cạnh mục menu (ví dụ yêu cầu đổi trả chờ quản lý).</summary>
public sealed record SidebarItemViewModel(MenuItem Item, bool Active, bool NeedsVerifiedEmail, int Badge = 0);

public sealed record SidebarGroupViewModel(string Key, string Title, IReadOnlyList<SidebarItemViewModel> Items, bool Open);

public sealed record SidebarViewModel(SidebarItemViewModel Home, IReadOnlyList<SidebarGroupViewModel> Groups);

/// <summary>Sidebar dựng theo quyền của người đăng nhập (RolePermissions + khóa cứng SYS-13, DEC-18).</summary>
public sealed class SidebarViewComponent : ViewComponent
{
    private readonly IPermissionService _permissions;
    private readonly IReturnService _returns;

    public SidebarViewComponent(IPermissionService permissions, IReturnService returns)
    {
        _permissions = permissions;
        _returns = returns;
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

        var groups = new List<SidebarGroupViewModel>();
        foreach (var group in NavigationMenu.Groups)
        {
            var items = group.Items
                .Where(i => i.OnlyRoles is null || i.OnlyRoles.Contains(role))
                .Where(i => i.HideIfHas is null || !codes.Contains(i.HideIfHas))
                .Where(i => i.AnyOf.Count == 0 || i.AnyOf.Any(codes.Contains))
                .Select(i => new SidebarItemViewModel(i, i.Key == activeKey,
                    !emailVerified && i.AnyOf.Count > 0 && i.AnyOf.All(PermissionCodes.RequiresVerifiedEmail),
                    i.Key == "returns" ? returnRequests : 0))
                .ToList();
            if (items.Count == 0) continue;
            var open = group.Key == primaryGroup || items.Any(i => i.Active);
            groups.Add(new SidebarGroupViewModel(group.Key, group.Title, items, open));
        }

        var home = new SidebarItemViewModel(NavigationMenu.Home, activeKey is null or NavigationMenu.Dashboard, false);
        return View(new SidebarViewModel(home, groups));
    }
}
