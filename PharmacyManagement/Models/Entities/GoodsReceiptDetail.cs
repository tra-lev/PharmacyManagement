using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class GoodsReceiptDetail
{
    public int GoodsReceiptDetailId { get; set; }

    public int GoodsReceiptId { get; set; }

    public int? PurchaseOrderDetailId { get; set; }

    public int MedicineId { get; set; }

    public int UnitId { get; set; }

    public decimal ConversionToBase { get; set; }

    public decimal QuantityReceived { get; set; }

    public decimal UnitCost { get; set; }

    public decimal DamagedQuantity { get; set; }

    public decimal? SubTotal { get; set; }

    public string? QualityNote { get; set; }

    public string? BatchNumber { get; set; }

    public DateOnly? ManufactureDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public int? LocationId { get; set; }

    public virtual GoodsReceipt GoodsReceipt { get; set; } = null!;

    public virtual StorageLocation? Location { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual MedicineBatch? MedicineBatch { get; set; }

    public virtual PurchaseOrderDetail? PurchaseOrderDetail { get; set; }

    public virtual Unit Unit { get; set; } = null!;
}
