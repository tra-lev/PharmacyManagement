using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class MedicineContraindication
{
    public int MedicineContraindicationId { get; set; }

    public int MedicineId { get; set; }

    public string ConditionKeyword { get; set; } = null!;

    public string? Note { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;
}
