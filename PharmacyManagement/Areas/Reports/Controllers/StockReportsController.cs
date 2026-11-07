using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Reports;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Reports.Controllers;

/// <summary>Báo cáo nhập – xuất – tồn (F4.2.1) và thuốc cận hạn (F4.2.2) — DEC-49.</summary>
[Area("Reports")]
public class StockReportsController : Controller
{
    private const string Nav = "nxt";

    /// <summary>Cùng điều kiện xem chi tiết lô của StockController.</summary>
    private static readonly string[] StockViewCodes =
    {
        PermissionCodes.F2_2_3, PermissionCodes.F2_3_2, PermissionCodes.F2_3_3_2, PermissionCodes.F2_4_1_1
    };

    private readonly IStockReportService _reports;
    private readonly IAuthorizationService _authorization;

    public StockReportsController(IStockReportService reports, IAuthorizationService authorization)
    {
        _reports = reports;
        _authorization = authorization;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] StockMovementFilter filter)
    {
        if (!await HasAsync(PermissionCodes.F4_2_1))
            return await HasAsync(PermissionCodes.F4_2_2) ? RedirectToAction(nameof(Expiry)) : Forbid();
        ViewData["Nav"] = Nav;
        var model = await _reports.GetMovementAsync(filter, all: false);
        model.CanOpenMedicine = await AnyAsync(StockViewCodes) || await HasAsync(PermissionCodes.F3_1_1);
        model.CanSeeExpiry = await HasAsync(PermissionCodes.F4_2_2);
        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F4_2_1)]
    public async Task<IActionResult> Print([FromQuery] StockMovementFilter filter)
        => View(await _reports.GetMovementAsync(filter, all: true));

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F4_2_2)]
    public async Task<IActionResult> Expiry([FromQuery] ExpiryFilter filter)
    {
        ViewData["Nav"] = Nav;
        var model = await _reports.GetExpiryAsync(filter);
        model.CanOpenBatch = await AnyAsync(StockViewCodes);
        model.CanDispose = await HasAsync(PermissionCodes.F2_3_2);
        model.CanSeeMovement = await HasAsync(PermissionCodes.F4_2_1);
        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F4_2_2)]
    public async Task<IActionResult> ExpiryPrint([FromQuery] ExpiryFilter filter)
        => View(await _reports.GetExpiryAsync(filter));

    private async Task<bool> AnyAsync(IEnumerable<string> codes)
    {
        foreach (var code in codes)
        {
            if (await HasAsync(code)) return true;
        }
        return false;
    }

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;
}
