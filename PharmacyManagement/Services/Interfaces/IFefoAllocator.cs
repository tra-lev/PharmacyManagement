using PharmacyManagement.Helpers;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Xuất kho bán theo lô — BFD 2.3.4.1 chọn lô hết hạn trước xuất trước (FEFO), 2.3.4.2 trừ tồn theo lô và ghi sổ kho (Hệ thống).
/// Dùng chung cho bán lẻ (3.1.4) và bán theo đơn (3.2.4). PHẢI gọi bên trong transaction đang mở của người gọi:
/// thất bại thì người gọi không commit (toàn bộ được hoàn tác).
/// </summary>
public interface IFefoAllocator
{
    /// <summary>
    /// Cấp lô cho mọi dòng của hóa đơn: chỉ lô Khả dụng, hạn dùng sau hôm nay (SYS-10), hạn gần nhất trước;
    /// trừ tồn bằng UPDATE có điều kiện (chống bán trùng lô giữa nhiều quầy — DB-07), ghi SalesIssueAllocations
    /// và InventoryTransactions loại Xuất bán. Value = số lô đã xuất.
    /// </summary>
    Task<OperationResult<int>> AllocateInvoiceAsync(int invoiceId, string invoiceCode, int employeeId, CancellationToken ct = default);
}
