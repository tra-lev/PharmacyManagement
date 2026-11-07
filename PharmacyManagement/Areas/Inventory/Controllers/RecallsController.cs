using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Thu hồi thuốc (DEC-45): xem cần một trong F2.3.3.1, F2.3.3.2, F2.3.3.3; tiếp nhận thông báo F2.3.3.1;
/// danh sách khách đã mua F2.3.3.2; thu hồi tồn và hoàn tất F2.3.3.3. Pharmacist không có mã F2.* (SYS-13).
/// </summary>
[Area("Inventory")]
public class RecallsController : Controller
{
    private const string Nav = "recall";

    private static readonly string[] ViewCodes = { PermissionCodes.F2_3_3_1, PermissionCodes.F2_3_3_2, PermissionCodes.F2_3_3_3 };

    /// <summary>Cùng điều kiện xem chi tiết lô của StockController.</summary>
    private static readonly string[] StockViewCodes =
    {
        PermissionCodes.F2_2_3, PermissionCodes.F2_3_2, PermissionCodes.F2_3_3_2, PermissionCodes.F2_4_1_1
    };

    private readonly IRecallService _recalls;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public RecallsController(IRecallService recalls, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _recalls = recalls;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] RecallFilter filter)
    {
        if (!await AnyAsync(ViewCodes)) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _recalls.GetListAsync(filter);
        model.CanCreate = await HasAsync(PermissionCodes.F2_3_3_1);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await AnyAsync(ViewCodes)) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await LoadAsync(id);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>Phiếu thu hồi (A4).</summary>
    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        if (!await AnyAsync(ViewCodes)) return Forbid();
        var model = await _recalls.GetDetailsAsync(id, includeBuyers: false);
        return model is null ? NotFound() : View(model);
    }

    // ---------------- 2.3.3.1 Tiếp nhận thông báo thu hồi ----------------

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_3_3_1)]
    public async Task<IActionResult> Create(int? medicineId, int? batchId)
    {
        ViewData["Nav"] = Nav;
        return View(await _recalls.NewFormAsync(medicineId, batchId));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_3_3_1)]
    public async Task<IActionResult> Create(RecallFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        if (ModelState.IsValid)
        {
            var result = await _recalls.CreateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _recalls.PrepareFormAsync(form);
        return View(form);
    }

    // ---------------- 2.3.3.3 Lập phiếu thu hồi ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_3_3_3)]
    public async Task<IActionResult> Recover(int id, RecoverForm form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Số lượng hoặc ghi chú chưa hợp lệ. Kiểm tra lại các dòng.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var result = await _recalls.RecoverAsync(id, form, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_3_3_3)]
    public async Task<IActionResult> Complete(int id)
    {
        var result = await _recalls.CompleteAsync(id, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<RecallDetailsViewModel?> LoadAsync(int id)
    {
        var canSeeBuyers = await HasAsync(PermissionCodes.F2_3_3_2);
        var model = await _recalls.GetDetailsAsync(id, canSeeBuyers);
        if (model is null) return null;
        model.CanSeeBuyers = canSeeBuyers;
        model.CanRecover = !model.IsCompleted && await HasAsync(PermissionCodes.F2_3_3_3);
        model.CanOpenInvoices = await HasAsync(PermissionCodes.F4_1_1);
        model.CanOpenBatches = await AnyAsync(StockViewCodes);
        return model;
    }

    private async Task<bool> AnyAsync(IEnumerable<string> codes)
    {
        foreach (var code in codes)
        {
            if (await HasAsync(code)) return true;
        }
        return false;
    }

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
