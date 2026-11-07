using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Kiểm kê (DEC-43): xem cần một trong F2.4.1.1, F2.4.1.2, F2.4.2, F2.4.3, F2.4.3-APPROVE; in phiếu đếm F2.4.1.1;
/// tạo phiếu + nhập số thực tế F2.4.1.2; nộp biên bản thêm F2.4.2; duyệt / từ chối F2.4.3-APPROVE (Owner, cần email đã xác minh);
/// điều chỉnh tồn F2.4.3. Pharmacist không có mã F2.* (SYS-13).
/// </summary>
[Area("Inventory")]
public class StocktakesController : Controller
{
    private const string Nav = "stocktake";

    private static readonly string[] ViewCodes =
    {
        PermissionCodes.F2_4_1_1, PermissionCodes.F2_4_1_2, PermissionCodes.F2_4_2, PermissionCodes.F2_4_3, PermissionCodes.F2_4_3_Approve
    };

    private readonly IStocktakeService _stocktakes;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public StocktakesController(IStocktakeService stocktakes, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _stocktakes = stocktakes;
        _authorization = authorization;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] StocktakeFilter filter)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _stocktakes.GetListAsync(filter);
        model.CanCreate = await HasAsync(PermissionCodes.F2_4_1_1) || await HasAsync(PermissionCodes.F2_4_1_2);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _stocktakes.GetDetailsAsync(id);
        if (model is null) return NotFound();
        await FillPermissionsAsync(model);
        return View(model);
    }

    /// <summary>Biên bản kiểm kê (A4).</summary>
    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        var model = await _stocktakes.GetDetailsAsync(id);
        if (model is null) return NotFound();
        if (model.IsCounting) return RedirectToAction(nameof(CountSheet), new { id });
        return View(model);
    }

    // ---------------- 2.4.1.1 Chọn phạm vi, in phiếu đếm ----------------

    [HttpGet]
    public async Task<IActionResult> Create([FromQuery] StocktakeScope pick)
    {
        var canPrint = await HasAsync(PermissionCodes.F2_4_1_1);
        var canStart = await HasAsync(PermissionCodes.F2_4_1_2);
        if (!canPrint && !canStart) return Forbid();
        ViewData["Nav"] = Nav;
        var model = await _stocktakes.ScopeFormAsync(pick);
        model.CanPrint = canPrint;
        model.CanStart = canStart;
        return View(model);
    }

    /// <summary>Phiếu đếm (A4, không có số tồn hệ thống): theo phiếu đã tạo (<paramref name="id"/>) hoặc theo phạm vi chọn.</summary>
    /// <remarks>Tham số đặt tên "pick" (không trùng thuộc tính Scope) để model binding đọc đúng ?Scope=…&amp;LocationId=….</remarks>
    [HttpGet]
    public async Task<IActionResult> CountSheet(int? id, [FromQuery] StocktakeScope pick)
    {
        if (!await HasAsync(PermissionCodes.F2_4_1_1) && !await HasAsync(PermissionCodes.F2_4_1_2)) return Forbid();
        var model = await _stocktakes.CountSheetAsync(id, pick);
        if (model is null)
        {
            TempData["Error"] = id is null ? "Chọn phạm vi kiểm kê trước khi in phiếu." : "Không tìm thấy phiếu kiểm kê.";
            return RedirectToAction(nameof(Create));
        }
        return View(model);
    }

    // ---------------- 2.4.1.2 Tạo phiếu, nhập số thực tế; 2.4.2 nộp biên bản ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_4_1_2)]
    public async Task<IActionResult> Start(StocktakeScope pick)
    {
        var result = await _stocktakes.StartAsync(pick, ActorId());
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Create), new { pick.Scope, pick.LocationId, pick.CategoryId });
        }
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Count), new { id = result.Value });
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.F2_4_1_2)]
    public async Task<IActionResult> Count(int id)
    {
        ViewData["Nav"] = Nav;
        var model = await _stocktakes.GetDetailsAsync(id);
        if (model is null) return NotFound();
        if (!model.IsCounting) return RedirectToAction(nameof(Details), new { id });
        await FillPermissionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_4_1_2)]
    public async Task<IActionResult> Count(int id, StocktakeCountForm form)
    {
        if (!await HasAsync(PermissionCodes.F2_4_2)) return Forbid();
        if (ModelState.IsValid)
        {
            var result = await _stocktakes.SubmitAsync(id, form, ActorId());
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(result.Field ?? string.Empty, result.Message!);
        }
        else
        {
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "Có ô số lượng không đúng. Số lượng thực tế là số không âm.");
        }

        ViewData["Nav"] = Nav;
        var model = await _stocktakes.GetDetailsAsync(id);
        if (model is null) return NotFound();
        if (!model.IsCounting) return RedirectToAction(nameof(Details), new { id });
        await FillPermissionsAsync(model);
        model.Entered = form.Lines.GroupBy(l => l.StocktakeDetailId).ToDictionary(g => g.Key, g => g.Last());
        model.EnteredNotes = form.Notes;
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_4_1_2)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _stocktakes.DeleteAsync(id, ActorId());
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    // ---------------- Duyệt (Owner) / điều chỉnh tồn (2.4.3) ----------------

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_4_3_Approve)]
    public async Task<IActionResult> Approve(int id, string? note)
    {
        var result = await _stocktakes.ApproveAsync(id, note, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_4_3_Approve)]
    public async Task<IActionResult> Reject(int id, string? note)
    {
        var result = await _stocktakes.RejectAsync(id, note, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_4_3)]
    public async Task<IActionResult> Adjust(int id)
    {
        var result = await _stocktakes.AdjustAsync(id, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task FillPermissionsAsync(StocktakeDetailsViewModel model)
    {
        var status = model.Status;
        model.CanCount = model.IsCounting && await HasAsync(PermissionCodes.F2_4_1_2);
        model.CanSubmit = model.CanCount && await HasAsync(PermissionCodes.F2_4_2);
        model.CanDelete = model.CanCount;
        model.CanApprove = status == (byte)Models.Enums.StocktakeStatus.PendingApproval && await HasAsync(PermissionCodes.F2_4_3_Approve);
        model.CanAdjust = status == (byte)Models.Enums.StocktakeStatus.Approved && await HasAsync(PermissionCodes.F2_4_3);
    }

    private async Task<bool> CanViewAsync()
    {
        foreach (var code in ViewCodes)
        {
            if (await HasAsync(code)) return true;
        }
        return false;
    }

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
