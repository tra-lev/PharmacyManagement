using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Catalog.Controllers;

/// <summary>
/// Khai báo tủ, kệ lưu trữ — BFD 1.5.1 (DEC-42). Mọi thao tác cần F1.5.1 (Manager, Owner) — kiểm tra ở server.
/// </summary>
[Area("Catalog")]
[Authorize(Policy = PermissionCodes.F1_5_1)]
public class LocationsController : Controller
{
    private const string Nav = "locations";

    private readonly ILocationService _locations;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public LocationsController(ILocationService locations, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _locations = locations;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Nav"] = Nav;
        var model = await _locations.GetTreeAsync();
        model.CanViewStock = await CanViewStockAsync();
        return View(model);
    }

    /// <param name="type">1 Tủ, 2 Kệ.</param>
    /// <param name="parentId">Tủ chọn sẵn khi thêm kệ.</param>
    [HttpGet]
    public async Task<IActionResult> Create(byte type = 2, int? parentId = null)
    {
        ViewData["Nav"] = Nav;
        return View(await _locations.NewFormAsync(type, parentId));
    }

    [HttpPost]
    public async Task<IActionResult> Create(LocationFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.LocationId = null;
        if (ModelState.IsValid)
        {
            var result = await _locations.CreateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _locations.PrepareFormAsync(form);
        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Nav"] = Nav;
        var form = await _locations.EditFormAsync(id);
        return form is null ? NotFound() : View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, LocationFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        var current = await _locations.EditFormAsync(id);
        if (current is null) return NotFound();
        form.LocationId = id;
        form.LocationType = current.LocationType;
        if (ModelState.IsValid)
        {
            var result = await _locations.UpdateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _locations.PrepareFormAsync(form);
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _locations.DeleteAsync(id, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Cùng điều kiện với mục menu "Tồn kho &amp; lô".</summary>
    private async Task<bool> CanViewStockAsync()
    {
        foreach (var code in NavigationMenu.Find("stock")?.AnyOf ?? Array.Empty<string>())
        {
            if ((await _authorization.AuthorizeAsync(User, code)).Succeeded) return true;
        }
        return false;
    }

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
