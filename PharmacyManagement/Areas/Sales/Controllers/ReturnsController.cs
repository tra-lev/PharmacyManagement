using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Sales.Controllers;

/// <summary>
/// Đổi trả — BFD 3.4.1 tiếp nhận (tra hóa đơn gốc, chỉ xem), 3.4.2 kiểm tra điều kiện + 3.4.3 hoàn tiền (cần cả hai quyền),
/// 3.4.4 nhập lại tồn hoặc không nhập. Mỗi thao tác kiểm tra mã quyền ở server.
/// </summary>
[Area("Sales")]
public class ReturnsController : Controller
{
    private const string Nav = "returns";

    private readonly IReturnService _returns;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public ReturnsController(IReturnService returns, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _returns = returns;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReturnFilter filter)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _returns.GetListAsync(filter);
        model.CanLookup = await CanLookupAsync();
        model.CanProcess = await CanProcessAsync();
        // Quản lý thấy mọi yêu cầu hôm nay; dược sĩ thấy yêu cầu mình đã gửi và kết quả
        if (model.CanProcess) model.Requests = await _returns.GetRequestsTodayAsync(null);
        else if (await HasAsync(PermissionCodes.F3_4_1)) model.Requests = await _returns.GetRequestsTodayAsync(UserId());
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _returns.GetDetailsAsync(id);
        if (model is null) return NotFound();
        model.CanRestock = await HasAsync(PermissionCodes.F3_4_4);
        return View(model);
    }

    // ---------------- 3.4.1 Tiếp nhận: tra hóa đơn gốc ----------------

    [HttpGet]
    public async Task<IActionResult> Lookup(string? q)
    {
        if (!await CanLookupAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        return View(await _returns.LookupAsync(q));
    }

    // ---------------- 3.4.2 Kiểm tra điều kiện + 3.4.3 hoàn tiền (người chỉ có 3.4.1 thì xem) ----------------

    /// <param name="request">Mở từ yêu cầu dược sĩ đã gửi (quản lý xử lý).</param>
    [HttpGet]
    public async Task<IActionResult> Create(int id, long? request)
    {
        if (!await CanLookupAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var canProcess = await CanProcessAsync();
        var model = await _returns.NewFormAsync(id, UserId(), canProcess ? request : null);
        if (model is null) return NotFound();
        model.CanProcess = canProcess;
        model.CanRequest = !canProcess && await HasAsync(PermissionCodes.F3_4_1);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F3_4_2)]
    public async Task<IActionResult> Create(int id, ReturnFormViewModel form)
    {
        if (!await HasAsync(PermissionCodes.F3_4_3)) return Forbid();
        ViewData["Nav"] = Nav;
        if (ModelState.IsValid)
        {
            var result = await _returns.CreateAsync(id, form, UserId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        if (!await _returns.PrepareFormAsync(form, id, UserId())) return NotFound();
        form.CanProcess = true;
        return View(form);
    }

    // ---------------- 3.4.1 Dược sĩ gửi yêu cầu → quản lý xử lý ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F3_4_1)]
    public async Task<IActionResult> SendRequest(int id, ReturnFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.RequestId = null;
        if (ModelState.IsValid)
        {
            var result = await _returns.SendRequestAsync(id, form, UserId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        if (!await _returns.PrepareFormAsync(form, id, UserId())) return NotFound();
        form.CanProcess = await CanProcessAsync();
        form.CanRequest = !form.CanProcess;
        return View(nameof(Create), form);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F3_4_2)]
    public async Task<IActionResult> RejectRequest(long requestId, string? reason)
    {
        var result = await _returns.RejectRequestAsync(requestId, reason, UserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    // ---------------- 3.4.4 Nhập lại hoặc không nhập ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F3_4_4)]
    public async Task<IActionResult> Restock(int id, RestockForm form)
    {
        var result = await _returns.RestockAsync(id, form, UserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------- Dùng chung ----------------

    private async Task<bool> CanLookupAsync() => await HasAsync(PermissionCodes.F3_4_1) || await CanProcessAsync();

    private async Task<bool> CanProcessAsync() => await HasAsync(PermissionCodes.F3_4_2) && await HasAsync(PermissionCodes.F3_4_3);

    private async Task<bool> CanViewAsync() => await CanLookupAsync() || await HasAsync(PermissionCodes.F3_4_4);

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string UserId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
