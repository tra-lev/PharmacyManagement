using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Hủy thuốc — BFD 2.3.2 lập biên bản hủy thuốc (Manager, Owner; Pharmacist bị khóa — SYS-13). DEC-41.
/// Vòng đời biên bản: Nháp (sửa được, chưa trừ tồn) → Đã ghi sổ (trừ tồn theo lô + sổ kho loại 4, không sửa) hoặc Hủy.
/// Lô đang thu hồi không hủy ở đây (xử lý ở Thu hồi 2.3.3).
/// </summary>
public interface IStockDisposalService
{
    Task<DisposalListViewModel> GetListAsync(DisposalFilter filter, CancellationToken ct = default);

    Task<DisposalDetailsViewModel?> GetDetailsAsync(int disposalId, CancellationToken ct = default);

    /// <summary>
    /// Form lập biên bản. <paramref name="batchId"/>: điền sẵn một lô (từ trang chi tiết lô);
    /// <paramref name="expiredLots"/>: điền sẵn mọi lô đã hết hạn còn tồn (từ Tồn kho → Theo lô → Hết hạn).
    /// </summary>
    Task<DisposalFormViewModel> NewFormAsync(int? batchId, bool expiredLots, CancellationToken ct = default);

    /// <summary>Form sửa biên bản; null nếu không có biên bản.</summary>
    Task<DisposalFormViewModel?> EditFormAsync(int disposalId, CancellationToken ct = default);

    /// <summary>Nạp lại danh sách lô khi trả form về vì lỗi.</summary>
    Task PrepareFormAsync(DisposalFormViewModel form, CancellationToken ct = default);

    Task<OperationResult<int>> CreateAsync(DisposalFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> UpdateAsync(DisposalFormViewModel form, string actorUserId, CancellationToken ct = default);

    /// <summary>Ghi sổ: trừ tồn từng lô + InventoryTransactions loại 4 trong một transaction (SYS-14).</summary>
    Task<OperationResult> PostAsync(int disposalId, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> CancelAsync(int disposalId, string? reason, string actorUserId, CancellationToken ct = default);
}
