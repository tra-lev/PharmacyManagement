// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái lô thuốc (FEFO chỉ chọn Available). Cột: MedicineBatches.Status (tinyint ↔ byte).</summary>
public enum BatchStatus : byte
{
    /// <summary>Khả dụng</summary>
    Available = 1,
    /// <summary>Bị khóa</summary>
    Locked = 2,
    /// <summary>Đang thu hồi (khóa từ 2.3.3.1)</summary>
    Recalled = 3,
    /// <summary>Ngừng sử dụng</summary>
    Discontinued = 4
}
