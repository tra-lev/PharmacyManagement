// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Lý do hủy thuốc (2.3.2). Cột: StockDisposalDetails.ReasonType (tinyint ↔ byte).</summary>
public enum DisposalReason : byte
{
    /// <summary>Hết hạn</summary>
    Expired = 1,
    /// <summary>Hỏng</summary>
    Damaged = 2,
    /// <summary>Móp méo</summary>
    Dented = 3,
    /// <summary>Khác</summary>
    Other = 4
}
