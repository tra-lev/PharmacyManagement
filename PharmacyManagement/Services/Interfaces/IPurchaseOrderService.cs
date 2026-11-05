using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Đặt hàng nhà cung cấp (QT3, nửa đầu) — BFD 2.1.1 lập đơn đặt hàng, 2.1.2 gửi đơn đặt hàng, 2.1.3 theo dõi đơn đặt hàng.
/// Vòng đời: Nháp (sửa được) → Chờ giao (đã gửi) → Giao thiếu / Đã giao (do Nhập kho 2.2 cập nhật) hoặc Hủy.
/// Email cho nhà cung cấp chỉ gửi khi người dùng bấm "Gửi email cho nhà cung cấp".
/// </summary>
public interface IPurchaseOrderService
{
    Task<PurchaseOrderListViewModel> GetListAsync(PurchaseOrderFilter filter, CancellationToken ct = default);

    Task<PurchaseOrderDetailsViewModel?> GetDetailsAsync(int purchaseOrderId, CancellationToken ct = default);

    /// <summary>Form lập đơn; <paramref name="suggest"/> = thêm sẵn các thuốc có tồn khả dụng dưới mức tối thiểu.</summary>
    Task<PurchaseOrderFormViewModel> NewFormAsync(int? supplierId, bool suggest, CancellationToken ct = default);

    /// <summary>Form sửa đơn Nháp; null nếu không có đơn hoặc đơn không còn là Nháp.</summary>
    Task<PurchaseOrderFormViewModel?> EditFormAsync(int purchaseOrderId, CancellationToken ct = default);

    Task PrepareFormAsync(PurchaseOrderFormViewModel form, CancellationToken ct = default);

    Task<OperationResult<int>> CreateAsync(PurchaseOrderFormViewModel form, string userId, CancellationToken ct = default);

    Task<OperationResult> UpdateAsync(int purchaseOrderId, PurchaseOrderFormViewModel form, string userId, CancellationToken ct = default);

    /// <summary>2.1.2: Nháp → Chờ giao; sau đó không sửa được.</summary>
    Task<OperationResult> SendAsync(int purchaseOrderId, string userId, CancellationToken ct = default);

    /// <summary>Gửi email đơn đặt hàng tới địa chỉ email của nhà cung cấp (đơn Chờ giao / Giao thiếu).</summary>
    Task<OperationResult> EmailSupplierAsync(int purchaseOrderId, string userId, CancellationToken ct = default);

    /// <summary>Hủy đơn Nháp hoặc Chờ giao chưa nhận hàng và chưa có phiếu nhập (bắt buộc lý do, không xóa).</summary>
    Task<OperationResult> CancelAsync(int purchaseOrderId, string? reason, string userId, CancellationToken ct = default);
}
