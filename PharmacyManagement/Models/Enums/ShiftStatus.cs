// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái ca làm việc (1.4). Cột: Shifts.Status (tinyint ↔ byte).</summary>
public enum ShiftStatus : byte
{
    /// <summary>Đang mở</summary>
    Open = 0,
    /// <summary>Đã bàn giao</summary>
    HandedOver = 1,
    /// <summary>Đã chốt</summary>
    Closed = 2
}
