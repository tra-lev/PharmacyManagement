using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Admin;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Admin.Controllers;

/// <summary>Nhật ký hoạt động (SYS-09, DEC-48): chỉ xem; cần quyền xem nhật ký và email đã xác minh.</summary>
[Area("Admin")]
[Authorize(Policy = PermissionCodes.Sys09ViewAuditLog)]
public class AuditLogsController : Controller
{
    private const string Nav = "audit";

    private readonly IAuditLogQueryService _logs;

    public AuditLogsController(IAuditLogQueryService logs) => _logs = logs;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AuditLogFilter filter)
    {
        ViewData["Nav"] = Nav;
        return View(await _logs.GetListAsync(filter));
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id)
    {
        ViewData["Nav"] = Nav;
        var model = await _logs.GetDetailsAsync(id);
        return model is null ? NotFound() : View(model);
    }
}
