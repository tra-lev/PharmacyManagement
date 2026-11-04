using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PharmacyManagement.Authorization;
using PharmacyManagement.Configuration;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Sales.Controllers;

/// <summary>
/// Bán lẻ và hóa đơn — BFD 3.1.1–3.1.4 lập hóa đơn bán lẻ, 3.3.1 tính tiền, 3.3.2.1–3.3.2.3 thanh toán, 2.3.4 xuất kho FEFO (Hệ thống).
/// Pharmacist, Manager, Owner (seed RolePermissions). Mỗi thao tác kiểm tra mã quyền ở server; người bán phải có ca đang mở.
/// </summary>
[Area("Sales")]
public class InvoicesController : Controller
{
    private const string NavList = "invoices";
    private const string NavPos = "pos";
    private const string NavRx = "rx";

    private readonly ISalesInvoiceService _invoices;
    private readonly IPaymentService _payments;
    private readonly ICustomerService _customers;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;
    private readonly BankTransferOptions _bank;

    public InvoicesController(ISalesInvoiceService invoices, IPaymentService payments, ICustomerService customers,
        IAuthorizationService authorization, UserManager<IdentityUser> users, IOptions<PharmacyOptions> pharmacy)
    {
        _invoices = invoices;
        _payments = payments;
        _customers = customers;
        _authorization = authorization;
        _users = users;
        _bank = pharmacy.Value.BankTransfer;
    }

