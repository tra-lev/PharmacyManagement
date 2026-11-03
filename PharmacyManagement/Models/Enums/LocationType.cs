// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Loại vị trí lưu trữ (cây Tủ → Kệ, 1.5.1). Cột: StorageLocations.LocationType (tinyint ↔ byte).</summary>
public enum LocationType : byte
{
    /// <summary>Tủ</summary>
    Cabinet = 1,
    /// <summary>Kệ</summary>
    Shelf = 2
}
