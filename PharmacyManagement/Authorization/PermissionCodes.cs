// Sinh từ PharmacyManagementDB.sql v2 + ENUMS.md, bổ sung quy tắc DEC-18 (Owner duy nhất). Đổi mã quyền phải sửa seed trong SQL trước.

namespace PharmacyManagement.Authorization;

/// <summary>
/// Mã quyền = mã chức năng lá BFD (Chương 2, Bảng 2.3) + quyền bổ sung. Trùng khớp bảng Permissions (seed script v2).
/// Dùng làm tên policy: [Authorize(Policy = PermissionCodes.F3_1_4)].
/// </summary>
public static class PermissionCodes
{
    /// <summary>Thêm thuốc mới — 1. Quản trị hệ thống</summary>
    public const string F1_1_1 = "F1.1.1";
    /// <summary>Sửa thông tin thuốc — 1. Quản trị hệ thống</summary>
    public const string F1_1_2 = "F1.1.2";
    /// <summary>Phân loại thuốc — 1. Quản trị hệ thống</summary>
    public const string F1_1_3 = "F1.1.3";
    /// <summary>Khai báo quy đổi Hộp – Vỉ – Viên — 1. Quản trị hệ thống</summary>
    public const string F1_1_4 = "F1.1.4";
    /// <summary>Cập nhật nhà cung cấp — 1. Quản trị hệ thống</summary>
    public const string F1_2_1 = "F1.2.1";
    /// <summary>Cập nhật khách hàng — 1. Quản trị hệ thống</summary>
    public const string F1_2_2 = "F1.2.2";
    /// <summary>Tạo tài khoản người dùng — 1. Quản trị hệ thống; nhạy cảm</summary>
    public const string F1_3_1 = "F1.3.1";
    /// <summary>Tạo vai trò (danh sách vai trò) — 1. Quản trị hệ thống; nhạy cảm; chỉ xem</summary>
    public const string F1_3_2 = "F1.3.2";
    /// <summary>Phân quyền cho vai trò — 1. Quản trị hệ thống; nhạy cảm</summary>
    public const string F1_3_3 = "F1.3.3";
    /// <summary>Gán vai trò cho người dùng — 1. Quản trị hệ thống; nhạy cảm</summary>
    public const string F1_3_4 = "F1.3.4";
    /// <summary>Khóa tài khoản — 1. Quản trị hệ thống; nhạy cảm</summary>
    public const string F1_3_5 = "F1.3.5";
    /// <summary>Mở ca — 1. Quản trị hệ thống</summary>
    public const string F1_4_1 = "F1.4.1";
    /// <summary>Bàn giao ca — 1. Quản trị hệ thống</summary>
    public const string F1_4_2 = "F1.4.2";
    /// <summary>Chốt tiền cuối ca — 1. Quản trị hệ thống</summary>
    public const string F1_4_3 = "F1.4.3";
    /// <summary>Khai báo tủ, kệ lưu trữ — 1. Quản trị hệ thống</summary>
    public const string F1_5_1 = "F1.5.1";
    /// <summary>Thiết lập định mức tồn — 1. Quản trị hệ thống</summary>
    public const string F1_5_2 = "F1.5.2";
    /// <summary>Thiết lập ngưỡng cận hạn — 1. Quản trị hệ thống</summary>
    public const string F1_5_3 = "F1.5.3";
    /// <summary>Thiết lập chính sách giá — 1. Quản trị hệ thống; nhạy cảm</summary>
    public const string F1_5_4 = "F1.5.4";
    /// <summary>Lập đơn đặt hàng — 2. Quản lý kho hàng</summary>
    public const string F2_1_1 = "F2.1.1";
    /// <summary>Gửi đơn đặt hàng — 2. Quản lý kho hàng</summary>
    public const string F2_1_2 = "F2.1.2";
    /// <summary>Theo dõi đơn đặt hàng — 2. Quản lý kho hàng</summary>
    public const string F2_1_3 = "F2.1.3";
    /// <summary>Đối chiếu đơn đặt hàng — 2. Quản lý kho hàng; chỉ xem</summary>
    public const string F2_2_1_1 = "F2.2.1.1";
    /// <summary>Kiểm tra hạn dùng, chất lượng — 2. Quản lý kho hàng</summary>
    public const string F2_2_1_2 = "F2.2.1.2";
    /// <summary>Ghi nhận hàng thiếu, hỏng — 2. Quản lý kho hàng</summary>
    public const string F2_2_1_3 = "F2.2.1.3";
    /// <summary>Lập phiếu nhập kho — 2. Quản lý kho hàng</summary>
    public const string F2_2_2 = "F2.2.2";
    /// <summary>Ghi nhận số lô, hạn dùng — 2. Quản lý kho hàng</summary>
    public const string F2_2_3 = "F2.2.3";
    /// <summary>Xếp thuốc vào vị trí — 2. Quản lý kho hàng</summary>
    public const string F2_2_4 = "F2.2.4";
    /// <summary>Thanh toán nhà cung cấp — 2. Quản lý kho hàng</summary>
    public const string F2_2_5 = "F2.2.5";
    /// <summary>Lập phiếu trả nhà cung cấp — 2. Quản lý kho hàng</summary>
    public const string F2_3_1 = "F2.3.1";
    /// <summary>Lập biên bản hủy thuốc — 2. Quản lý kho hàng</summary>
    public const string F2_3_2 = "F2.3.2";
    /// <summary>Tiếp nhận thông báo thu hồi — 2. Quản lý kho hàng</summary>
    public const string F2_3_3_1 = "F2.3.3.1";
    /// <summary>Tra cứu lô đang tồn/đã bán — 2. Quản lý kho hàng; chỉ xem</summary>
    public const string F2_3_3_2 = "F2.3.3.2";
    /// <summary>Lập phiếu thu hồi — 2. Quản lý kho hàng</summary>
    public const string F2_3_3_3 = "F2.3.3.3";
    /// <summary>Thu tiền hoàn từ nhà cung cấp — 2. Quản lý kho hàng</summary>
    public const string F2_3_5 = "F2.3.5";
    /// <summary>In phiếu kiểm kê — 2. Quản lý kho hàng</summary>
    public const string F2_4_1_1 = "F2.4.1.1";
    /// <summary>Nhập số lượng thực tế — 2. Quản lý kho hàng</summary>
    public const string F2_4_1_2 = "F2.4.1.2";
    /// <summary>Lập biên bản kiểm kê — 2. Quản lý kho hàng</summary>
    public const string F2_4_2 = "F2.4.2";
    /// <summary>Điều chỉnh tồn kho — 2. Quản lý kho hàng</summary>
    public const string F2_4_3 = "F2.4.3";
    /// <summary>Ghi sổ nhiệt độ, độ ẩm — 2. Quản lý kho hàng</summary>
    public const string F2_5_3 = "F2.5.3";
    /// <summary>Tra cứu thuốc — 3. Quản lý bán hàng; chỉ xem</summary>
    public const string F3_1_1 = "F3.1.1";
    /// <summary>Lập hóa đơn bán lẻ — 3. Quản lý bán hàng</summary>
    public const string F3_1_4 = "F3.1.4";
    /// <summary>Tiếp nhận đơn thuốc — 3. Quản lý bán hàng</summary>
    public const string F3_2_1 = "F3.2.1";
    /// <summary>Kiểm tra tính hợp lệ của đơn — 3. Quản lý bán hàng</summary>
    public const string F3_2_2_1 = "F3.2.2.1";
    /// <summary>Kiểm tra liều dùng — 3. Quản lý bán hàng</summary>
    public const string F3_2_2_2 = "F3.2.2.2";
    /// <summary>Lưu thông tin đơn thuốc — 3. Quản lý bán hàng</summary>
    public const string F3_2_3 = "F3.2.3";
    /// <summary>Lập hóa đơn bán theo đơn — 3. Quản lý bán hàng</summary>
    public const string F3_2_4 = "F3.2.4";
    /// <summary>Thu tiền mặt — 3. Quản lý bán hàng</summary>
    public const string F3_3_2_1 = "F3.3.2.1";
    /// <summary>Thu chuyển khoản, thẻ — 3. Quản lý bán hàng</summary>
    public const string F3_3_2_2 = "F3.3.2.2";
    /// <summary>Ghi nhận thanh toán kết hợp — 3. Quản lý bán hàng</summary>
    public const string F3_3_2_3 = "F3.3.2.3";
    /// <summary>Tiếp nhận yêu cầu đổi trả — 3. Quản lý bán hàng; chỉ xem</summary>
    public const string F3_4_1 = "F3.4.1";
    /// <summary>Kiểm tra điều kiện đổi trả — 3. Quản lý bán hàng</summary>
    public const string F3_4_2 = "F3.4.2";
    /// <summary>Hoàn tiền cho khách — 3. Quản lý bán hàng</summary>
    public const string F3_4_3 = "F3.4.3";
    /// <summary>Nhập lại thuốc trả — 3. Quản lý bán hàng</summary>
    public const string F3_4_4 = "F3.4.4";
    /// <summary>Lập lịch nhắc — 3. Quản lý bán hàng</summary>
    public const string F3_5_2_1 = "F3.5.2.1";
    /// <summary>Lập báo cáo doanh thu — 4. Lập báo cáo; chỉ xem</summary>
    public const string F4_1_1 = "F4.1.1";
    /// <summary>Lập báo cáo lợi nhuận — 4. Lập báo cáo; chỉ xem</summary>
    public const string F4_1_2 = "F4.1.2";
    /// <summary>Lập báo cáo nhập – xuất – tồn — 4. Lập báo cáo; chỉ xem</summary>
    public const string F4_2_1 = "F4.2.1";
    /// <summary>Lập báo cáo thuốc cận hạn — 4. Lập báo cáo; chỉ xem</summary>
    public const string F4_2_2 = "F4.2.2";
    /// <summary>Kết xuất sổ theo dõi xuất nhập thuốc — 4. Lập báo cáo; chỉ xem</summary>
    public const string F4_3_1 = "F4.3.1";
    /// <summary>Kết xuất sổ thuốc kiểm soát đặc biệt — 4. Lập báo cáo; chỉ xem</summary>
    public const string F4_3_2 = "F4.3.2";
    /// <summary>Lập sổ theo dõi — 4. Lập báo cáo; chỉ xem</summary>
    public const string F4_3_3 = "F4.3.3";
    /// <summary>Xử lý lỗi gửi dữ liệu — 4. Lập báo cáo</summary>
    public const string F4_4_2_3 = "F4.4.2.3";
    /// <summary>Xem doanh số cá nhân (4.1.1) — 4. Lập báo cáo; chỉ xem</summary>
    public const string F4_1_1_Own = "F4.1.1-OWN";
    /// <summary>Duyệt điều chỉnh tồn kho (QT5) — 2. Quản lý kho hàng; nhạy cảm</summary>
    public const string F2_4_3_Approve = "F2.4.3-APPROVE";
    /// <summary>Giảm giá vượt mức tối đa — Bảo mật (SYS-09); nhạy cảm</summary>
    public const string Sys09DiscountOverLimit = "SYS09.DiscountOverLimit";
    /// <summary>Hủy hóa đơn chờ thanh toán — Bảo mật (SYS-09); nhạy cảm</summary>
    public const string Sys09CancelInvoice = "SYS09.CancelInvoice";
    /// <summary>Xem nhật ký hoạt động — Bảo mật (SYS-09); nhạy cảm; chỉ xem</summary>
    public const string Sys09ViewAuditLog = "SYS09.ViewAuditLog";

