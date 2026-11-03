using Microsoft.AspNetCore.Authorization;

namespace PharmacyManagement.Authorization;

/// <summary>Yêu cầu có quyền theo mã chức năng BFD, ví dụ "F1.3.1".</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string code) => Code = code;

    public string Code { get; }
}
