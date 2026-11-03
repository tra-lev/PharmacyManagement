using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>Quyền của người đang thao tác với form thuốc (controller kiểm tra theo mã BFD rồi truyền vào).</summary>
public sealed record MedicineEditRights(bool CanEditCategory, bool CanEditConversions);

/// <summary>
/// Danh mục thuốc — BFD 1.1.1 thêm thuốc, 1.1.2 sửa thông tin, 1.1.3 phân loại, 1.1.4 quy đổi Hộp – Vỉ – Viên.
/// Giá bán chỉ khai báo một lần (DEC-09); đổi giá về sau ở Chính sách giá (1.5.4, chỉ Owner).
/// Không xóa thuốc: ngừng / mở lại kinh doanh (SYS-06).
/// </summary>
public interface IMedicineCatalogService
{
    Task<MedicineListViewModel> GetListAsync(MedicineFilter filter, CancellationToken ct = default);

    Task<MedicineDetailsViewModel?> GetDetailsAsync(int medicineId, CancellationToken ct = default);

    /// <summary>Form trống để thêm thuốc (mã thuốc dự kiến, danh sách chọn).</summary>
    Task<MedicineFormViewModel> NewFormAsync(MedicineEditRights rights, CancellationToken ct = default);

    Task<MedicineFormViewModel?> EditFormAsync(int medicineId, MedicineEditRights rights, CancellationToken ct = default);

    /// <summary>Nạp lại danh sách chọn và trạng thái khóa khi hiện lại form sau lỗi.</summary>
    Task PrepareFormAsync(MedicineFormViewModel form, MedicineEditRights rights, CancellationToken ct = default);

    Task<OperationResult<int>> CreateAsync(MedicineFormViewModel form, MedicineEditRights rights, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> UpdateAsync(MedicineFormViewModel form, MedicineEditRights rights, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> SetActiveAsync(int medicineId, bool active, string actorUserId, CancellationToken ct = default);
}
