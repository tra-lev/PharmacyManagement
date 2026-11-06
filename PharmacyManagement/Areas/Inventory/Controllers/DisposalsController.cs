using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Hủy thuốc — BFD 2.3.2 lập biên bản hủy thuốc (DEC-41). Mọi thao tác cần F2.3.2 (Manager, Owner);
/// Pharmacist không bao giờ có mã F2.* (SYS-13, khóa ở PermissionHandler).
/// </summary>
[Area("Inventory")]
[Authorize(Policy = PermissionCodes.F2_3_2)]
public class DisposalsController : Controller
{
    private const string Nav = "disposal";

    private readonly IStockDisposalService _disposals;
    private readonly UserManager<IdentityUser> _users;

    public DisposalsController(IStockDisposalService disposals, UserManager<IdentityUser> users)
    {
        _disposals = disposals;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DisposalFilter filter)
    {
        ViewData["Nav"] = Nav;
        return View(await _disposals.GetListAsync(filter));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        ViewData["Nav"] = Nav;
        var model = await _disposals.GetDetailsAsync(id);
        if (model is null) return NotFound();
        model.CanEdit = model.IsDraft;
        return View(model);
    }

    /// <summary>Trang in biên bản hủy (A4) — In hoặc Lưu PDF bằng trình duyệt.</summary>
    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        var model = await _disposals.GetDetailsAsync(id);
        if (model is null) return NotFound();
        return View(model);
    }

    // ---------------- Lập / sửa biên bản (Nháp) ----------------

    /// <param name="batchId">Điền sẵn một lô (từ trang chi tiết lô).</param>
    /// <param name="expired">Điền sẵn mọi lô đã hết hạn còn tồn (từ Tồn kho → Theo lô → Hết hạn).</param>
    [HttpGet]
    public async Task<IActionResult> Create(int? batchId, bool expired = false)
    {
        ViewData["Nav"] = Nav;
        return View(await _disposals.NewFormAsync(batchId, expired));
    }

    [HttpPost]
    public async Task<IActionResult> Create(DisposalFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.DisposalId = null;
        if (ModelState.IsValid)
        {
            var result = await _disposals.CreateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _disposals.PrepareFormAsync(form);
        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Nav"] = Nav;
        var form = await _disposals.EditFormAsync(id);
        if (form is null) return NotFound();
        if (!form.IsDraft)
        {
            TempData["Error"] = "Biên bản đã ghi sổ hoặc đã hủy nên không sửa được.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, DisposalFormViewModel form)
    {
        ViewData["Nav"] = Nav;
        form.DisposalId = id;
        if (ModelState.IsValid)
        {
            var result = await _disposals.UpdateAsync(form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        await _disposals.PrepareFormAsync(form);
        return View(form);
    }

    // ---------------- Ghi sổ / hủy biên bản ----------------

    [HttpPost]
    public async Task<IActionResult> Post(int id)
    {
        var result = await _disposals.PostAsync(id, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var result = await _disposals.CancelAsync(id, reason, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
