using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class SupplierPayment
{
    public long SupplierPaymentId { get; set; }

    public int SupplierId { get; set; }

    public int? GoodsReceiptId { get; set; }

    public int? SupplierReturnId { get; set; }

    public int EmployeeId { get; set; }

    public byte PaymentType { get; set; }

    public byte PaymentMethod { get; set; }

    public decimal Amount { get; set; }

    public string? TransactionReference { get; set; }

    public DateTime PaidAt { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public string SupplierPaymentCode { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;

    public virtual GoodsReceipt? GoodsReceipt { get; set; }

    public virtual Supplier Supplier { get; set; } = null!;

    public virtual SupplierReturn? SupplierReturn { get; set; }
}
