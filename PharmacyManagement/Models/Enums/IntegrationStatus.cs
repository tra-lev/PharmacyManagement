// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái gửi liên thông mô phỏng (3.3.3, 3.5.2.2, 4.4). Cột: IntegrationLogs.Status (tinyint ↔ byte).</summary>
public enum IntegrationStatus : byte
{
    /// <summary>Chờ gửi</summary>
    Pending = 0,
    /// <summary>Thành công</summary>
    Succeeded = 1,
    /// <summary>Lỗi</summary>
    Failed = 2
}
