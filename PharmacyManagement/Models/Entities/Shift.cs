using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Shift
{
    public int ShiftId { get; set; }

    public string ShiftCode { get; set; } = null!;

    public int EmployeeId { get; set; }

    public DateTime OpenedAt { get; set; }

    public decimal OpeningCash { get; set; }

    public DateTime? ClosedAt { get; set; }

    public byte Status { get; set; }

    public string? HandoverNote { get; set; }

    public DateTime? HandedOverAt { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual ICollection<Return> Returns { get; set; } = new List<Return>();

    public virtual ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();

    public virtual ShiftClosing? ShiftClosing { get; set; }
}
