// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái biên bản hủy (2.3.2). Cột: StockDisposals.Status (tinyint ↔ byte).</summary>
public enum DisposalStatus : byte
{
    /// <summary>Nháp</summary>
    Draft = 0,
    /// <summary>Đã ghi sổ (đã trừ tồn)</summary>
    Posted = 1,
    /// <summary>Hủy</summary>
    Cancelled = 2
}
