using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class InventoryPolicy
{
    public int InventoryPolicyId { get; set; }

    public int MedicineId { get; set; }

    public int WarehouseId { get; set; }

    public decimal MinimumQuantity { get; set; }

    public int? ExpiryWarningDays { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual Warehouse Warehouse { get; set; } = null!;
}
