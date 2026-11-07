using System.Text.RegularExpressions;

namespace PharmacyManagement.Models.ViewModels.Admin;

/// <summary>Chữ hiển thị cho quyền (DEC-34: không ghi mã chức năng, mã SYS, mã quy trình).</summary>
public static partial class PermissionText
{
    /// <summary>"Xem doanh số cá nhân (4.1.1)" → "Xem doanh số cá nhân"; "Duyệt điều chỉnh tồn kho (QT5)" → "Duyệt điều chỉnh tồn kho".</summary>
    public static string Name(string name) => CodeInParens().Replace(name, string.Empty).Trim();

    /// <summary>"1. Quản trị hệ thống" → "Quản trị hệ thống"; "Bảo mật (SYS-09)" → "Bảo mật".</summary>
    public static string Module(string module) => Name(LeadingNumber().Replace(module, string.Empty));

    [GeneratedRegex(@"\s*\((?=[^)]*(?:\d|QT|SYS))[^)]*\)")]
    private static partial Regex CodeInParens();

    [GeneratedRegex(@"^\s*\d+\.\s*")]
    private static partial Regex LeadingNumber();
}

public sealed record RoleRow(string Name, string DisplayName, string Description, int Accounts, int Granted, int Total, bool Editable);

public sealed class RoleListViewModel
{
    public IReadOnlyList<RoleRow> Roles { get; init; } = Array.Empty<RoleRow>();
    public bool CanEdit { get; set; }
}

public sealed record PermissionItem(
    int PermissionId,
    string Name,
    bool IsAction,
    bool IsSensitive,
    bool Granted,
    string? LockedReason);

public sealed record PermissionGroup(string Key, string Title, IReadOnlyList<PermissionItem> Items);

public sealed class RolePermissionsViewModel
{
    public string Role { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Accounts { get; init; }
    public IReadOnlyList<PermissionGroup> Groups { get; init; } = Array.Empty<PermissionGroup>();

    /// <summary>Vai trò Owner luôn toàn quyền, không sửa được (tránh tự khóa mình).</summary>
    public bool Editable { get; init; }

    /// <summary>Người xem có quyền sửa (F1.3.3).</summary>
    public bool CanEdit { get; set; }

    public int GrantedCount => Groups.Sum(g => g.Items.Count(i => i.Granted));
    public int TotalCount => Groups.Sum(g => g.Items.Count);
}
