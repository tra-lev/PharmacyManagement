// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Kênh nhắc lịch (3.5.2.2). Cột: CustomerReminders.Channel (tinyint ↔ byte).</summary>
public enum ReminderChannel : byte
{
    /// <summary>SMS</summary>
    Sms = 0,
    /// <summary>Email</summary>
    Email = 1
}
