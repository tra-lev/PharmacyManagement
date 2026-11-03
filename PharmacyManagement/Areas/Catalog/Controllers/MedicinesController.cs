using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Catalog.Controllers;

/// <summary>
/// Danh mục thuốc — 1.1.1 thêm, 1.1.2 sửa (xem danh sách, chi tiết, ngừng/mở bán), 1.1.3 phân loại, 1.1.4 quy đổi.
/// Quyền kiểm tra ở server theo mã BFD (RolePermissions); Pharmacist không có các mã này (tra cứu thuốc ở 3.1.1).
/// </summary>
[Area("Catalog")]
public class MedicinesController : Controller
{
    private const string Nav = "meds";

    private readonly IMedicineCatalogService _catalog;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public MedicinesController(IMedicineCatalogService catalog, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _catalog = catalog;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] MedicineFilter filter)
    {
        if (!await HasAsync(PermissionCodes.F1_1_1) && !await HasAsync(PermissionCodes.F1_1_2)) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _catalog.GetListAsync(filter);
        model.CanCreate = await HasAsync(PermissionCodes.F1_1_1);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await HasAsync(PermissionCodes.F1_1_1) && !await HasAsync(PermissionCodes.F1_1_2)) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _catalog.GetDetailsAsync(id);
        if (model is null) return NotFound();
        model.CanEdit = await HasAsync(PermissionCodes.F1_1_2);
        return View(model);
    }

    // ---------------- Thêm thuốc (1.1.1) ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F1_1_1)]
    public async Task<IActionResult> Create()
    {
        ViewData["Nav"] = Nav;
        return View(await _catalog.NewFormAsync(await RightsAsync()));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_1_1)]
    public async Task<IActionResult> Create(MedicineFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.MedicineId = null;
        var rights = await RightsAsync();
        if (ModelState.IsValid)
        {
            var result = await _catalog.CreateAsync(form, rights, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _catalog.PrepareFormAsync(form, rights);
        return View(form);
    }

    // ---------------- Sửa thông tin thuốc (1.1.2) ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F1_1_2)]
    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Nav"] = Nav;
        var form = await _catalog.EditFormAsync(id, await RightsAsync());
        return form is null ? NotFound() : View(form);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_1_2)]
    public async Task<IActionResult> Edit(int id, MedicineFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.MedicineId = id;
        var rights = await RightsAsync();
        if (ModelState.IsValid)
        {
            var result = await _catalog.UpdateAsync(form, rights, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _catalog.PrepareFormAsync(form, rights);
        return View(form);
    }

    // ---------------- Ngừng / mở lại kinh doanh (không xóa — SYS-06) ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_1_2)]
    public async Task<IActionResult> Deactivate(int id) => await SetActiveAsync(id, false);

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_1_2)]
    public async Task<IActionResult> Activate(int id) => await SetActiveAsync(id, true);

    private async Task<IActionResult> SetActiveAsync(int id, bool active)
    {
        var result = await _catalog.SetActiveAsync(id, active, ActorId());
        if (!result.Succeeded) TempData["Error"] = result.Message;
        else if (result.Warning is not null) TempData["Warning"] = result.Warning;
        else TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<MedicineEditRights> RightsAsync()
        => new(await HasAsync(PermissionCodes.F1_1_3), await HasAsync(PermissionCodes.F1_1_4));

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
