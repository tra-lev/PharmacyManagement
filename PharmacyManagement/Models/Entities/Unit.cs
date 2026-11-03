using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Unit
{
    public int UnitId { get; set; }

    public string UnitCode { get; set; } = null!;

    public string UnitName { get; set; } = null!;

    public virtual ICollection<GoodsReceiptDetail> GoodsReceiptDetails { get; set; } = new List<GoodsReceiptDetail>();

    public virtual ICollection<MedicinePriceHistory> MedicinePriceHistories { get; set; } = new List<MedicinePriceHistory>();

    public virtual ICollection<MedicineUnitConversion> MedicineUnitConversions { get; set; } = new List<MedicineUnitConversion>();

    public virtual ICollection<Medicine> Medicines { get; set; } = new List<Medicine>();

    public virtual ICollection<PrescriptionDetail> PrescriptionDetails { get; set; } = new List<PrescriptionDetail>();

    public virtual ICollection<PurchaseOrderDetail> PurchaseOrderDetails { get; set; } = new List<PurchaseOrderDetail>();

    public virtual ICollection<SalesInvoiceDetail> SalesInvoiceDetails { get; set; } = new List<SalesInvoiceDetail>();
}
