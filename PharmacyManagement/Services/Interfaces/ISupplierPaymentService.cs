using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Inventory;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Công nợ và tiền với nhà cung cấp (DEC-44): 2.2.5 thanh toán NCC (phiếu chi PC… theo từng phiếu nhập đã nhập kho),
/// 2.3.5 thu tiền hoàn (phiếu thu PT… theo phiếu trả đã xuất; thu đủ → phiếu trả Đã quyết toán). Hủy phiếu chi / thu bắt buộc lý do, không xóa.
/// Còn nợ phiếu nhập = TotalAmount − Σ phiếu chi còn hiệu lực.
/// </summary>
public interface ISupplierPaymentService
{
    Task<SupplierDebtListViewModel> GetDebtsAsync(SupplierDebtFilter filter, CancellationToken ct = default);

    Task<SupplierDebtViewModel?> GetSupplierAsync(int supplierId, CancellationToken ct = default);

    Task<SupplierPaymentListViewModel> GetPaymentsAsync(SupplierPaymentFilter filter, CancellationToken ct = default);

    Task<SupplierPaymentDetailsViewModel?> GetPaymentAsync(long paymentId, CancellationToken ct = default);

    /// <summary>Trang lập phiếu chi; null nếu không có phiếu nhập đã nhập kho này.</summary>
    Task<PayReceiptViewModel?> PayFormAsync(int receiptId, CancellationToken ct = default);

    Task<OperationResult<long>> PayAsync(int receiptId, SupplierMoneyForm form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult<long>> RefundAsync(int supplierReturnId, SupplierMoneyForm form, string actorUserId, CancellationToken ct = default);

    Task<OperationResult> VoidAsync(long paymentId, string? reason, string actorUserId, CancellationToken ct = default);

    /// <summary>Đã trả và còn nợ của một phiếu nhập.</summary>
    Task<(decimal Paid, decimal Remaining)> ReceiptBalanceAsync(int receiptId, CancellationToken ct = default);
}
