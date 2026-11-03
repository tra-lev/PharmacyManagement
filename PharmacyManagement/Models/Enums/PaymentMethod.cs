// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Phương thức thanh toán / hoàn tiền (3.3.2, 3.4.3). Cột: Payments.PaymentMethod, Returns.RefundMethod (tinyint ↔ byte).</summary>
public enum PaymentMethod : byte
{
    /// <summary>Tiền mặt</summary>
    Cash = 1,
    /// <summary>Chuyển khoản/QR</summary>
    BankTransfer = 2,
    /// <summary>Thẻ</summary>
    Card = 3
}
