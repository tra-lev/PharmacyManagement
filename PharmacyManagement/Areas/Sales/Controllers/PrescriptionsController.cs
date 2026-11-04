using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Sales.Controllers;

/// <summary>
/// Bán theo đơn — BFD 3.2.1 tiếp nhận đơn, 3.2.2.1 kiểm tra hợp lệ, 3.2.2.2 kiểm tra liều dùng, 3.2.2.3 kiểm tra an toàn (Hệ thống),
/// 3.2.3 lưu đơn; 3.2.4 lập hóa đơn theo đơn ở <see cref="InvoicesController"/> (Create?prescriptionId=).
/// Mỗi thao tác kiểm tra mã quyền ở server.
/// </summary>
[Area("Sales")]
public class PrescriptionsController : Controller
{
    private const string Nav = "rx";

    private readonly IPrescriptionService _prescriptions;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public PrescriptionsController(IPrescriptionService prescriptions, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _prescriptions = prescriptions;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] PrescriptionFilter filter)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _prescriptions.GetListAsync(filter);
        model.CanCreate = await HasAsync(PermissionCodes.F3_2_1);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _prescriptions.GetDetailsAsync(id);
        if (model is null) return NotFound();
        model.CanSell = model.IsSaved && !model.Expired && model.PendingInvoiceId is null && await HasAsync(PermissionCodes.F3_2_4);
        model.CanCancel = model.IsSaved && model.PendingInvoiceId is null && await HasAsync(PermissionCodes.F3_2_3);
        return View(model);
    }

    // ---------------- 3.2.1 Tiếp nhận đơn → 3.2.2 kiểm tra → 3.2.3 lưu ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F3_2_1)]
    public async Task<IActionResult> Create()
    {
        ViewData["Nav"] = Nav;
        var model = await _prescriptions.NewFormAsync();
        model.CanCreateCustomer = await HasAsync(PermissionCodes.F1_2_2);
        return View(model);
    }

    /// <param name="intent">"check": chỉ kiểm tra đơn (3.2.2), không lưu; "save": lưu đơn (3.2.3).</param>
    [HttpPost]
    [Authorize(Policy = PermissionCodes.F3_2_1)]
    public async Task<IActionResult> Create(PrescriptionFormViewModel form, string? intent)
    {
        ViewData["Nav"] = Nav;
        var save = intent == "save";
        if (!await HasAsync(save ? PermissionCodes.F3_2_3 : PermissionCodes.F3_2_2_1)) return Forbid();

        if (ModelState.IsValid)
        {
            if (save)
            {
                var result = await _prescriptions.CreateAsync(form, UserId());
                if (result.Succeeded)
                {
                    TempData["Success"] = result.Message;
                    return RedirectToAction(nameof(Details), new { id = result.Value });
                }
                ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            }
            else
            {
                var result = await _prescriptions.CheckAsync(form);
                if (result.Succeeded) form.Checked = true;
                else ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
            }
        }
        await _prescriptions.PrepareFormAsync(form);
        form.CanCreateCustomer = await HasAsync(PermissionCodes.F1_2_2);
        return View(form);
    }

    // ---------------- Hủy đơn chưa bán ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F3_2_3)]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _prescriptions.CancelAsync(id, reason, UserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------- Dùng chung ----------------

    private async Task<bool> CanViewAsync()
        => await HasAsync(PermissionCodes.F3_2_1) || await HasAsync(PermissionCodes.F3_2_4);

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string UserId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
