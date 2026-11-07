using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Kiểm kê — BFD 2.4.1.1 in phiếu, 2.4.1.2 nhập số thực tế, 2.4.2 lập biên bản, 2.4.3 điều chỉnh tồn; Owner duyệt (F2.4.3-APPROVE). DEC-43.
/// Vòng đời: Đang kiểm đếm → Chờ duyệt → Đã duyệt → Đã điều chỉnh, hoặc Chờ duyệt → Từ chối (kiểm lại bằng phiếu mới).
/// Điều chỉnh = tồn hiện tại + chênh lệch (thực tế − tồn lúc tạo phiếu), không âm; ghi sổ kho loại 6.
/// </summary>
public interface IStocktakeService
{
    Task<StocktakeListViewModel> GetListAsync(StocktakeFilter filter, CancellationToken ct = default);

    Task<StocktakeScopeViewModel> ScopeFormAsync(StocktakeScope scope, CancellationToken ct = default);

    /// <summary>Phiếu đếm in ra: theo phiếu đã tạo (<paramref name="stocktakeId"/>) hoặc theo phạm vi chọn. Null nếu phạm vi không hợp lệ.</summary>
    Task<CountSheetViewModel?> CountSheetAsync(int? stocktakeId, StocktakeScope scope, CancellationToken ct = default);

    /// <summary>Tạo phiếu (Đang kiểm đếm) với mọi lô còn tồn trong phạm vi, chụp tồn hệ thống lúc này.</summary>
    Task<OperationResult<int>> StartAsync(StocktakeScope scope, string actorUserId, CancellationToken ct = default);

    Task<StocktakeDetailsViewModel?> GetDetailsAsync(int stocktakeId, CancellationToken ct = default);

    /// <summary>Ghi số thực tế mọi lô và nộp biên bản (Đang kiểm đếm → Chờ duyệt).</summary>
    Task<OperationResult> SubmitAsync(int stocktakeId, StocktakeCountForm form, string actorUserId, CancellationToken ct = default);

    /// <summary>Xóa phiếu đang kiểm đếm (chưa ảnh hưởng tồn kho).</summary>
    Task<OperationResult> DeleteAsync(int stocktakeId, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> ApproveAsync(int stocktakeId, string? note, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> RejectAsync(int stocktakeId, string? note, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> AdjustAsync(int stocktakeId, string actorUserId, CancellationToken ct = default);

    /// <summary>Số phiếu chờ duyệt và đã duyệt chờ điều chỉnh (số cạnh mục menu).</summary>
    Task<(int Pending, int Approved)> WorkCountsAsync(CancellationToken ct = default);
}
