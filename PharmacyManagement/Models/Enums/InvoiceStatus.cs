// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái hóa đơn (3.1.4/3.2.4 → 3.3). Cột: SalesInvoices.Status (tinyint ↔ byte).</summary>
public enum InvoiceStatus : byte
{
    /// <summary>Chờ thanh toán</summary>
    PendingPayment = 0,
    /// <summary>Đã thanh toán</summary>
    Paid = 1,
    /// <summary>Đã hủy</summary>
    Cancelled = 2
}
