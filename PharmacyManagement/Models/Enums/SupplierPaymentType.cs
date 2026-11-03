// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Loại phiếu chi/thu nhà cung cấp (2.2.5, 2.3.5). Cột: SupplierPayments.PaymentType (tinyint ↔ byte).</summary>
public enum SupplierPaymentType : byte
{
    /// <summary>Chi trả NCC — phiếu chi (2.2.5)</summary>
    PaymentToSupplier = 1,
    /// <summary>Nhận tiền hoàn từ NCC — phiếu thu (2.3.5)</summary>
    RefundFromSupplier = 2
}
