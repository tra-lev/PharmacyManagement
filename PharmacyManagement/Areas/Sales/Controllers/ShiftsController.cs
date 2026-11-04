using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Sales.Controllers;

/// <summary>
/// Ca làm việc của người đang đăng nhập — BFD 1.4.1 Mở ca, 1.4.2 Bàn giao ca, 1.4.3 Chốt tiền cuối ca
/// (Pharmacist, Manager, Owner — seed RolePermissions). Mỗi thao tác kiểm tra đúng mã quyền ở server.
/// </summary>
[Area("Sales")]
public class ShiftsController : Controller
{
    private const string Nav = "shifts";

    private readonly IShiftService _shifts;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public ShiftsController(IShiftService shifts, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _shifts = shifts;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await HasAnyAsync()) return Forbid();
        return await PageAsync(null);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var canViewAll = await HasAsync(PermissionCodes.F4_1_1);
        if (!canViewAll && !await HasAnyAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _shifts.GetDetailsAsync(id, UserId(), canViewAll);
        return model is null ? NotFound() : View(model);
    }

    // ---------------- Ca toàn nhà thuốc — chỉ xem (F4.1.1 Lập báo cáo doanh thu: Owner, Manager) ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F4_1_1)]
    public async Task<IActionResult> All([FromQuery] ShiftFilter filter)
    {
        ViewData["Nav"] = Nav;
        return View(await _shifts.GetAllAsync(filter));
    }

    // ---------------- 1.4.1 Mở ca ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_4_1)]
    public async Task<IActionResult> Open([Bind(Prefix = "Open")] OpenShiftForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await _shifts.OpenAsync(form, UserId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }
            AddError("Open", result.Field, result.Message!);
        }
        return await PageAsync(m => m.Open = form);
    }

    // ---------------- 1.4.2 Bàn giao ca ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_4_2)]
    public async Task<IActionResult> Handover([Bind(Prefix = "Handover")] HandoverForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await _shifts.HandOverAsync(form, UserId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }
            AddError("Handover", result.Field, result.Message!);
        }
        return await PageAsync(m => m.Handover = form);
    }

    // ---------------- 1.4.3 Chốt tiền cuối ca ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_4_3)]
    public async Task<IActionResult> Close([Bind(Prefix = "Close")] CloseShiftForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await _shifts.CloseAsync(form, UserId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }
            AddError("Close", result.Field, result.Message!);
        }
        return await PageAsync(m => m.Close = form);
    }

    // ---------------- Dùng chung ----------------

    /// <summary>Dựng lại trang "Ca của tôi"; <paramref name="keepInput"/> giữ dữ liệu người dùng vừa nhập khi có lỗi.</summary>
    private async Task<IActionResult> PageAsync(Action<MyShiftViewModel>? keepInput)
    {
        ViewData["Nav"] = Nav;
        var model = await _shifts.GetMyShiftAsync(UserId());
        model.CanViewAll = await HasAsync(PermissionCodes.F4_1_1);
        model.CanOpen = await HasAsync(PermissionCodes.F1_4_1);
        model.CanHandover = await HasAsync(PermissionCodes.F1_4_2);
        model.CanClose = await HasAsync(PermissionCodes.F1_4_3);
        keepInput?.Invoke(model);
        return View(nameof(Index), model);
    }

    private void AddError(string prefix, string? field, string message)
        => ModelState.AddModelError(field is null ? string.Empty : prefix + "." + field, message);

    private async Task<bool> HasAnyAsync()
        => await HasAsync(PermissionCodes.F1_4_1) || await HasAsync(PermissionCodes.F1_4_2) || await HasAsync(PermissionCodes.F1_4_3);

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string UserId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
