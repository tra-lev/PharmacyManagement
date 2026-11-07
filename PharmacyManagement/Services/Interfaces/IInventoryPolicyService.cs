using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Thiết lập định mức tồn (1.5.2) và ngưỡng cận hạn (1.5.3) — DEC-42. Ghi InventoryPolicies (thuốc + kho KHO01) và
/// PharmacySettings.ExpiryWarningDays; Tồn kho, gợi ý đặt hàng, nhập kho đọc lại các giá trị này (CHANGELOG_DB G-02).
/// </summary>
public interface IInventoryPolicyService
{
    Task<PolicyListViewModel> GetListAsync(PolicyFilter filter, CancellationToken ct = default);

    /// <summary>Lưu các dòng đã đổi; trường người dùng không có quyền sửa thì giữ nguyên giá trị cũ.</summary>
    Task<OperationResult> SaveAsync(IReadOnlyList<PolicyRowInput> rows, bool canEditMinimum, bool canEditWarning, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> SaveDefaultWarningDaysAsync(int? days, string actorUserId, CancellationToken ct = default);
}
