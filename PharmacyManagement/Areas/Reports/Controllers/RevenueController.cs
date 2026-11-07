using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Reports;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Reports.Controllers;

/// <summary>
/// Báo cáo doanh thu, lợi nhuận (DEC-46): xem cần F4.1.1 hoặc F4.1.2; giá vốn, lợi nhuận, tổn thất chỉ hiện khi có F4.1.2.
/// "Doanh số của tôi" cần F4.1.1-OWN: chỉ hóa đơn của chính người xem, không có giá vốn.
/// </summary>
[Area("Reports")]
public class RevenueController : Controller
{
    private readonly IRevenueReportService _reports;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public RevenueController(IRevenueReportService reports, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _reports = reports;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] RevenueFilter filter)
    {
        var model = await BuildFullAsync(filter);
        if (model is null) return Forbid();
        ViewData["Nav"] = "revenue";
        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F4_1_1_Own)]
    public async Task<IActionResult> Mine([FromQuery] RevenueFilter filter)
    {
        ViewData["Nav"] = "mysales";
        var model = await BuildMineAsync(filter);
        if (model is null)
        {
            TempData["Error"] = "Tài khoản chưa gắn hồ sơ nhân viên nên chưa có doanh số.";
            return RedirectToAction("Index", "Home", new { area = "" });
        }
        return View("Index", model);
    }

    /// <summary>Bản in A4 của báo cáo đang xem.</summary>
    [HttpGet]
    public async Task<IActionResult> Print([FromQuery] RevenueFilter filter, bool mine = false)
    {
        RevenueReportViewModel? model;
        if (mine)
        {
            if (!await HasAsync(PermissionCodes.F4_1_1_Own)) return Forbid();
            model = await BuildMineAsync(filter);
            if (model is null) return Forbid();
        }
        else
        {
            model = await BuildFullAsync(filter);
            if (model is null) return Forbid();
        }
        return View(model);
    }

    private async Task<RevenueReportViewModel?> BuildFullAsync(RevenueFilter filter)
    {
        var revenue = await HasAsync(PermissionCodes.F4_1_1);
        var profit = await HasAsync(PermissionCodes.F4_1_2);
        if (!revenue && !profit) return null;
        var model = await _reports.BuildAsync(filter, null, profit);
        model.ShowProfit = profit;
        model.ShowEmployees = true;
        return model;
    }

    private async Task<RevenueReportViewModel?> BuildMineAsync(RevenueFilter filter)
    {
        var userId = _users.GetUserId(User);
        if (userId is null) return null;
        var me = await _reports.EmployeeOfAsync(userId);
        if (me is null) return null;
        filter.EmployeeId = null;
        var model = await _reports.BuildAsync(filter, me.Value.EmployeeId, includeProfit: false);
        model.IsMine = true;
        return model;
    }

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;
}
