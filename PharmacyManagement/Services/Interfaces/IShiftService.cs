using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Sales;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Ca làm việc — BFD 1.4.1 Mở ca, 1.4.2 Bàn giao ca, 1.4.3 Chốt tiền cuối ca (Pharmacist, Manager, Owner — QT7).
/// Vòng đời: Đang mở → Đã bàn giao → Đã chốt (CK_Shifts_Lifecycle); mỗi nhân viên tối đa 1 ca chưa chốt (UX_Shifts_OpenPerEmployee).
/// Mỗi người chỉ thao tác trên ca của chính mình.
/// </summary>
public interface IShiftService
{
    /// <summary>Trang "Ca của tôi": ca chưa chốt (kèm số liệu tiền), gợi ý tiền đầu ca, lịch sử ca đã chốt.</summary>
    Task<MyShiftViewModel> GetMyShiftAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Chi tiết một ca. <paramref name="canViewAll"/> = false: chỉ ca của chính người dùng (null nếu không phải);
    /// true (F4.1.1 — Owner, Manager): xem được mọi ca, chỉ xem.
    /// </summary>
    Task<ShiftDetailsViewModel?> GetDetailsAsync(int shiftId, string userId, bool canViewAll, CancellationToken ct = default);

    /// <summary>Danh sách ca toàn nhà thuốc (F4.1.1): mọi nhân viên, mọi trạng thái, mới nhất trước.</summary>
    Task<ShiftListViewModel> GetAllAsync(ShiftFilter filter, CancellationToken ct = default);

    Task<OperationResult<int>> OpenAsync(OpenShiftForm form, string userId, CancellationToken ct = default);

    /// <summary>Bàn giao: bị chặn khi ca còn hóa đơn chờ thanh toán. Sau bàn giao không lập hóa đơn mới trong ca.</summary>
    Task<OperationResult> HandOverAsync(HandoverForm form, string userId, CancellationToken ct = default);

    /// <summary>Chốt tiền: chỉ sau khi bàn giao; chênh lệch khác 0 phải có giải trình.</summary>
    Task<OperationResult> CloseAsync(CloseShiftForm form, string userId, CancellationToken ct = default);

    /// <summary>
    /// Ca ĐANG MỞ (chưa bàn giao) của người dùng — dùng khi lập hóa đơn (3.1.4, 3.2.4, quy tắc 7) và khi hoàn tiền mặt
    /// (3.4.3, DEC-11). null = không có ca đang mở → phải từ chối thao tác.
    /// </summary>
    Task<int?> GetOpenShiftIdAsync(string userId, CancellationToken ct = default);
}
