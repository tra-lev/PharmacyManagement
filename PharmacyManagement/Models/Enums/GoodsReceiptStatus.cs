// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái phiếu nhập (2.2.1.3 → 2.2.2). Cột: GoodsReceipts.Status (tinyint ↔ byte).</summary>
public enum GoodsReceiptStatus : byte
{
    /// <summary>Đang kiểm nhận — biên bản nhận hàng (2.2.1.3)</summary>
    Inspecting = 0,
    /// <summary>Đã nhập kho, đã cộng tồn (2.2.2–2.2.4)</summary>
    Posted = 1,
    /// <summary>Hủy</summary>
    Cancelled = 2
}
