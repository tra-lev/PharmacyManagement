// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái hóa đơn điện tử mô phỏng (3.3.3). Cột: SalesInvoices.EInvoiceStatus (tinyint ↔ byte).</summary>
public enum EInvoiceStatus : byte
{
    /// <summary>Chưa xuất</summary>
    NotIssued = 0,
    /// <summary>Đã xuất</summary>
    Issued = 1,
    /// <summary>Lỗi</summary>
    Failed = 2
}
