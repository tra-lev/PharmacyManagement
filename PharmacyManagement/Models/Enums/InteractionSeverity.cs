// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Mức độ tương tác thuốc (3.1.3.1). Cột: IngredientInteractions.Severity (tinyint ↔ byte).</summary>
public enum InteractionSeverity : byte
{
    /// <summary>Nhẹ</summary>
    Minor = 1,
    /// <summary>Trung bình</summary>
    Moderate = 2,
    /// <summary>Nghiêm trọng</summary>
    Severe = 3
}
