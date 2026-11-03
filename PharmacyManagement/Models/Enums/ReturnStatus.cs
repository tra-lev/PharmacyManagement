// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái phiếu đổi trả (3.4). Cột: Returns.Status (tinyint ↔ byte).</summary>
public enum ReturnStatus : byte
{
    /// <summary>Đã hoàn tiền — chờ nhập kho</summary>
    RefundedPendingRestock = 0,
    /// <summary>Hoàn tất</summary>
    Completed = 1,
    /// <summary>Hủy</summary>
    Cancelled = 2
}
