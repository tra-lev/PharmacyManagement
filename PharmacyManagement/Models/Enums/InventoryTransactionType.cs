// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Loại biến động kho (InventoryTransactions). Cột: InventoryTransactions.TransactionType (tinyint ↔ byte).</summary>
public enum InventoryTransactionType : byte
{
    /// <summary>Nhập mua — GoodsReceiptDetail, > 0</summary>
    PurchaseReceipt = 1,
    /// <summary>Xuất bán FEFO — SalesIssueAllocation, < 0</summary>
    SaleIssue = 2,
    /// <summary>Trả NCC — SupplierReturnDetail, < 0</summary>
    SupplierReturn = 3,
    /// <summary>Hủy thuốc — StockDisposalDetail, < 0</summary>
    Disposal = 4,
    /// <summary>Thu hồi — RecallBatch, < 0</summary>
    Recall = 5,
    /// <summary>Điều chỉnh kiểm kê — StocktakeDetail, ≠ 0</summary>
    StocktakeAdjustment = 6,
    /// <summary>Nhập lại hàng khách trả — ReturnDetail, > 0</summary>
    CustomerReturnRestock = 7
}
