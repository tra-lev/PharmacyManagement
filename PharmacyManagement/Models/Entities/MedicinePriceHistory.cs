using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class MedicinePriceHistory
{
    public long PriceHistoryId { get; set; }

    public int MedicineId { get; set; }

    public int UnitId { get; set; }

    public decimal? PurchasePrice { get; set; }

    public decimal SellingPrice { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public int? ChangedByEmployeeId { get; set; }

    public decimal? OldSellingPrice { get; set; }

    public string? ChangeReason { get; set; }

    public virtual Employee? ChangedByEmployee { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual Unit Unit { get; set; } = null!;
}
