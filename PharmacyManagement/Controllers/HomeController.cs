using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels;

namespace PharmacyManagement.Controllers;

public class HomeController : Controller
{
    /// <summary>Trang tổng quan. Số liệu theo vai trò sẽ có khi các module bán hàng, kho hoàn thành — chưa hiện số giả.</summary>
    public IActionResult Index()
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
            EmailVerified = User.HasClaim(AppClaimTypes.EmailVerified, "true")
        };
        return View(model);
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
