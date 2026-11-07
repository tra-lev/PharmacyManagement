using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Nhập kho — BFD 2.2.1.1 đối chiếu đơn đặt hàng (xem), 2.2.1.2–2.2.1.3 kiểm nhận, 2.2.2 lập phiếu nhập, 2.2.3 số lô/hạn dùng,
/// 2.2.4 xếp vị trí (Manager, Owner). Pharmacist không bao giờ có các mã F2.* (SYS-13, khóa ở PermissionHandler).
/// </summary>
[Area("Inventory")]
public class ReceiptsController : Controller
{
    private const string Nav = "gr";

    private readonly IGoodsReceiptService _receipts;
    private readonly ISupplierPaymentService _payments;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public ReceiptsController(IGoodsReceiptService receipts, ISupplierPaymentService payments, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _receipts = receipts;
        _payments = payments;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReceiptFilter filter)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _receipts.GetListAsync(filter);
        model.CanCreate = await HasAsync(PermissionCodes.F2_2_2);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _receipts.GetDetailsAsync(id);
        if (model is null) return NotFound();
        var inspecting = model.Status == (byte)GoodsReceiptStatus.Inspecting;
        model.CanEdit = inspecting && await HasAsync(PermissionCodes.F2_2_2);
        model.CanPost = inspecting && await CanPostAsync();
        if (model.Status == (byte)GoodsReceiptStatus.Posted)
        {
            var (paid, remaining) = await _payments.ReceiptBalanceAsync(id);
            model.PaidAmount = paid;
            model.CanPay = remaining > 0 && await HasAsync(PermissionCodes.F2_2_5);
            model.CanReturnToSupplier = await HasAsync(PermissionCodes.F2_3_1);
            model.CanOpenPayments = await HasAsync(PermissionCodes.F2_2_5) || await HasAsync(PermissionCodes.F2_3_5);
        }
        return View(model);
    }

    // ---------------- Lập phiếu (kiểm nhận 2.2.1, lập phiếu 2.2.2) ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_2_2)]
    public async Task<IActionResult> Create(int? supplierId, int? purchaseOrderId)
    {
        ViewData["Nav"] = Nav;
        return View(await _receipts.NewFormAsync(supplierId, purchaseOrderId));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_2_2)]
    public async Task<IActionResult> Create(ReceiptFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.GoodsReceiptId = null;
        if (ModelState.IsValid)
        {
            var result = await _receipts.CreateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _receipts.PrepareFormAsync(form);
        return View(form);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_2_2)]
    public async Task<IActionResult> Edit(int id)
    {
        var status = await _receipts.GetStatusAsync(id);
        if (status is null) return NotFound();
        if (status != (byte)GoodsReceiptStatus.Inspecting)
        {
            TempData["Error"] = "Chỉ sửa được phiếu đang kiểm nhận.";
            return RedirectToAction(nameof(Details), new { id });
        }
        ViewData["Nav"] = Nav;
        var form = await _receipts.EditFormAsync(id);
        return form is null ? NotFound() : View(form);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_2_2)]
    public async Task<IActionResult> Edit(int id, ReceiptFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.GoodsReceiptId = id;
        if (ModelState.IsValid)
        {
            var result = await _receipts.UpdateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _receipts.PrepareFormAsync(form);
        if (form.Code is null) return NotFound();
        return View(form);
    }

    // ---------------- Nhập kho (2.2.2 + 2.2.3 + 2.2.4) ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_2_2)]
    public async Task<IActionResult> Post(int id)
    {
        if (!await CanPostAsync()) return Forbid();
        var result = await _receipts.PostAsync(id, ActorId());
        if (!result.Succeeded) TempData["Error"] = result.Message;
        else if (result.Warning is not null) TempData["Warning"] = result.Warning;
        else TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_2_2)]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _receipts.CancelAsync(id, reason, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanViewAsync()
        => await HasAsync(PermissionCodes.F2_2_1_1) || await HasAsync(PermissionCodes.F2_2_2);

    /// <summary>Nhập kho = lập phiếu (2.2.2) + ghi số lô, hạn dùng (2.2.3) + xếp vị trí (2.2.4).</summary>
    private async Task<bool> CanPostAsync()
        => await HasAsync(PermissionCodes.F2_2_2) && await HasAsync(PermissionCodes.F2_2_3) && await HasAsync(PermissionCodes.F2_2_4);

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
