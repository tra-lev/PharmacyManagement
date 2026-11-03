using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class PurchaseOrderDetail
{
    public int PurchaseOrderDetailId { get; set; }

    public int PurchaseOrderId { get; set; }

    public int MedicineId { get; set; }

    public decimal OrderedQuantity { get; set; }

    public int UnitId { get; set; }

    public decimal ExpectedUnitPrice { get; set; }

    public decimal ReceivedQuantity { get; set; }

    public virtual ICollection<GoodsReceiptDetail> GoodsReceiptDetails { get; set; } = new List<GoodsReceiptDetail>();

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual PurchaseOrder PurchaseOrder { get; set; } = null!;

    public virtual Unit Unit { get; set; } = null!;
}