    /// <summary>Toàn bộ 67 mã quyền theo thứ tự seed.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        F1_1_1,
        F1_1_2,
        F1_1_3,
        F1_1_4,
        F1_2_1,
        F1_2_2,
        F1_3_1,
        F1_3_2,
        F1_3_3,
        F1_3_4,
        F1_3_5,
        F1_4_1,
        F1_4_2,
        F1_4_3,
        F1_5_1,
        F1_5_2,
        F1_5_3,
        F1_5_4,
        F2_1_1,
        F2_1_2,
        F2_1_3,
        F2_2_1_1,
        F2_2_1_2,
        F2_2_1_3,
        F2_2_2,
        F2_2_3,
        F2_2_4,
        F2_2_5,
        F2_3_1,
        F2_3_2,
        F2_3_3_1,
        F2_3_3_2,
        F2_3_3_3,
        F2_3_5,
        F2_4_1_1,
        F2_4_1_2,
        F2_4_2,
        F2_4_3,
        F2_5_3,
        F3_1_1,
        F3_1_4,
        F3_2_1,
        F3_2_2_1,
        F3_2_2_2,
        F3_2_3,
        F3_2_4,
        F3_3_2_1,
        F3_3_2_2,
        F3_3_2_3,
        F3_4_1,
        F3_4_2,
        F3_4_3,
        F3_4_4,
        F3_5_2_1,
        F4_1_1,
        F4_1_2,
        F4_2_1,
        F4_2_2,
        F4_3_1,
        F4_3_2,
        F4_3_3,
        F4_4_2_3,
        F4_1_1_Own,
        F2_4_3_Approve,
        Sys09DiscountOverLimit,
        Sys09CancelInvoice,
        Sys09ViewAuditLog
    };

    /// <summary>
    /// SYS-13 / Q-15: quyền làm thay đổi tồn kho (nhóm 2 và 3.4.4 nhập lại thuốc trả).
    /// Không bao giờ được cấp cho Pharmacist, kể cả khi Owner chỉnh quyền ở 1.3.3 — kiểm tra phía server.
    /// </summary>
    public static bool IsLockedForPharmacist(string permissionCode) =>
        permissionCode.StartsWith("F2.", StringComparison.Ordinal) || permissionCode == F3_4_4;

    /// <summary>
    /// DEC-18: quản trị tài khoản (1.3.1–1.3.5) chỉ dành cho Owner duy nhất — không cấp được cho vai trò khác
    /// dù Owner chỉnh quyền ở 1.3.3; kiểm tra phía server.
    /// </summary>
    public static readonly IReadOnlySet<string> OwnerOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        F1_3_1, F1_3_2, F1_3_3, F1_3_4, F1_3_5
    };

    /// <summary>DEC-18: chức năng quản trị quan trọng — người dùng phải xác minh email trước khi dùng.</summary>
    public static readonly IReadOnlySet<string> RequireVerifiedEmail = new HashSet<string>(StringComparer.Ordinal)
    {
        F1_3_1, F1_3_2, F1_3_3, F1_3_4, F1_3_5, F1_5_4, F2_4_3_Approve,
        Sys09DiscountOverLimit, Sys09CancelInvoice, Sys09ViewAuditLog
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>Mã quyền có trong danh sách 67 mã (dùng làm tên policy).</summary>
    public static bool IsKnown(string permissionCode) => Known.Contains(permissionCode);

    public static bool IsOwnerOnly(string permissionCode) => OwnerOnly.Contains(permissionCode);

    public static bool RequiresVerifiedEmail(string permissionCode) => RequireVerifiedEmail.Contains(permissionCode);
}
