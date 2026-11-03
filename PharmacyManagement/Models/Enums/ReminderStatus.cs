// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái nhắc lịch (3.5.2). Cột: CustomerReminders.Status (tinyint ↔ byte).</summary>
public enum ReminderStatus : byte
{
    /// <summary>Chờ gửi</summary>
    Pending = 0,
    /// <summary>Đã gửi</summary>
    Sent = 1,
    /// <summary>Lỗi</summary>
    Failed = 2,
    /// <summary>Hủy</summary>
    Cancelled = 3
}
