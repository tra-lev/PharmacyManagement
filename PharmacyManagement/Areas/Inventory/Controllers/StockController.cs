using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Authorization;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Areas.Inventory.Controllers;

/// <summary>
/// Tồn kho &amp; lô (DEC-39). Hai mức xem, kiểm tra ở server:
/// - Có quyền kho (một trong F2.2.3, F2.3.2, F2.3.3.2, F2.4.1.1 — cùng danh sách mục menu "Tồn kho &amp; lô"; Manager, Owner): xem đầy đủ,
///   gồm giá vốn, phiếu nhập, nhà cung cấp, sổ kho, chi tiết lô; danh sách hóa đơn đã xuất từ lô cần thêm F2.3.3.2.
/// - Chỉ có F3.1.1 tra cứu thuốc (Pharmacist): tồn khả dụng, lô, hạn dùng, vị trí — không giá vốn, không sổ kho, không mở chi tiết lô.
/// Đổi vị trí lô (2.2.4) cần F2.2.4. Pharmacist không bao giờ có mã F2.* (SYS-13, khóa ở PermissionHandler).
/// </summary>
[Area("Inventory")]
public class StockController : Controller
{
    private const string NavStock = "stock";
    private const string NavLookup = "lookup";

    private static readonly string[] StockViewCodes =
    {
        PermissionCodes.F2_2_3, PermissionCodes.F2_3_2, PermissionCodes.F2_3_3_2, PermissionCodes.F2_4_1_1
    };

    private readonly IStockService _stock;
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<IdentityUser> _users;

    public StockController(IStockService stock, IAuthorizationService authorization, UserManager<IdentityUser> users)
    {
        _stock = stock;
        _authorization = authorization;
        _users = users;
    }

    /// <summary>Tồn theo thuốc + thẻ cảnh báo cận hạn / dưới mức tối thiểu.</summary>
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] StockFilter filter)
    {
        var full = await IsFullViewAsync();
        if (!full && !await HasAsync(PermissionCodes.F3_1_1)) return Forbid();
        ViewData["Nav"] = full ? NavStock : NavLookup;
        var model = await _stock.GetMedicineListAsync(filter);
        model.FullView = full;
        return View(model);
    }

    /// <summary>Tồn theo lô (hạn dùng gần trước).</summary>
    [HttpGet]
    public async Task<IActionResult> Batches([FromQuery] BatchFilter filter)
    {
        var full = await IsFullViewAsync();
        if (!full && !await HasAsync(PermissionCodes.F3_1_1)) return Forbid();
        ViewData["Nav"] = full ? NavStock : NavLookup;
        var model = await _stock.GetBatchListAsync(filter);
        model.FullView = full;
        model.Links = full ? await LinksAsync() : StockLinks.None;
        model.CanDispose = full && await HasAsync(PermissionCodes.F2_3_2);
        return View(model);
    }

    /// <summary>Tra cứu một thuốc: thông tin, giá bán, các lô còn hàng; người có quyền kho xem thêm sổ kho.</summary>
    [HttpGet]
    public async Task<IActionResult> Medicine(int id, int page = 1)
    {
        var full = await IsFullViewAsync();
        if (!full && !await HasAsync(PermissionCodes.F3_1_1)) return Forbid();
        ViewData["Nav"] = full ? NavStock : NavLookup;
        var model = await _stock.GetMedicineAsync(id, full, page);
        if (model is null) return NotFound();
        model.FullView = full;
        model.Links = full ? await LinksAsync() : StockLinks.None;
        model.CanSetPolicy = full && (await HasAsync(PermissionCodes.F1_5_2) || await HasAsync(PermissionCodes.F1_5_3));
        return View(model);
    }

    /// <summary>Chi tiết lô: sổ kho của lô, hóa đơn đã xuất (F2.3.3.2), đổi vị trí (F2.2.4).</summary>
    [HttpGet]
    public async Task<IActionResult> Batch(int id)
    {
        if (!await IsFullViewAsync()) return Forbid();
        ViewData["Nav"] = NavStock;
        var canSeeSales = await HasAsync(PermissionCodes.F2_3_3_2);
        var model = await _stock.GetBatchAsync(id, canSeeSales);
        if (model is null) return NotFound();
        model.CanSeeSales = canSeeSales;
        model.CanMove = model.Movable && await HasAsync(PermissionCodes.F2_2_4);
        model.CanDispose = model.Quantity > 0 && model.Status != (byte)BatchStatus.Recalled && await HasAsync(PermissionCodes.F2_3_2);
        model.CanReturn = model.Quantity > 0 && model.Status != (byte)BatchStatus.Recalled && model.GoodsReceiptId is not null
                          && await HasAsync(PermissionCodes.F2_3_1);
        model.CanRecall = model.Status != (byte)BatchStatus.Recalled && await HasAsync(PermissionCodes.F2_3_3_1);
        model.Links = await LinksAsync();
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.F2_2_4)]
    public async Task<IActionResult> MoveBatch(int id, int? locationId)
    {
        var result = await _stock.MoveBatchAsync(id, locationId, ActorId());
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Batch), new { id });
    }

    private async Task<bool> IsFullViewAsync()
    {
        foreach (var code in StockViewCodes)
        {
            if (await HasAsync(code)) return true;
        }
        return false;
    }

    /// <summary>Chỉ hiện liên kết tới trang chứng từ người dùng mở được.</summary>
    private async Task<StockLinks> LinksAsync() => new(
        Receipts: await HasAsync(PermissionCodes.F2_2_1_1) || await HasAsync(PermissionCodes.F2_2_2),
        Invoices: await HasAsync(PermissionCodes.F4_1_1),
        Returns: await HasAsync(PermissionCodes.F3_4_1) || await HasAsync(PermissionCodes.F3_4_4)
                 || (await HasAsync(PermissionCodes.F3_4_2) && await HasAsync(PermissionCodes.F3_4_3)),
        Catalog: await HasAsync(PermissionCodes.F1_1_1) || await HasAsync(PermissionCodes.F1_1_2),
        Disposals: await HasAsync(PermissionCodes.F2_3_2),
        SupplierReturns: await HasAsync(PermissionCodes.F2_3_1) || await HasAsync(PermissionCodes.F2_3_5),
        Recalls: await HasAsync(PermissionCodes.F2_3_3_1) || await HasAsync(PermissionCodes.F2_3_3_2) || await HasAsync(PermissionCodes.F2_3_3_3),
        Stocktakes: await HasAsync(PermissionCodes.F2_4_1_1) || await HasAsync(PermissionCodes.F2_4_1_2) || await HasAsync(PermissionCodes.F2_4_2)
                    || await HasAsync(PermissionCodes.F2_4_3) || await HasAsync(PermissionCodes.F2_4_3_Approve));

    private async Task<bool> HasAsync(string code) => (await _authorization.AuthorizeAsync(User, code)).Succeeded;

    private string ActorId() => _users.GetUserId(User) ?? throw new InvalidOperationException("Chưa đăng nhập.");
}
