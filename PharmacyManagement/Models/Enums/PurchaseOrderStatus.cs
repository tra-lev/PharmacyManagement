// Sinh tự động từ PharmacyManagementDB.sql v2 + ENUMS.md — không sửa tay; đổi giá trị phải sửa CHECK trong SQL trước.

namespace PharmacyManagement.Models.Enums;

/// <summary>Trạng thái đơn đặt hàng (2.1.3). Cột: PurchaseOrders.Status (tinyint ↔ byte).</summary>
public enum PurchaseOrderStatus : byte
{
    /// <summary>Nháp</summary>
    Draft = 0,
    /// <summary>Chờ giao (đã gửi NCC)</summary>
    AwaitingDelivery = 1,
    /// <summary>Giao thiếu</summary>
    PartiallyDelivered = 2,
    /// <summary>Đã giao</summary>
    Delivered = 3,
    /// <summary>Hủy</summary>
    Cancelled = 4
}
