using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Khai báo tủ, kệ lưu trữ — BFD 1.5.1 (Manager, Owner; DEC-42). Cây hai tầng: Tủ (không có cha) → Kệ (thuộc một tủ cùng kho);
/// lô thuốc chỉ xếp lên kệ. Xóa chỉ khi tủ / kệ chưa từng dùng (không lô, phiếu nhập, kiểm kê, sổ nhiệt độ nào gắn với nó; tủ không còn kệ).
/// </summary>
public interface ILocationService
{
    Task<LocationListViewModel> GetTreeAsync(CancellationToken ct = default);

    /// <param name="type">1 Tủ, 2 Kệ.</param>
    /// <param name="parentId">Tủ chọn sẵn khi thêm kệ.</param>
    Task<LocationFormViewModel> NewFormAsync(byte type, int? parentId, CancellationToken ct = default);

    Task<LocationFormViewModel?> EditFormAsync(int locationId, CancellationToken ct = default);

    /// <summary>Nạp lại danh sách tủ khi trả form về vì lỗi.</summary>
    Task PrepareFormAsync(LocationFormViewModel form, CancellationToken ct = default);

    Task<OperationResult> CreateAsync(LocationFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> UpdateAsync(LocationFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> DeleteAsync(int locationId, string actorUserId, CancellationToken ct = default);
}
