using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Thiết lập định mức tồn (1.5.2, F1.5.2) và ngưỡng cận hạn (1.5.3, F1.5.3) — DEC-42. Vào trang cần một trong hai mã;
/// trường nào thiếu quyền thì chỉ xem, server giữ nguyên giá trị cũ của trường đó khi lưu.
/// </summary>
[Area("Inventory")]
public class PoliciesController : Controller
{
    private const string Nav = "settings";

    private readonly IInventoryPolicyService _policies;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public PoliciesController(IInventoryPolicyService policies, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _policies = policies;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] PolicyFilter filter)
    {
        var canMinimum = await HasAsync(PermissionCodes.F1_5_2);
        var canWarning = await HasAsync(PermissionCodes.F1_5_3);
        if (!canMinimum && !canWarning) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _policies.GetListAsync(filter);
        model.CanEditMinimum = canMinimum;
        model.CanEditWarning = canWarning;
        return View(model);
    }

    /// <summary>Lưu các thuốc đang hiện trên trang.</summary>
    [HttpPost]
    public async Task<IActionResult> Save(PolicySaveForm form)
    {
        var canMinimum = await HasAsync(PermissionCodes.F1_5_2);
        var canWarning = await HasAsync(PermissionCodes.F1_5_3);
        if (!canMinimum && !canWarning) return Forbid();

        var result = ModelState.IsValid
            ? await _policies.SaveAsync(form.Rows, canMinimum, canWarning, ActorId())
            : null;
        if (result is { Succeeded: true })
        {
            TempData["Success"] = result.Message;
            return RedirectToAction(nameof(Index), new { form.Q, form.CategoryId, form.Status, form.Show, page = form.Page });
        }

        // Lỗi: hiện lại trang với số vừa nhập
        ModelState.Clear();
        ModelState.AddModelError(string.Empty, result?.Message
            ?? "Có ô nhập không đúng: mức tối thiểu là số không âm, số ngày cận hạn là số nguyên. Kiểm tra lại rồi lưu.");
        ViewData["Nav"] = Nav;
        var filter = new PolicyFilter { Q = form.Q, CategoryId = form.CategoryId, Status = form.Status, Show = form.Show, Page = form.Page };
        var model = await _policies.GetListAsync(filter);
        model.CanEditMinimum = canMinimum;
        model.CanEditWarning = canWarning;
        var posted = form.Rows.GroupBy(r => r.MedicineId).ToDictionary(g => g.Key, g => g.Last());
        model.Rows = model.Rows.Select(r => posted.TryGetValue(r.MedicineId, out var input)
                ? r with
                {
                    Minimum = canMinimum ? input.Minimum ?? 0m : r.Minimum,
                    WarningDays = canWarning ? input.WarningDays : r.WarningDays
                }
                : r)
            .ToList();
        return View(nameof(Index), model);
    }

    /// <summary>Ngưỡng cận hạn chung (PharmacySettings.ExpiryWarningDays).</summary>
    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_5_3)]
    public async Task<IActionResult> SaveDefault(int? days, string? q, int? categoryId, string? status, string? show, int page = 1)
    {
        var result = await _policies.SaveDefaultWarningDaysAsync(days, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index), new { q, categoryId, status, show, page });
    }

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
