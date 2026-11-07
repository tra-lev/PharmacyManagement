using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Thu hồi thuốc — BFD 2.3.3 (DEC-45). 2.3.3.1 tiếp nhận thông báo: lập phiếu THUHOI-yyMM-nn, khóa các lô (trạng thái Thu hồi),
/// chụp tồn lúc thu hồi. 2.3.3.2 tra cứu lô đang tồn / khách đã mua. 2.3.3.3 lập phiếu thu hồi: trừ tồn theo lô, sổ kho loại 5;
/// hoàn tất khi mọi lô đã thu hồi đủ hoặc hết tồn. Sau tiếp nhận không bỏ lô khỏi phiếu.
/// </summary>
public interface IRecallService
{
    Task<RecallListViewModel> GetListAsync(RecallFilter filter, CancellationToken ct = default);

    Task<RecallDetailsViewModel?> GetDetailsAsync(int recallId, bool includeBuyers, CancellationToken ct = default);

    /// <param name="batchId">Điền sẵn từ trang chi tiết lô: chọn thuốc của lô và đánh dấu lô đó.</param>
    Task<RecallFormViewModel> NewFormAsync(int? medicineId, int? batchId, CancellationToken ct = default);

    Task PrepareFormAsync(RecallFormViewModel form, CancellationToken ct = default);

    Task<OperationResult<int>> CreateAsync(RecallFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> RecoverAsync(int recallId, RecoverForm form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> CompleteAsync(int recallId, string actorUserId, CancellationToken ct = default);

    /// <summary>Phiếu thu hồi chưa hoàn tất (số trên menu).</summary>
    Task<int> OpenCountAsync(CancellationToken ct = default);
}
