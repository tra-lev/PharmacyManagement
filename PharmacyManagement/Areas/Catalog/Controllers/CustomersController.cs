using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Catalog.Controllers;

/// <summary>
/// Hồ sơ khách hàng — BFD 1.2.2 Cập nhật khách hàng (Manager, Pharmacist, Owner theo Bảng 2.3).
/// Mọi thao tác (xem, thêm, sửa, ngừng/mở lại) cần mã F1.2.2 — kiểm tra ở server.
/// </summary>
[Area("Catalog")]
[Authorize(Policy = PermissionCodes.F1_2_2)]
public class CustomersController : Controller
{
    private const string Nav = "customers";

    private readonly ICustomerService _customers;
    private readonly UserManager<IdentityUser> _users;

    public CustomersController(ICustomerService customers, UserManager<IdentityUser> users)
    {
        _customers = customers;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] CustomerFilter filter)
    {
        ViewData["Nav"] = Nav;
        return View(await _customers.GetListAsync(filter));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        ViewData["Nav"] = Nav;
        var model = await _customers.GetDetailsAsync(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewData["Nav"] = Nav;
        return View(await _customers.NewFormAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CustomerFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.CustomerId = null;
        if (ModelState.IsValid)
        {
            var result = await _customers.CreateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _customers.PrepareFormAsync(form);
        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Nav"] = Nav;
        var form = await _customers.EditFormAsync(id);
        return form is null ? NotFound() : View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, CustomerFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.CustomerId = id;
        if (ModelState.IsValid)
        {
            var result = await _customers.UpdateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _customers.PrepareFormAsync(form);
        if (form.CustomerCode is null) return NotFound();
        return View(form);
    }

    // ---------------- Ngừng / mở lại (không xóa — SYS-06) ----------------

    [HttpPost]
    public async Task<IActionResult> Deactivate(int id) => await SetActiveAsync(id, false);

    [HttpPost]
    public async Task<IActionResult> Activate(int id) => await SetActiveAsync(id, true);

    private async Task<IActionResult> SetActiveAsync(int id, bool active)
    {
        var result = await _customers.SetActiveAsync(id, active, ActorId());
        if (!result.Succeeded) TempData["Error"] = result.Message;
        else if (result.Warning is not null) TempData["Warning"] = result.Warning;
        else TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
