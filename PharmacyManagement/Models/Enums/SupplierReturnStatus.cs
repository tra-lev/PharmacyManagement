// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái phiếu trả nhà cung cấp (2.3.1). Cột: SupplierReturns.Status (tinyint ↔ byte).</summary>
public enum SupplierReturnStatus : byte
{
    /// <summary>Nháp</summary>
    Draft = 0,
    /// <summary>Đã xuất trả (đã trừ tồn)</summary>
    Issued = 1,
    /// <summary>Đã quyết toán</summary>
    Settled = 2,
    /// <summary>Hủy</summary>
    Cancelled = 3
}
