// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái thu hồi (2.3.3). Cột: ProductRecalls.Status (tinyint ↔ byte).</summary>
public enum RecallStatus : byte
{
    /// <summary>Mới tiếp nhận</summary>
    Received = 0,
    /// <summary>Đang thu hồi</summary>
    InProgress = 1,
    /// <summary>Hoàn tất</summary>
    Completed = 2
}
