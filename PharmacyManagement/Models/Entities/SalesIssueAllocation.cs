using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class SalesIssueAllocation
{
    public long AllocationId { get; set; }

    public int InvoiceDetailId { get; set; }

    public int BatchId { get; set; }

    public decimal QuantityBase { get; set; }

    public decimal UnitCostAtSale { get; set; }

    public DateTime AllocatedAt { get; set; }

    public virtual MedicineBatch Batch { get; set; } = null!;

    public virtual SalesInvoiceDetail InvoiceDetail { get; set; } = null!;
}
