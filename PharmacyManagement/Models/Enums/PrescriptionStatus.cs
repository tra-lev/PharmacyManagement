// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái đơn thuốc (3.2.3). Cột: Prescriptions.Status (tinyint ↔ byte).</summary>
public enum PrescriptionStatus : byte
{
    /// <summary>Đã lưu (hợp lệ)</summary>
    Saved = 0,
    /// <summary>Đã bán</summary>
    Dispensed = 1,
    /// <summary>Hủy</summary>
    Cancelled = 2
}
