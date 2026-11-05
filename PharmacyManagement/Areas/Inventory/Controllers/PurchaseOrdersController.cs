using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Đặt hàng nhà cung cấp — BFD 2.1.1 lập đơn (F2.1.1), 2.1.2 gửi đơn (F2.1.2), 2.1.3 theo dõi (F2.1.3).
/// Manager, Owner (Pharmacist bị khóa mọi mã F2.* — SYS-13). Mỗi thao tác kiểm tra mã quyền ở server.
/// </summary>
[Area("Inventory")]
public class PurchaseOrdersController : Controller
{
    private const string Nav = "po";

    private readonly IPurchaseOrderService _orders;
    private readonly IEmailService _email;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public PurchaseOrdersController(IPurchaseOrderService orders, IEmailService email, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _orders = orders;
        _email = email;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] PurchaseOrderFilter filter)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _orders.GetListAsync(filter);
        model.CanCreate = await HasAsync(PermissionCodes.F2_1_1);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _orders.GetDetailsAsync(id);
        if (model is null) return NotFound();
        await FillPermissionsAsync(model);
        return View(model);
    }

    /// <summary>Trang in đơn đặt hàng (A4) — In hoặc Lưu PDF bằng trình duyệt.</summary>
    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        var model = await _orders.GetDetailsAsync(id);
        if (model is null) return NotFound();
        return View(model);
    }

    // ---------------- 2.1.1 Lập / sửa đơn (Nháp) ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_1_1)]
    public async Task<IActionResult> Create(int? supplierId, bool suggest = false)
    {
        ViewData["Nav"] = Nav;
        return View(await _orders.NewFormAsync(supplierId, suggest));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_1_1)]
    public async Task<IActionResult> Create(PurchaseOrderFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.PurchaseOrderId = null;
        form.Code = null;
        if (ModelState.IsValid)
        {
            var result = await _orders.CreateAsync(form, UserId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _orders.PrepareFormAsync(form);
        return View(form);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_1_1)]
    public async Task<IActionResult> Edit(int id)
    {
        var form = await _orders.EditFormAsync(id);
        if (form is null)
        {
            TempData["Error"] = "Chỉ sửa được đơn nháp (chưa gửi).";
            return RedirectToAction(nameof(Details), new { id });
        }
        ViewData["Nav"] = Nav;
        return View(form);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_1_1)]
    public async Task<IActionResult> Edit(int id, PurchaseOrderFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.PurchaseOrderId = id;
        form.Code = null;
        if (ModelState.IsValid)
        {
            var result = await _orders.UpdateAsync(id, form, UserId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _orders.PrepareFormAsync(form);
        return View(form);
    }

    // ---------------- 2.1.2 Gửi đơn, gửi email cho nhà cung cấp ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_1_2)]
    public async Task<IActionResult> Send(int id)
    {
        var result = await _orders.SendAsync(id, UserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_1_2)]
    public async Task<IActionResult> Email(int id)
    {
        var result = await _orders.EmailSupplierAsync(id, UserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_1_1)]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _orders.CancelAsync(id, reason, UserId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------- Dùng chung ----------------

    private async Task FillPermissionsAsync(PurchaseOrderDetailsViewModel model)
    {
        var canCreate = await HasAsync(PermissionCodes.F2_1_1);
        var canSend = await HasAsync(PermissionCodes.F2_1_2);
        var nothingReceived = model.Lines.All(l => l.ReceivedQuantity == 0)
                              && model.Receipts.All(r => r.Status == (byte)GoodsReceiptStatus.Cancelled);
        model.CanEdit = model.IsDraft && canCreate;
        model.CanSend = model.IsDraft && canSend;
        model.CanEmail = model.IsOpen && canSend;
        model.EmailConfigured = _email.IsConfigured;
        model.CanCancel = canCreate && nothingReceived
                          && model.Status is (byte)PurchaseOrderStatus.Draft or (byte)PurchaseOrderStatus.AwaitingDelivery;
        model.CanReceive = model.IsOpen && await HasAsync(PermissionCodes.F2_2_2);
    }

    private async Task<bool> CanViewAsync() => await HasAsync(PermissionCodes.F2_1_3) || await HasAsync(PermissionCodes.F2_1_1);

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string UserId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
