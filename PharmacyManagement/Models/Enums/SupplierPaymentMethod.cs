// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Phương thức thanh toán với nhà cung cấp. Cột: SupplierPayments.PaymentMethod (tinyint ↔ byte).</summary>
public enum SupplierPaymentMethod : byte
{
    /// <summary>Tiền mặt</summary>
    Cash = 1,
    /// <summary>Chuyển khoản</summary>
    BankTransfer = 2
}
