using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Công nợ nhà cung cấp (DEC-44): xem cần F2.2.5 hoặc F2.3.5; lập / hủy phiếu chi F2.2.5; hủy phiếu thu tiền hoàn F2.3.5.
/// Pharmacist không có mã F2.* (SYS-13).
/// </summary>
[Area("Inventory")]
public class SupplierPaymentsController : Controller
{
    private const string Nav = "spay";

    private readonly ISupplierPaymentService _payments;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public SupplierPaymentsController(ISupplierPaymentService payments, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _payments = payments;
        _authorization = authorization;
        _users = users;
    }

    /// <summary>Công nợ theo nhà cung cấp.</summary>
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] SupplierDebtFilter filter)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        return View(await _payments.GetDebtsAsync(filter));
    }

    /// <summary>Công nợ một nhà cung cấp: phiếu nhập còn nợ, phiếu trả chờ tiền hoàn, phiếu chi / thu gần nhất.</summary>
    [HttpGet]
    public async Task<IActionResult> Supplier(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _payments.GetSupplierAsync(id);
        if (model is null) return NotFound();
        model.CanPay = await HasAsync(PermissionCodes.F2_2_5);
        model.CanRefund = await HasAsync(PermissionCodes.F2_3_5);
        model.CanOpenReceipts = await CanOpenReceiptsAsync();
        model.CanOpenReturns = await CanOpenReturnsAsync();
        return View(model);
    }

    /// <summary>Danh sách phiếu chi / thu.</summary>
    [HttpGet]
    public async Task<IActionResult> Payments([FromQuery] SupplierPaymentFilter filter)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        return View(await _payments.GetPaymentsAsync(filter));
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _payments.GetPaymentAsync(id);
        if (model is null) return NotFound();
        model.CanVoid = model.Status == (byte)Models.Enums.PaymentStatus.Succeeded
                        && await HasAsync(model.IsRefund ? PermissionCodes.F2_3_5 : PermissionCodes.F2_2_5);
        model.CanOpenReceipts = await CanOpenReceiptsAsync();
        model.CanOpenReturns = await CanOpenReturnsAsync();
        return View(model);
    }

    // ---------------- 2.2.5 Lập phiếu chi cho phiếu nhập ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_2_5)]
    public async Task<IActionResult> Pay(int receiptId)
    {
        ViewData["Nav"] = Nav;
        var model = await _payments.PayFormAsync(receiptId);
        if (model is null) return NotFound();
        if (model.Remaining <= 0)
        {
            TempData["Error"] = $"Phiếu nhập {model.ReceiptCode} đã thanh toán đủ.";
            return RedirectToAction(nameof(Supplier), new { id = model.SupplierId });
        }
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_2_5)]
    public async Task<IActionResult> Pay(int receiptId, [Bind(Prefix = "Form")] SupplierMoneyForm form)
    {
        ViewData["Nav"] = Nav;
        if (ModelState.IsValid)
        {
            var result = await _payments.PayAsync(receiptId, form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field is null ? string.Empty : "Form." + result.Field, result.Message!);
        }
        var model = await _payments.PayFormAsync(receiptId);
        if (model is null) return NotFound();
        model.Form = form;
        return View(model);
    }

    // ---------------- Hủy phiếu chi / thu ----------------

    [HttpPost]
    public async Task<IActionResult> Void(long id, string? reason)
    {
        var payment = await _payments.GetPaymentAsync(id);
        if (payment is null) return NotFound();
        if (!await HasAsync(payment.IsRefund ? PermissionCodes.F2_3_5 : PermissionCodes.F2_2_5)) return Forbid();
        var result = await _payments.VoidAsync(id, reason, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanViewAsync() => await HasAsync(PermissionCodes.F2_2_5) || await HasAsync(PermissionCodes.F2_3_5);

    /// <summary>Cùng điều kiện xem của ReceiptsController / SupplierReturnsController.</summary>
    private async Task<bool> CanOpenReceiptsAsync() => await HasAsync(PermissionCodes.F2_2_1_1) || await HasAsync(PermissionCodes.F2_2_2);

    private async Task<bool> CanOpenReturnsAsync() => await HasAsync(PermissionCodes.F2_3_1) || await HasAsync(PermissionCodes.F2_3_5);

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
