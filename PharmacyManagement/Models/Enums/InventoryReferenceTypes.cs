// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.
namespace PharmacyManagement.Models.Enums;

/// <summary>
/// Giá trị cột InventoryTransactions.ReferenceType. Cặp (TransactionType, ReferenceType, dấu QuantityChange)
/// bị CHECK CK_InventoryTransactions_TypeRef ràng buộc — luôn dùng <see cref="For"/> để lấy đúng cặp.
/// </summary>
public static class InventoryReferenceTypes
{
    public const string GoodsReceiptDetail = "GoodsReceiptDetail";
    public const string SalesIssueAllocation = "SalesIssueAllocation";
    public const string SupplierReturnDetail = "SupplierReturnDetail";
    public const string StockDisposalDetail = "StockDisposalDetail";
    public const string RecallBatch = "RecallBatch";
    public const string StocktakeDetail = "StocktakeDetail";
    public const string ReturnDetail = "ReturnDetail";

    /// <summary>ReferenceType bắt buộc cho từng loại biến động kho.</summary>
    public static string For(InventoryTransactionType type) => type switch
    {
        InventoryTransactionType.PurchaseReceipt => GoodsReceiptDetail,
        InventoryTransactionType.SaleIssue => SalesIssueAllocation,
        InventoryTransactionType.SupplierReturn => SupplierReturnDetail,
        InventoryTransactionType.Disposal => StockDisposalDetail,
        InventoryTransactionType.Recall => RecallBatch,
        InventoryTransactionType.StocktakeAdjustment => StocktakeDetail,
        InventoryTransactionType.CustomerReturnRestock => ReturnDetail,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Loại biến động kho không hợp lệ")
    };
}
