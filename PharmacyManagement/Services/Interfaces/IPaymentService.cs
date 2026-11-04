using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Sales;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Thanh toán hóa đơn — BFD 3.3.2.1 thu tiền mặt, 3.3.2.2 thu chuyển khoản/thẻ, 3.3.2.3 thanh toán kết hợp.
/// Thu một lần cho đủ tiền; trong MỘT transaction (SYS-14): hóa đơn → Đã thanh toán, ghi Payments, xuất kho FEFO
/// (<see cref="IFefoAllocator"/>, 2.3.4). Thiếu tồn hoặc lỗi bất kỳ → hoàn tác toàn bộ (SYS-10).
/// </summary>
public interface IPaymentService
{
    /// <summary>Chỉ người lập hóa đơn, trong ca đang mở của họ, với quyền thu tiền tương ứng <paramref name="permissions"/>.</summary>
    Task<OperationResult> PayAsync(int invoiceId, PaymentForm form, string userId, PaymentPermissions permissions, CancellationToken ct = default);
}
