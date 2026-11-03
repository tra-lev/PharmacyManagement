namespace PharmacyManagement.Models.ViewModels;

public sealed class HomeIndexViewModel
{
    public string FullName { get; init; } = string.Empty;

    /// <summary>Tên tài khoản (phần trước @ của email đăng nhập) — dùng để chào, không dùng họ tên.</summary>
    public string UserName { get; init; } = string.Empty;
    public string? RoleName { get; init; }
    public bool IsOwner { get; init; }
    public bool EmailVerified { get; init; }

    /// <summary>Owner chưa có họ tên thật trong hồ sơ nhân viên.</summary>
    public bool NeedsFullName { get; init; }
}
