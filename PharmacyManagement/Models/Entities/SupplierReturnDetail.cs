using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class SupplierReturnDetail
{
    public int SupplierReturnDetailId { get; set; }

    public int SupplierReturnId { get; set; }

    public int BatchId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitCost { get; set; }

    public decimal? LineTotal { get; set; }

    public string? Reason { get; set; }

    public virtual MedicineBatch Batch { get; set; } = null!;

    public virtual SupplierReturn SupplierReturn { get; set; } = null!;
}
