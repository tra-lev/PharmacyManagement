using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Prescription
{
    public int PrescriptionId { get; set; }

    public string PrescriptionCode { get; set; } = null!;

    public int CustomerId { get; set; }

    public string DoctorName { get; set; } = null!;

    public string ClinicName { get; set; } = null!;

    public string? Diagnosis { get; set; }

    public DateOnly PrescriptionDate { get; set; }

    public string? ImagePath { get; set; }

    public int CheckedByEmployeeId { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public virtual Employee CheckedByEmployee { get; set; } = null!;

    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<PrescriptionDetail> PrescriptionDetails { get; set; } = new List<PrescriptionDetail>();

    public virtual ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();
}
