using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Nhập kho (QT3, nửa sau) — BFD 2.2.1.1 đối chiếu đơn đặt hàng, 2.2.1.2 kiểm tra hạn dùng/chất lượng, 2.2.1.3 ghi nhận hàng thiếu/hỏng,
/// 2.2.2 lập phiếu nhập kho, 2.2.3 ghi số lô/hạn dùng, 2.2.4 xếp thuốc vào vị trí (Manager, Owner; Pharmacist bị khóa — SYS-13).
/// Vòng đời phiếu: Đang kiểm nhận (sửa được, chưa cộng tồn) → Đã nhập kho (sinh lô + sổ kho, không sửa) hoặc Hủy.
/// </summary>
public interface IGoodsReceiptService
{
    Task<ReceiptListViewModel> GetListAsync(ReceiptFilter filter, CancellationToken ct = default);

    Task<ReceiptDetailsViewModel?> GetDetailsAsync(int receiptId, CancellationToken ct = default);

    /// <summary>Form lập phiếu; có <paramref name="purchaseOrderId"/> thì điền sẵn các dòng còn thiếu của đơn.</summary>
    Task<ReceiptFormViewModel> NewFormAsync(int? supplierId, int? purchaseOrderId, CancellationToken ct = default);

    /// <summary>Form sửa phiếu đang kiểm nhận; null nếu không có. Phiếu đã nhập kho / đã hủy: trả form để controller từ chối.</summary>
    Task<ReceiptFormViewModel?> EditFormAsync(int receiptId, CancellationToken ct = default);

    /// <summary>Nạp lại danh sách chọn và thông tin hiển thị khi hiện lại form sau lỗi.</summary>
    Task PrepareFormAsync(ReceiptFormViewModel form, CancellationToken ct = default);

    /// <summary>Trạng thái phiếu (GoodsReceiptStatus); null nếu không có.</summary>
    Task<byte?> GetStatusAsync(int receiptId, CancellationToken ct = default);

    Task<OperationResult<int>> CreateAsync(ReceiptFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> UpdateAsync(ReceiptFormViewModel form, string actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Nhập kho: trong MỘT transaction — phiếu → Đã nhập kho, mỗi dòng có hàng tốt sinh một lô, ghi InventoryTransactions (loại 1),
    /// cộng số đã nhận của đơn đặt hàng và cập nhật trạng thái đơn. Warning liệt kê lô có hạn dùng ngắn.
    /// </summary>
    Task<OperationResult> PostAsync(int receiptId, string actorUserId, CancellationToken ct = default);

    /// <summary>Hủy phiếu đang kiểm nhận (không xóa — SYS-06).</summary>
    Task<OperationResult> CancelAsync(int receiptId, string? reason, string actorUserId, CancellationToken ct = default);
}
