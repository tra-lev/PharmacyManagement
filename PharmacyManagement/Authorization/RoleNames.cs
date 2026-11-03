// Sinh từ PharmacyManagementDB.sql v2 + ENUMS.md, bổ sung quy tắc DEC-18 (Owner duy nhất). Đổi mã quyền phải sửa seed trong SQL trước.
namespace PharmacyManagement.Authorization;

/// <summary>3 vai trò cố định (1.3.2), Id trùng seed AspNetRoles của script v2. Mỗi tài khoản đúng 1 vai trò (1.3.4).</summary>
public static class RoleNames
{
    /// <summary>Chủ nhà thuốc — toàn quyền.</summary>
    public const string Owner = "Owner";
    /// <summary>Quản lý kiêm thủ kho — được bán hàng như Pharmacist.</summary>
    public const string Manager = "Manager";
    /// <summary>Dược sĩ bán hàng — chỉ xem tồn kho.</summary>
    public const string Pharmacist = "Pharmacist";

    public const string OwnerId = "7c1e5b0a-0001-4a6f-9b1e-000000000001";
    public const string ManagerId = "7c1e5b0a-0002-4a6f-9b1e-000000000002";
    public const string PharmacistId = "7c1e5b0a-0003-4a6f-9b1e-000000000003";

    public static readonly IReadOnlyList<string> All = new[] { Owner, Manager, Pharmacist };

    /// <summary>Vai trò Owner có thể mời qua giao diện: KHÔNG có Owner (DEC-18 — Owner duy nhất).</summary>
    public static readonly IReadOnlyList<string> Invitable = new[] { Manager, Pharmacist };

    /// <summary>Id cố định của vai trò theo tên; null nếu tên không hợp lệ.</summary>
    public static string? IdOf(string roleName) => roleName switch
    {
        Owner => OwnerId,
        Manager => ManagerId,
        Pharmacist => PharmacistId,
        _ => null
    };

    /// <summary>Tên hiển thị tiếng Việt.</summary>
    public static string DisplayName(string? roleName) => roleName switch
    {
        Owner => "Chủ nhà thuốc",
        Manager => "Quản lý",
        Pharmacist => "Dược sĩ",
        _ => "Chưa có vai trò"
    };
}
