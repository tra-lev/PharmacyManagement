// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Giới tính. Cột: Employees.Gender, Customers.Gender (tinyint ↔ byte).</summary>
public enum Gender : byte
{
    /// <summary>Nam</summary>
    Male = 1,
    /// <summary>Nữ</summary>
    Female = 2,
    /// <summary>Khác</summary>
    Other = 3
}
