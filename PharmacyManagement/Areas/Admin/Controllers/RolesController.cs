using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Admin.Controllers;

/// <summary>
/// Vai trò và phân quyền (DEC-47): xem cần F1.3.2 hoặc F1.3.3; lưu quyền cần F1.3.3. Cả hai chỉ Owner có (DEC-18) và cần email đã xác minh.
/// </summary>
[Area("Admin")]
public class RolesController : Controller
{
    private const string Nav = "roles";

    private readonly IRoleAdminService _roles;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public RolesController(IRoleAdminService roles, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _roles = roles;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _roles.GetRolesAsync();
        model.CanEdit = await HasAsync(PermissionCodes.F1_3_3);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _roles.GetPermissionsAsync(id);
        if (model is null) return NotFound();
        model.CanEdit = model.Editable && await HasAsync(PermissionCodes.F1_3_3);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_3_3)]
    public async Task<IActionResult> Edit(string id, List<int>? granted)
    {
        var actor = _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
        var result = await _roles.SaveAsync(id, granted ?? new List<int>(), actor);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<bool> CanViewAsync() => await HasAsync(PermissionCodes.F1_3_2) || await HasAsync(PermissionCodes.F1_3_3);

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;
}
