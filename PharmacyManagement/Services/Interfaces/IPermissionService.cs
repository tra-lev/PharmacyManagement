namespace PharmacyManagement.Services.Interfaces;

/// <summary>Đọc quyền của vai trò từ bảng RolePermissions (có bộ nhớ đệm 5 phút).</summary>
public interface IPermissionService
{
    /// <summary>Các mã quyền vai trò được dùng, đã áp khóa cứng SYS-13 và DEC-18.</summary>
    Task<IReadOnlySet<string>> GetCodesAsync(string roleName, CancellationToken ct = default);

    Task<bool> RoleHasAsync(string roleName, string code, CancellationToken ct = default);

    /// <summary>Xóa bộ nhớ đệm sau khi Owner sửa quyền (1.3.3).</summary>
    void Invalidate(string roleName);
}
