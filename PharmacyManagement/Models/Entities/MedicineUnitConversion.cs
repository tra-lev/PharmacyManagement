using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class MedicineUnitConversion
{
    public int ConversionId { get; set; }

    public int MedicineId { get; set; }

    public int UnitId { get; set; }

    public decimal ConversionToBase { get; set; }

    public decimal? SellingPrice { get; set; }

    public bool IsDefaultSaleUnit { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual Unit Unit { get; set; } = null!;
}
