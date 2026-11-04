using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Sales;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Đổi trả — BFD 3.4.1 tiếp nhận yêu cầu (tra hóa đơn gốc, chỉ đọc), 3.4.2 kiểm tra điều kiện, 3.4.3 hoàn tiền
/// (hàng "chờ nhập"), 3.4.4 nhập lại tồn hoặc không nhập. Chỉ trả trong ngày bán (ngày thanh toán hóa đơn), đối chiếu số đã bán.
/// </summary>
public interface IReturnService
{
    /// <summary>Tìm hóa đơn đã thanh toán theo mã hóa đơn hoặc SĐT khách (mới nhất trước).</summary>
    Task<ReturnLookupViewModel> LookupAsync(string? q, CancellationToken ct = default);

    /// <summary>Hóa đơn gốc + số còn trả được từng dòng; null nếu không có hóa đơn.</summary>
    /// <param name="requestId">Mở từ yêu cầu của dược sĩ: điền sẵn số lượng, tình trạng, lý do.</param>
    Task<ReturnFormViewModel?> NewFormAsync(int invoiceId, string userId, long? requestId, CancellationToken ct = default);

    /// <summary>Nạp lại phần hiển thị khi trả form về (giữ số người dùng đã nhập).</summary>
    Task<bool> PrepareFormAsync(ReturnFormViewModel form, int invoiceId, string userId, CancellationToken ct = default);

    /// <summary>Lập phiếu đổi trả + hoàn tiền: trạng thái Đã hoàn tiền — chờ nhập kho, mọi dòng Chờ nhập.</summary>
    Task<OperationResult<int>> CreateAsync(int invoiceId, ReturnFormViewModel form, string userId, CancellationToken ct = default);

    Task<ReturnListViewModel> GetListAsync(ReturnFilter filter, CancellationToken ct = default);

    Task<ReturnDetailsViewModel?> GetDetailsAsync(int returnId, CancellationToken ct = default);

    /// <summary>3.4.4: nhập lại một dòng vào lô đã xuất (cộng tồn + sổ kho) hoặc không nhập (bắt buộc lý do). Hết dòng chờ → Hoàn tất.</summary>
    Task<OperationResult> RestockAsync(int returnId, RestockForm form, string userId, CancellationToken ct = default);

    /// <summary>3.4.1: dược sĩ ghi số lượng, tình trạng, lý do và gửi yêu cầu cho quản lý (AuditLogs RETURN_REQUEST). Mỗi hóa đơn một yêu cầu đang chờ.</summary>
    Task<OperationResult> SendRequestAsync(int invoiceId, ReturnFormViewModel form, string userId, CancellationToken ct = default);

    /// <summary>Quản lý từ chối yêu cầu (bắt buộc lý do).</summary>
    Task<OperationResult> RejectRequestAsync(long requestId, string? reason, string userId, CancellationToken ct = default);

    /// <summary>Yêu cầu gửi hôm nay kèm kết quả; <paramref name="onlyUserId"/> = chỉ yêu cầu của người này.</summary>
    Task<IReadOnlyList<ReturnRequestInfo>> GetRequestsTodayAsync(string? onlyUserId, CancellationToken ct = default);

    /// <summary>Số yêu cầu đang chờ (số trên menu Đổi trả của quản lý).</summary>
    Task<int> PendingRequestCountAsync(CancellationToken ct = default);

    /// <summary>Các lần trả của một hóa đơn (trang hóa đơn).</summary>
    Task<IReadOnlyList<InvoiceReturnInfo>> GetInvoiceReturnsAsync(int invoiceId, CancellationToken ct = default);

    /// <summary>Hóa đơn còn trả được hôm nay (đã thanh toán trong ngày, còn số lượng chưa trả).</summary>
    Task<bool> CanReturnTodayAsync(int invoiceId, CancellationToken ct = default);
}
