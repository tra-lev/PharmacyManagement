using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Catalog;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Hồ sơ khách hàng — BFD 1.2.2 Cập nhật khách hàng (Manager, Pharmacist, Owner theo Bảng 2.3).
/// Gồm thông tin liên hệ, bệnh nền (đối chiếu từ khóa chống chỉ định — DEC-08) và dị ứng hoạt chất (cảnh báo 3.1.3.3).
/// Số điện thoại không trùng giữa các khách đang hoạt động (quầy bán tra khách theo số điện thoại).
/// Không xóa khách hàng (đã gắn hóa đơn, đơn thuốc — SYS-06): ngừng / mở lại; bán hàng chỉ chọn khách đang hoạt động.
/// Phân hệ Bán hàng dùng lại service này khi thêm nhanh khách ở quầy.
/// </summary>
public interface ICustomerService
{
    Task<CustomerListViewModel> GetListAsync(CustomerFilter filter, CancellationToken ct = default);

    /// <summary>Hồ sơ, dị ứng và lịch sử mua (3.5.1 — dẫn xuất từ hóa đơn đã thanh toán).</summary>
    Task<CustomerDetailsViewModel?> GetDetailsAsync(int customerId, CancellationToken ct = default);

    /// <summary>Form trống kèm mã khách hàng dự kiến và danh sách gợi ý hoạt chất.</summary>
    Task<CustomerFormViewModel> NewFormAsync(CancellationToken ct = default);

    Task<CustomerFormViewModel?> EditFormAsync(int customerId, CancellationToken ct = default);

    /// <summary>Nạp lại mã dự kiến, danh sách gợi ý và giới hạn ngày khi hiện lại form sau lỗi.</summary>
    Task PrepareFormAsync(CustomerFormViewModel form, CancellationToken ct = default);

    Task<OperationResult<int>> CreateAsync(CustomerFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> UpdateAsync(CustomerFormViewModel form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> SetActiveAsync(int customerId, bool active, string actorUserId, CancellationToken ct = default);
}
