using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Trả nhà cung cấp (DEC-44): xem cần F2.3.1 hoặc F2.3.5; lập / sửa / xuất trả / hủy phiếu F2.3.1; thu tiền hoàn (phiếu thu) F2.3.5.
/// Pharmacist không có mã F2.* (SYS-13).
/// </summary>
[Area("Inventory")]
public class SupplierReturnsController : Controller
{
    private const string Nav = "sret";

    private readonly ISupplierReturnService _returns;
    private readonly ISupplierPaymentService _payments;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public SupplierReturnsController(ISupplierReturnService returns, ISupplierPaymentService payments,
        IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _returns = returns;
        _payments = payments;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] SupplierReturnFilter filter)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _returns.GetListAsync(filter);
        model.CanCreate = await HasAsync(PermissionCodes.F2_3_1);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _returns.GetDetailsAsync(id);
        if (model is null) return NotFound();
        model.CanEdit = model.IsDraft && await HasAsync(PermissionCodes.F2_3_1);
        model.CanRefund = model.Status == (byte)SupplierReturnStatus.Issued && model.Remaining > 0 && await HasAsync(PermissionCodes.F2_3_5);
        model.CanOpenPayments = await HasAsync(PermissionCodes.F2_2_5) || await HasAsync(PermissionCodes.F2_3_5);
        return View(model);
    }

    /// <summary>Phiếu trả hàng nhà cung cấp (A4).</summary>
    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        var model = await _returns.GetDetailsAsync(id);
        return model is null ? NotFound() : View(model);
    }

    // ---------------- 2.3.1 Lập / sửa phiếu (Nháp) ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_3_1)]
    public async Task<IActionResult> Create(int? supplierId, int? receiptId, int? batchId)
    {
        ViewData["Nav"] = Nav;
        return View(await _returns.NewFormAsync(supplierId, receiptId, batchId));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_3_1)]
    public async Task<IActionResult> Create(SupplierReturnFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.SupplierReturnId = null;
        if (ModelState.IsValid)
        {
            var result = await _returns.CreateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _returns.PrepareFormAsync(form);
        return View(form);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_3_1)]
    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Nav"] = Nav;
        var form = await _returns.EditFormAsync(id);
        if (form is null) return NotFound();
        if (!form.IsDraft)
        {
            TempData["Error"] = "Phiếu đã xuất trả hoặc đã hủy nên không sửa được.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(form);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_3_1)]
    public async Task<IActionResult> Edit(int id, SupplierReturnFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.SupplierReturnId = id;
        if (ModelState.IsValid)
        {
            var result = await _returns.UpdateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _returns.PrepareFormAsync(form);
        return View(form);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_3_1)]
    public async Task<IActionResult> Issue(int id)
    {
        var result = await _returns.IssueAsync(id, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_3_1)]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _returns.CancelAsync(id, reason, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------- 2.3.5 Thu tiền hoàn ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_3_5)]
    public async Task<IActionResult> Refund(int id, SupplierMoneyForm refund)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Số tiền hoàn không đúng: nhập số tiền theo đồng, lớn hơn 0.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var result = await _payments.RefundAsync(id, refund, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanViewAsync() => await HasAsync(PermissionCodes.F2_3_1) || await HasAsync(PermissionCodes.F2_3_5);

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
