using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Return
{
    public int ReturnId { get; set; }

    public string ReturnCode { get; set; } = null!;

    public int InvoiceId { get; set; }

    public int ProcessedByEmployeeId { get; set; }

    public DateTime ReturnDate { get; set; }

    public string Reason { get; set; } = null!;

    public decimal RefundAmount { get; set; }

    public byte Status { get; set; }

    public int? ShiftId { get; set; }

    public byte RefundMethod { get; set; }

    public string? RefundReference { get; set; }

    public virtual SalesInvoice Invoice { get; set; } = null!;

    public virtual Employee ProcessedByEmployee { get; set; } = null!;

    public virtual ICollection<ReturnDetail> ReturnDetails { get; set; } = new List<ReturnDetail>();

    public virtual Shift? Shift { get; set; }
}
