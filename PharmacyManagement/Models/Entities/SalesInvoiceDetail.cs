using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class SalesInvoiceDetail
{
    public int InvoiceDetailId { get; set; }

    public int InvoiceId { get; set; }

    public int MedicineId { get; set; }

    public int UnitId { get; set; }

    public decimal Quantity { get; set; }

    public decimal ConversionToBase { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal LineTotal { get; set; }

    public int? PromotionId { get; set; }

    public virtual SalesInvoice Invoice { get; set; } = null!;

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual Promotion? Promotion { get; set; }

    public virtual ICollection<ReturnDetail> ReturnDetails { get; set; } = new List<ReturnDetail>();

    public virtual ICollection<SalesIssueAllocation> SalesIssueAllocations { get; set; } = new List<SalesIssueAllocation>();

    public virtual Unit Unit { get; set; } = null!;
}
