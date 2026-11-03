using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Payment
{
    public long PaymentId { get; set; }

    public int InvoiceId { get; set; }

    public byte PaymentMethod { get; set; }

    public decimal Amount { get; set; }

    public string? TransactionReference { get; set; }

    public DateTime PaidAt { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public decimal? TenderedAmount { get; set; }

    public virtual SalesInvoice Invoice { get; set; } = null!;
}