    // ---------------- Danh sách: hóa đơn của tôi; F4.1.1 (Owner, Manager) xem toàn nhà thuốc ----------------

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] InvoiceFilter filter)
    {
        var viewAll = await HasAsync(PermissionCodes.F4_1_1);
        if (!viewAll && !await CanSellOrCollectAsync()) return Forbid();
        ViewData["Nav"] = NavList;
        var model = await _invoices.GetListAsync(filter, UserId(), viewAll);
        model.CanSell = await HasAsync(PermissionCodes.F3_1_4);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var viewAll = await HasAsync(PermissionCodes.F4_1_1);
        if (!viewAll && !await CanSellOrCollectAsync()) return Forbid();
        ViewData["Nav"] = NavList;
        var model = await _invoices.GetDetailsAsync(id, UserId(), viewAll);
        if (model is null) return NotFound();
        await FillPermissionsAsync(model);
        if (model.CanPay && model.Payment.CashAmount is null && model.Permissions.Cash)
            model.Payment.CashAmount = model.TotalAmount; // gợi ý: thu đủ bằng tiền mặt
        return View(model);
    }

    // ---------------- 3.1.4 Lập hóa đơn bán lẻ / 3.2.4 lập hóa đơn theo đơn (?prescriptionId=) ----------------

    [HttpGet]
    public async Task<IActionResult> Create(int? prescriptionId)
    {
        if (!await CanSellAsync(prescriptionId)) return Forbid();
        SaleFormViewModel model;
        if (prescriptionId is null)
        {
            model = await _invoices.NewFormAsync(UserId());
        }
        else
        {
            var result = await _invoices.NewFromPrescriptionAsync(prescriptionId.Value, UserId());
            if (!result.Succeeded)
            {
                TempData["Error"] = result.Message;
                return RedirectToAction("Details", "Prescriptions", new { id = prescriptionId });
            }
            model = result.Value!;
        }
        ViewData["Nav"] = NavOf(model);
        model.CanExceedDiscountLimit = await HasAsync(PermissionCodes.Sys09DiscountOverLimit);
        model.CanCreateCustomer = await HasAsync(PermissionCodes.F1_2_2);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaleFormViewModel form)
    {
        if (!await CanSellAsync(form.PrescriptionId)) return Forbid();
        ViewData["Nav"] = NavOf(form);
        form.InvoiceId = null;
        var canExceed = await HasAsync(PermissionCodes.Sys09DiscountOverLimit);
        if (ModelState.IsValid)
        {
            var result = await _invoices.CreateAsync(form, UserId(), canExceed);
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _invoices.PrepareFormAsync(form, UserId());
        form.CanExceedDiscountLimit = canExceed;
        form.CanCreateCustomer = await HasAsync(PermissionCodes.F1_2_2);
        return View(form);
    }

    // ---------------- Sửa hóa đơn chờ thanh toán: nút "Sửa thuốc" ở bước thu tiền quay lại màn hình bán ----------------

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!await CanSellAsync(await _invoices.GetPrescriptionIdAsync(id))) return Forbid();
        var result = await _invoices.EditFormAsync(id, UserId());
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
        var model = result.Value!;
        ViewData["Nav"] = NavOf(model);
        model.CanExceedDiscountLimit = await HasAsync(PermissionCodes.Sys09DiscountOverLimit);
        model.CanCreateCustomer = await HasAsync(PermissionCodes.F1_2_2);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, SaleFormViewModel form)
    {
        // Hóa đơn theo đơn hay bán lẻ: lấy từ CSDL, không tin ô ẩn của form
        form.PrescriptionId = await _invoices.GetPrescriptionIdAsync(id);
        if (!await CanSellAsync(form.PrescriptionId)) return Forbid();
        ViewData["Nav"] = NavOf(form);
        form.InvoiceId = id;
        var canExceed = await HasAsync(PermissionCodes.Sys09DiscountOverLimit);
        if (ModelState.IsValid)
        {
            var result = await _invoices.UpdateAsync(id, form, UserId(), canExceed);
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _invoices.PrepareFormAsync(form, UserId());
        form.CanExceedDiscountLimit = canExceed;
        form.CanCreateCustomer = await HasAsync(PermissionCodes.F1_2_2);
        return View(form);
    }

    // ---------------- Thêm nhanh khách hàng tại quầy (1.2.2) — gọi từ màn hình Bán lẻ, trả JSON ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F1_2_2)]
    public async Task<IActionResult> QuickCustomer(QuickCustomerForm form)
    {
        if (!await HasAsync(PermissionCodes.F3_1_4) && !await HasAsync(PermissionCodes.F3_2_1)) return Forbid();
        if (!ModelState.IsValid)
        {
            var error = ModelState.FirstOrDefault(e => e.Value?.Errors.Count > 0);
            return Json(new { ok = false, field = error.Key, message = error.Value?.Errors[0].ErrorMessage ?? "Dữ liệu chưa hợp lệ." });
        }

        // Dùng lại toàn bộ kiểm tra của hồ sơ khách hàng (DEC-25): chặn trùng SĐT, mã KH tự sinh, nhật ký CUSTOMER_CREATE
        var result = await _customers.CreateAsync(new CustomerFormViewModel
        {
            FullName = form.FullName,
            Phone = form.Phone,
            DateOfBirth = form.DateOfBirth,
            Gender = form.Gender
        }, UserId());
        if (!result.Succeeded) return Json(new { ok = false, field = result.Field, message = result.Message });

        var option = await _invoices.GetCustomerOptionAsync(result.Value);
        if (option is null) return Json(new { ok = false, field = (string?)null, message = "Đã thêm khách nhưng chưa đọc lại được. Tải lại trang." });
        return Json(new
        {
            ok = true, id = option.CustomerId, label = option.Label, keywords = option.Keywords, allergies = option.Allergies,
            message = result.Message
        });
    }

    // ---------------- 3.3.2 Thanh toán (một lần cho đủ tiền) → 2.3.4 FEFO ----------------

    [HttpPost]
    public async Task<IActionResult> Pay(int id, [Bind(Prefix = "Payment")] PaymentForm form)
    {
        var permissions = await PaymentPermissionsAsync();
        if (!permissions.Any) return Forbid();

        if (ModelState.IsValid)
        {
            var result = await _payments.PayAsync(id, form, UserId(), permissions);
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field is null ? string.Empty : "Payment." + result.Field, result.Message!);
        }

        ViewData["Nav"] = NavList;
        var model = await _invoices.GetDetailsAsync(id, UserId(), await HasAsync(PermissionCodes.F4_1_1));
        if (model is null) return NotFound();
        await FillPermissionsAsync(model);
        model.Payment = form;
        return View(nameof(Details), model);
    }

    // ---------------- Hủy hóa đơn chờ thanh toán (G-10) — thao tác nhạy cảm SYS09.CancelInvoice ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.Sys09CancelInvoice)]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _invoices.CancelAsync(id, reason, UserId(), hasCancelPermission: true);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------- Dùng chung ----------------

    private async Task FillPermissionsAsync(InvoiceDetailsViewModel model)
    {
        model.Permissions = await PaymentPermissionsAsync();
        model.Bank = _bank.IsConfigured
            ? new BankTransferInfo(_bank.BankBin, _bank.BankName, _bank.AccountNumber, _bank.AccountName)
            : null;
        model.CanPay = model.IsPending && model.IsOwn && model.ShiftOpen && model.Permissions.Any;
        model.CanCancel = model.IsPending && await HasAsync(PermissionCodes.Sys09CancelInvoice);
        model.CanEdit = model.IsPending && model.IsOwn && model.ShiftOpen && await CanSellAsync(model.PrescriptionId);
    }

    private async Task<PaymentPermissions> PaymentPermissionsAsync()
        => new(await HasAsync(PermissionCodes.F3_3_2_1), await HasAsync(PermissionCodes.F3_3_2_2), await HasAsync(PermissionCodes.F3_3_2_3));

    private async Task<bool> CanSellOrCollectAsync()
        => await HasAsync(PermissionCodes.F3_1_4) || await HasAsync(PermissionCodes.F3_2_4) || (await PaymentPermissionsAsync()).Any;

    /// <summary>Bán lẻ: F3.1.4; bán theo đơn: F3.2.4.</summary>
    private Task<bool> CanSellAsync(int? prescriptionId)
        => HasAsync(prescriptionId is null ? PermissionCodes.F3_1_4 : PermissionCodes.F3_2_4);

    private static string NavOf(SaleFormViewModel form) => form.IsPrescriptionSale ? NavRx : NavPos;

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string UserId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
