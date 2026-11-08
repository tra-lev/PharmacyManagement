using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels;
using PharmacyManagement.Models.ViewModels.Reports;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Controllers;

public class HomeController : Controller
{
    private readonly IStockService _stock;
    private readonly IRevenueReportService _revenue;
    private readonly IAuthorizationService _authorization;

    public HomeController(IStockService stock, IRevenueReportService revenue, IAuthorizationService authorization)
    {
        _stock = stock;
        _revenue = revenue;
        _authorization = authorization;
    }

    /// <summary>
    /// Trang tổng quan. Cảnh báo kho (2.5.1, 2.5.2) hiện cho người thấy mục "Tồn kho &amp; lô" (DEC-39);
    /// số liệu bán hàng hôm nay lấy từ báo cáo doanh thu (DEC-46): toàn nhà thuốc hoặc doanh số của chính người xem.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        ViewData["Nav"] = NavigationMenu.Dashboard;
        var role = User.FindFirstValue(ClaimTypes.Role);
        var fullName = User.FindFirstValue(AppClaimTypes.FullName);
        var (today, todayIsMine, todayShowProfit) = await TodaySalesAsync();
        var model = new HomeIndexViewModel
        {
            FullName = fullName ?? string.Empty,
            NeedsFullName = role == RoleNames.Owner
                && (fullName is null || AccountHelpers.ValidateFullName(fullName, User.Identity?.Name, AccountHelpers.EmailOf(User)) is not null),
            UserName = AccountHelpers.DisplayUserName(User),
            RoleName = role,
            IsOwner = role == RoleNames.Owner,
            EmailVerified = User.HasClaim(AppClaimTypes.EmailVerified, "true"),
            StockAlerts = await CanSeeStockAsync() ? await _stock.GetAlertCountsAsync() : null,
            Today = today,
            TodayIsMine = todayIsMine,
            TodayShowProfit = todayShowProfit
        };
        return View(model);
    }

    /// <summary>Số liệu hôm nay theo quyền: F4.1.1 / F4.1.2 → toàn nhà thuốc; chỉ F4.1.1-OWN → của chính mình; không có → null.</summary>
    private async Task<(RevenueReportViewModel? Report, bool Mine, bool Profit)> TodaySalesAsync()
    {
        var day = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var filter = new RevenueFilter { From = day, To = day, By = "day" };
        var revenue = await HasAsync(PermissionCodes.F4_1_1);
        var profit = await HasAsync(PermissionCodes.F4_1_2);
        if (revenue || profit) return (await _revenue.BuildAsync(filter, null, profit), false, profit);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !await HasAsync(PermissionCodes.F4_1_1_Own)) return (null, false, false);
        var me = await _revenue.EmployeeOfAsync(userId);
        if (me is null) return (null, false, false);
        return (await _revenue.BuildAsync(filter, me.Value.EmployeeId, includeProfit: false), true, false);
    }

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    /// <summary>Cùng điều kiện với mục menu "Tồn kho &amp; lô" (StockController kiểm tra lại khi mở trang).</summary>
    private async Task<bool> CanSeeStockAsync()
    {
        foreach (var code in NavigationMenu.Find("stock")?.AnyOf ?? Array.Empty<string>())
        {
            if ((await _authorization.AuthorizeAsync(User, code)).Succeeded) return true;
        }
        return false;
    }

    /// <summary>Mục menu chưa xây dựng: trang báo rõ, không có nút trang trí.</summary>
    public IActionResult ComingSoon(string? feature)
    {
        var item = NavigationMenu.Find(feature);
        ViewData["Nav"] = item?.Key;
        ViewData["Title"] = item?.Title ?? "Đang xây dựng";
        return View(item);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
