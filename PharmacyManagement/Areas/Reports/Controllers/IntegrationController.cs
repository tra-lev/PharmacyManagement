using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Reports;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Reports.Controllers;

/// <summary>Liên thông Dược quốc gia — 4.4.2.3 xử lý lỗi gửi dữ liệu (F4.4.2.3, DEC-50). Gửi tự động do tác vụ chạy ngầm đảm nhận.</summary>
[Area("Reports")]
[Authorize(Policy = PermissionCodes.F4_4_2_3)]
public class IntegrationController : Controller
{
    private const string Nav = "sync";

    private readonly IDqgSyncService _sync;
    private readonly IAuthorizationService _authorization;
    private readonly IAuditLogService _audit;
    private readonly UserManager<IdentityUser> _users;

    public IntegrationController(IDqgSyncService sync, IAuthorizationService authorization, IAuditLogService audit, UserManager<IdentityUser> users)
    {
        _sync = sync;
        _authorization = authorization;
        _audit = audit;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] IntegrationFilter filter)
    {
        ViewData["Nav"] = Nav;
        var model = await _sync.GetListAsync(filter);
        model.CanOpenReceipts = await HasAsync(PermissionCodes.F2_2_1_1) || await HasAsync(PermissionCodes.F2_2_2);
        model.CanOpenInvoices = await HasAsync(PermissionCodes.F4_1_1);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id)
    {
        ViewData["Nav"] = Nav;
        var model = await _sync.GetDetailsAsync(id);
        if (model is null) return NotFound();
        model.CanResend = model.Status == (byte)Models.Enums.IntegrationStatus.Failed;
        model.CanOpenDocument = model.Type == DqgText.ImportType
            ? await HasAsync(PermissionCodes.F2_2_1_1) || await HasAsync(PermissionCodes.F2_2_2)
            : await HasAsync(PermissionCodes.F4_1_1);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Resend(long id)
    {
        var result = await _sync.ResendAsync(id, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ResendAll()
    {
        var result = await _sync.ResendAllFailedAsync(ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Tổng hợp và gửi ngay, không chờ lần chạy tự động kế tiếp.</summary>
    [HttpPost]
    public async Task<IActionResult> RunNow()
    {
        var r = await _sync.RunAsync();
        await _audit.WriteAsync("INTEGRATION_RUN", "IntegrationLogs", null, null,
            new { r.Online, r.Queued, r.Sent, r.Failed, r.Waiting }, ActorId());
        if (!r.Online)
            TempData["Warning"] = $"Đang mất kết nối mạng. Đã tổng hợp thêm {r.Queued} chứng từ; {r.Waiting} gói tin đang chờ, sẽ tự gửi khi có mạng.";
        else if (r.Failed > 0)
            TempData["Error"] = $"Đã tổng hợp {r.Queued} chứng từ, gửi được {r.Sent}, {r.Failed} gói tin bị từ chối — xem danh sách lỗi.";
        else
            TempData["Success"] = r.Queued + r.Sent == 0
                ? "Không có dữ liệu mới cần gửi."
                : $"Đã tổng hợp {r.Queued} chứng từ, gửi được {r.Sent} gói tin.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
