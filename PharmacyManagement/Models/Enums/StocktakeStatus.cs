// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái kiểm kê (QT5: Manager lập → Owner duyệt → Manager điều chỉnh). Cột: Stocktakes.Status (tinyint ↔ byte).</summary>
public enum StocktakeStatus : byte
{
    /// <summary>Đang kiểm đếm</summary>
    Counting = 0,
    /// <summary>Chờ Owner duyệt</summary>
    PendingApproval = 1,
    /// <summary>Đã duyệt</summary>
    Approved = 2,
    /// <summary>Manager đã điều chỉnh tồn</summary>
    Adjusted = 3,
    /// <summary>Từ chối</summary>
    Rejected = 4
}
