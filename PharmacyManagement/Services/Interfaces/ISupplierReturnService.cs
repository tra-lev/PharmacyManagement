using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Trả nhà cung cấp — BFD 2.3.1 lập phiếu trả (DEC-44). Vòng đời: Nháp (sửa được, chưa trừ tồn) → Đã xuất trả (trừ tồn theo lô,
/// sổ kho loại 3) → Đã quyết toán (thu đủ tiền hoàn bằng phiếu thu — 2.3.5); Nháp → Hủy. Lô trả phải nhập từ chính NCC đó, không đang thu hồi.
/// </summary>
public interface ISupplierReturnService
{
    Task<SupplierReturnListViewModel> GetListAsync(SupplierReturnFilter filter, CancellationToken ct = default);

    Task<SupplierReturnDetailsViewModel?> GetDetailsAsync(int supplierReturnId, CancellationToken ct = default);

    /// <param name="batchId">Điền sẵn một lô (từ trang chi tiết lô) — NCC lấy theo phiếu nhập của lô.</param>
    Task<SupplierReturnFormViewModel> NewFormAsync(int? supplierId, int? receiptId, int? batchId, CancellationToken ct = default);

    Task<SupplierReturnFormViewModel?> EditFormAsync(int supplierReturnId, CancellationToken ct = default);

    Task PrepareFormAsync(SupplierReturnFormViewModel form, CancellationToken ct = default);

    Task<OperationResult<int>> CreateAsync(SupplierReturnFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> UpdateAsync(SupplierReturnFormViewModel form, string actorUserId, CancellationToken ct = default);

    /// <summary>Xuất trả: trừ tồn từng lô + sổ kho loại 3 trong một transaction (SYS-14).</summary>
    Task<OperationResult> IssueAsync(int supplierReturnId, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> CancelAsync(int supplierReturnId, string? reason, string actorUserId, CancellationToken ct = default);
}
