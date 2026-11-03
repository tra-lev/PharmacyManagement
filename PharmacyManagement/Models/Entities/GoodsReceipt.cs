using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class GoodsReceipt
{
    public int GoodsReceiptId { get; set; }

    public string GoodsReceiptCode { get; set; } = null!;

    public int? PurchaseOrderId { get; set; }

    public int SupplierId { get; set; }

    public int ReceivedByEmployeeId { get; set; }

    public DateTime ReceiptDate { get; set; }

    public string? SupplierInvoiceNumber { get; set; }

    public byte Status { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public virtual ICollection<GoodsReceiptDetail> GoodsReceiptDetails { get; set; } = new List<GoodsReceiptDetail>();

    public virtual ICollection<MedicineBatch> MedicineBatches { get; set; } = new List<MedicineBatch>();

    public virtual PurchaseOrder? PurchaseOrder { get; set; }

    public virtual Employee ReceivedByEmployee { get; set; } = null!;

    public virtual Supplier Supplier { get; set; } = null!;

    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();

    public virtual ICollection<SupplierReturn> SupplierReturns { get; set; } = new List<SupplierReturn>();
}
