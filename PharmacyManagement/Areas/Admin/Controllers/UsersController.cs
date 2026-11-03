using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PharmacyManagement.Authorization;
using PharmacyManagement.Configuration;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Admin;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Admin.Controllers;

/// <summary>Tài khoản nhân viên — chỉ Owner (1.3.1, 1.3.4, 1.3.5 khóa cứng cho Owner, phải xác minh email).</summary>
[Area("Admin")]
[Authorize(Policy = PermissionCodes.F1_3_1)]
public class UsersController : Controller
{
    private readonly IUserAdminService _admin;
    private readonly UserManager<IdentityUser> _users;
    private readonly EmailOptions _emailOptions;

    public UsersController(IUserAdminService admin, UserManager<IdentityUser> users, IOptions<EmailOptions> emailOptions)
    {
        _admin = admin;
        _users = users;
        _emailOptions = emailOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? invite = null)
    {
        ViewData["Nav"] = NavigationMenu.Accounts;
        InviteUserViewModel? prefill = null;
        var model = await _admin.GetListAsync();
        if (invite is not null)
        {
            var option = model.InvitableEmployees.FirstOrDefault(e => e.EmployeeId == invite);
            if (option is not null)
            {
                prefill = new InviteUserViewModel { EmployeeId = option.EmployeeId, Email = option.Email ?? string.Empty };
                model = await _admin.GetListAsync(prefill, openInvite: true);
            }
        }
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_3_4)]
    public async Task<IActionResult> Invite(InviteUserViewModel model)
    {
        ViewData["Nav"] = NavigationMenu.Accounts;
        if (model.NewEmployee && string.IsNullOrWhiteSpace(model.FullName))
            ModelState.AddModelError(nameof(model.FullName), "Nhập họ tên nhân viên.");
        if (!model.NewEmployee && model.EmployeeId is null)
            ModelState.AddModelError(nameof(model.EmployeeId), "Chọn nhân viên cần mời.");
        if (!ModelState.IsValid) return View(nameof(Index), await _admin.GetListAsync(model, openInvite: true));

        var result = await _admin.InviteAsync(model, ActorId(), BaseUrl());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            return View(nameof(Index), await _admin.GetListAsync(model, openInvite: true));
        }
        SetResultMessage(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Resend(int employeeId)
    {
        SetResultMessage(await _admin.ResendInviteAsync(employeeId, ActorId(), BaseUrl()));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> CancelInvite(int employeeId)
    {
        SetResultMessage(await _admin.CancelInviteAsync(employeeId, ActorId()));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ChangeEmail(ChangeEmailViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        SetResultMessage(await _admin.ChangeEmailAsync(model, ActorId(), BaseUrl()));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_3_5)]
    public async Task<IActionResult> Lock(LockUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        SetResultMessage(await _admin.LockAsync(model, ActorId()));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_3_5)]
    public async Task<IActionResult> Unlock(int employeeId)
    {
        SetResultMessage(await _admin.UnlockAsync(employeeId, ActorId()));
        return RedirectToAction(nameof(Index));
    }

    private void SetResultMessage(OperationResult result)
    {
        if (!result.Succeeded) TempData["Error"] = result.Message;
        else if (result.Warning is not null) TempData["Warning"] = result.Warning;
        else if (result.Message is not null) TempData["Success"] = result.Message;
    }

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");

    private string BaseUrl()
        => string.IsNullOrWhiteSpace(_emailOptions.PublicBaseUrl) ? $"{Request.Scheme}://{Request.Host}" : _emailOptions.PublicBaseUrl!;
}
