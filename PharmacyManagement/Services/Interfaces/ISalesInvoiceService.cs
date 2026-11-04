using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Sales;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Hóa đơn bán lẻ — BFD 3.1.1 tra cứu thuốc khi bán, 3.1.2 quy đổi đơn vị, 3.1.3 cảnh báo an toàn (dị ứng, tương tác),
/// 3.1.4 lập hóa đơn bán lẻ, 3.3.1 tính tổng tiền (khuyến mãi, giảm thêm trong giới hạn chính sách giá).
/// Hóa đơn lưu ở trạng thái Chờ thanh toán, gắn ca ĐANG MỞ của người bán (quy tắc 7); bán lẻ chặn thuốc kê đơn.
/// Thanh toán và trừ tồn: <see cref="IPaymentService"/>.
/// </summary>
public interface ISalesInvoiceService
{
    /// <summary>Form bán lẻ trống (một dòng thuốc) kèm danh sách chọn.</summary>
    Task<SaleFormViewModel> NewFormAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Form bán theo đơn (3.2.4): điền sẵn thuốc và số lượng kê, khách hàng theo đơn. Đơn phải Chưa bán, còn hạn mua
    /// (<see cref="PrescriptionRules.ValidDays"/> ngày kể từ ngày kê) và chưa có hóa đơn chờ thanh toán.
    /// </summary>
    Task<OperationResult<SaleFormViewModel>> NewFromPrescriptionAsync(int prescriptionId, string userId, CancellationToken ct = default);

    /// <summary>Nạp lại danh sách chọn, ca, chính sách giá và cảnh báo an toàn của các dòng hiện có (khi hiện lại form).</summary>
    Task PrepareFormAsync(SaleFormViewModel form, string userId, CancellationToken ct = default);

    /// <summary>
    /// Lập hóa đơn Chờ thanh toán. Có cảnh báo an toàn mà chưa tích xác nhận → thất bại với Field = AcknowledgeWarnings.
    /// Tổng giảm (khuyến mãi + giảm thêm) không quá MaxDiscountPercent của chính sách giá, trừ khi
    /// <paramref name="canExceedDiscountLimit"/> (quyền SYS09.DiscountOverLimit — ghi AuditLogs).
    /// </summary>
    Task<OperationResult<int>> CreateAsync(SaleFormViewModel form, string userId, bool canExceedDiscountLimit, CancellationToken ct = default);

    /// <summary>
    /// Form sửa hóa đơn Chờ thanh toán (nút "Sửa thuốc" ở bước thu tiền): điền sẵn dòng thuốc, khách, % giảm thêm, ghi chú.
    /// Chỉ người lập, khi ca của hóa đơn còn đang mở; không sửa được thì thất bại kèm lý do.
    /// </summary>
    Task<OperationResult<SaleFormViewModel>> EditFormAsync(int invoiceId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Lưu thay đổi hóa đơn Chờ thanh toán: kiểm tra lại như lập mới (tồn, thuốc kê đơn, cảnh báo, mức giảm), giữ mã hóa đơn,
    /// thay toàn bộ dòng thuốc; AuditLogs INVOICE_UPDATE. Hóa đơn vừa được thu tiền ở nơi khác → thất bại (RowVersion).
    /// </summary>
    Task<OperationResult> UpdateAsync(int invoiceId, SaleFormViewModel form, string userId, bool canExceedDiscountLimit, CancellationToken ct = default);

    /// <summary>Một lựa chọn khách hàng (nhãn, từ khóa tìm, dị ứng) — trả về sau khi thêm nhanh khách tại quầy; null nếu không có/đã ngừng.</summary>
    Task<SaleCustomerOption?> GetCustomerOptionAsync(int customerId, CancellationToken ct = default);

    /// <summary>Đơn thuốc gắn với hóa đơn (null: bán lẻ hoặc không có hóa đơn) — controller chọn mã quyền F3.1.4 / F3.2.4.</summary>
    Task<int?> GetPrescriptionIdAsync(int invoiceId, CancellationToken ct = default);

    /// <summary>Khách hàng đang hoạt động (nhãn, từ khóa tìm, dị ứng) — dùng chung cho màn hình tiếp nhận đơn thuốc.</summary>
    Task<IReadOnlyList<SaleCustomerOption>> GetCustomerOptionsAsync(CancellationToken ct = default);

    /// <summary><paramref name="viewAll"/> = true (F4.1.1): mọi hóa đơn; false: chỉ hóa đơn của người dùng.</summary>
    Task<InvoiceListViewModel> GetListAsync(InvoiceFilter filter, string userId, bool viewAll, CancellationToken ct = default);

    /// <summary>null nếu không có hoặc không được xem (không phải hóa đơn của mình và không có F4.1.1).</summary>
    Task<InvoiceDetailsViewModel?> GetDetailsAsync(int invoiceId, string userId, bool viewAll, CancellationToken ct = default);

    /// <summary>
    /// Hủy hóa đơn CHỜ THANH TOÁN (G-10), bắt buộc lý do, không xóa (SYS-06). Thao tác nhạy cảm: cần quyền
    /// SYS09.CancelInvoice (<paramref name="hasCancelPermission"/>; seed chỉ Owner, Owner cấp thêm ở 1.3.3), ghi AuditLogs.
    /// </summary>
    Task<OperationResult> CancelAsync(int invoiceId, string? reason, string userId, bool hasCancelPermission, CancellationToken ct = default);
}
