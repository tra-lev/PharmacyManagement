using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class PurchaseOrder
{
    public int PurchaseOrderId { get; set; }

    public string PurchaseOrderCode { get; set; } = null!;

    public int SupplierId { get; set; }

    public int CreatedByEmployeeId { get; set; }

    public DateTime OrderDate { get; set; }

    public DateOnly? ExpectedDeliveryDate { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public decimal TotalAmount { get; set; }

    public virtual Employee CreatedByEmployee { get; set; } = null!;

    public virtual ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();

    public virtual ICollection<PurchaseOrderDetail> PurchaseOrderDetails { get; set; } = new List<PurchaseOrderDetail>();

    public virtual Supplier Supplier { get; set; } = null!;
}
