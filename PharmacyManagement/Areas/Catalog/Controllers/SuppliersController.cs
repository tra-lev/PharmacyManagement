using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Catalog.Controllers;

/// <summary>
/// Danh mục nhà cung cấp — BFD 1.2.1 Cập nhật nhà cung cấp (Manager, Owner theo Bảng 2.3).
/// Mọi thao tác (xem, thêm, sửa, ngừng/mở lại giao dịch) cần mã F1.2.1 — kiểm tra ở server.
/// </summary>
[Area("Catalog")]
[Authorize(Policy = PermissionCodes.F1_2_1)]
public class SuppliersController : Controller
{
    private const string Nav = "suppliers";

    private readonly ISupplierService _suppliers;
    private readonly UserManager<IdentityUser> _users;

    public SuppliersController(ISupplierService suppliers, UserManager<IdentityUser> users)
    {
        _suppliers = suppliers;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] SupplierFilter filter)
    {
        ViewData["Nav"] = Nav;
        return View(await _suppliers.GetListAsync(filter));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        ViewData["Nav"] = Nav;
        var model = await _suppliers.GetDetailsAsync(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewData["Nav"] = Nav;
        return View(await _suppliers.NewFormAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(SupplierFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.SupplierId = null;
        if (ModelState.IsValid)
        {
            var result = await _suppliers.CreateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        form.SupplierCode = (await _suppliers.NewFormAsync()).SupplierCode;
        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Nav"] = Nav;
        var form = await _suppliers.EditFormAsync(id);
        return form is null ? NotFound() : View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, SupplierFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        var current = await _suppliers.EditFormAsync(id);
        if (current is null) return NotFound();
        form.SupplierId = id;
        form.SupplierCode = current.SupplierCode;
        if (ModelState.IsValid)
        {
            var result = await _suppliers.UpdateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        return View(form);
    }

    // ---------------- Ngừng / mở lại giao dịch (không xóa — SYS-06) ----------------

    [HttpPost]
    public async Task<IActionResult> Deactivate(int id) => await SetActiveAsync(id, false);

    [HttpPost]
    public async Task<IActionResult> Activate(int id) => await SetActiveAsync(id, true);

    private async Task<IActionResult> SetActiveAsync(int id, bool active)
    {
        var result = await _suppliers.SetActiveAsync(id, active, ActorId());
        if (!result.Succeeded) TempData["Error"] = result.Message;
        else if (result.Warning is not null) TempData["Warning"] = result.Warning;
        else TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
