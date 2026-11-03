// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái nhập lại thuốc khách trả (3.4.4). Cột: ReturnDetails.RestockStatus (tinyint ↔ byte).</summary>
public enum RestockStatus : byte
{
    /// <summary>Chờ nhập</summary>
    Pending = 0,
    /// <summary>Đã nhập lại</summary>
    Restocked = 1,
    /// <summary>Không nhập</summary>
    Rejected = 2
}
