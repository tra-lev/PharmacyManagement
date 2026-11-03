using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class ReturnDetail
{
    public int ReturnDetailId { get; set; }

    public int ReturnId { get; set; }

    public int InvoiceDetailId { get; set; }

    public int? BatchId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitRefundPrice { get; set; }

    public byte RestockStatus { get; set; }

    public string? ConditionNote { get; set; }

    public virtual MedicineBatch? Batch { get; set; }

    public virtual SalesInvoiceDetail InvoiceDetail { get; set; } = null!;

    public virtual Return Return { get; set; } = null!;
}
