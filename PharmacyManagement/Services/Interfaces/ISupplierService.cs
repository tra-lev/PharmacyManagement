using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Danh mục nhà cung cấp — BFD 1.2.1 Cập nhật nhà cung cấp (Manager, Owner).
/// Không xóa nhà cung cấp (đã gắn đơn đặt hàng, phiếu nhập, công nợ — SYS-06): ngừng / mở lại giao dịch.
/// Đặt hàng, nhập kho (nhóm 2) chỉ được chọn nhà cung cấp đang giao dịch (IsActive = 1).
/// </summary>
public interface ISupplierService
{
    Task<SupplierListViewModel> GetListAsync(SupplierFilter filter, CancellationToken ct = default);

    Task<SupplierDetailsViewModel?> GetDetailsAsync(int supplierId, CancellationToken ct = default);

    /// <summary>Form trống kèm mã nhà cung cấp dự kiến.</summary>
    Task<SupplierFormViewModel> NewFormAsync(CancellationToken ct = default);

    Task<SupplierFormViewModel?> EditFormAsync(int supplierId, CancellationToken ct = default);

    Task<OperationResult<int>> CreateAsync(SupplierFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> UpdateAsync(SupplierFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> SetActiveAsync(int supplierId, bool active, string actorUserId, CancellationToken ct = default);
}
