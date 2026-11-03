using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Permission
{
    public int PermissionId { get; set; }

    public string PermissionCode { get; set; } = null!;

    public string PermissionName { get; set; } = null!;

    public string ModuleName { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsSensitive { get; set; }

    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
