using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class PrescriptionDetail
{
    public int PrescriptionDetailId { get; set; }

    public int PrescriptionId { get; set; }

    public int MedicineId { get; set; }

    public decimal PrescribedQuantity { get; set; }

    public int UnitId { get; set; }

    public string? Dosage { get; set; }

    public string? Frequency { get; set; }

    public int? DurationDays { get; set; }

    public string? Instructions { get; set; }

    public decimal? DosePerTime { get; set; }

    public byte? TimesPerDay { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual Prescription Prescription { get; set; } = null!;

    public virtual Unit Unit { get; set; } = null!;
}
