using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Controllers;

public class HomeController : Controller
{
    private readonly IStockService _stock;
    private readonly IAuthorizationService _authorization;

    public HomeController(IStockService stock, IAuthorizationService authorization)
    {
        _stock = stock;
        _authorization = authorization;
    }

    /// <summary>
    /// Trang tổng quan. Cảnh báo kho (2.5.1, 2.5.2) hiện cho người thấy mục "Tồn kho &amp; lô" (DEC-39);
    /// doanh thu sẽ có khi làm phân hệ Báo cáo — chưa hiện số giả.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        ViewData["Nav"] = NavigationMenu.Dashboard;
        var role = User.FindFirstValue(ClaimTypes.Role);
        var fullName = User.FindFirstValue(AppClaimTypes.FullName);
        var model = new HomeIndexViewModel
        {
            FullName = fullName ?? string.Empty,
            NeedsFullName = role == RoleNames.Owner
                && (fullName is null || AccountHelpers.ValidateFullName(fullName, User.Identity?.Name, AccountHelpers.EmailOf(User)) is not null),
            UserName = AccountHelpers.DisplayUserName(User),
            RoleName = role,
            IsOwner = role == RoleNames.Owner,
            EmailVerified = User.HasClaim(AppClaimTypes.EmailVerified, "true"),
            StockAlerts = await CanSeeStockAsync() ? await _stock.GetAlertCountsAsync() : null
        };
        return View(model);
    }

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
