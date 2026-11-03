// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái thanh toán. Cột: Payments.Status, SupplierPayments.Status (tinyint ↔ byte).</summary>
public enum PaymentStatus : byte
{
    /// <summary>Thành công</summary>
    Succeeded = 1,
    /// <summary>Đã hủy</summary>
    Voided = 2
}
