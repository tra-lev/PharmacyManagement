using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class RolePermission
{
    public string RoleId { get; set; } = null!;

    public int PermissionId { get; set; }

    public bool CanView { get; set; }

    public bool CanCreate { get; set; }

    public bool CanUpdate { get; set; }

    public bool CanDelete { get; set; }

    public virtual Permission Permission { get; set; } = null!;
}
