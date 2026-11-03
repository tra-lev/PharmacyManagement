using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class SalesInvoice
{
    public int InvoiceId { get; set; }

    public string InvoiceCode { get; set; } = null!;

    public int? CustomerId { get; set; }

    public int EmployeeId { get; set; }

    public int ShiftId { get; set; }

    public int? PrescriptionId { get; set; }

    public DateTime InvoiceDate { get; set; }

    public decimal SubTotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public string? EinvoiceNumber { get; set; }

    public byte EinvoiceStatus { get; set; }

    public DateTime? EinvoiceIssuedAt { get; set; }

    public string? BuyerName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public string? BuyerAddress { get; set; }

    public int? CancelledByEmployeeId { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancelReason { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Employee? CancelledByEmployee { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Prescription? Prescription { get; set; }

    public virtual ICollection<Return> Returns { get; set; } = new List<Return>();

    public virtual ICollection<SalesInvoiceDetail> SalesInvoiceDetails { get; set; } = new List<SalesInvoiceDetail>();

    public virtual Shift Shift { get; set; } = null!;
}
