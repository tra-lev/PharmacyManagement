using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class MedicineBatch
{
    public int BatchId { get; set; }

    public int MedicineId { get; set; }

    public int? GoodsReceiptId { get; set; }

    public int? GoodsReceiptDetailId { get; set; }

    public int WarehouseId { get; set; }

    public int? LocationId { get; set; }

    public string BatchNumber { get; set; } = null!;

    public DateOnly? ManufactureDate { get; set; }

    public DateOnly ExpiryDate { get; set; }

    public DateOnly ReceivedDate { get; set; }

    public decimal UnitCost { get; set; }

    public decimal QuantityOnHand { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual GoodsReceipt? GoodsReceipt { get; set; }

    public virtual GoodsReceiptDetail? GoodsReceiptDetail { get; set; }

    public virtual ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    public virtual StorageLocation? Location { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual ICollection<RecallBatch> RecallBatches { get; set; } = new List<RecallBatch>();

    public virtual ICollection<ReturnDetail> ReturnDetails { get; set; } = new List<ReturnDetail>();

    public virtual ICollection<SalesIssueAllocation> SalesIssueAllocations { get; set; } = new List<SalesIssueAllocation>();

    public virtual ICollection<StockDisposalDetail> StockDisposalDetails { get; set; } = new List<StockDisposalDetail>();

    public virtual ICollection<StocktakeDetail> StocktakeDetails { get; set; } = new List<StocktakeDetail>();

    public virtual ICollection<SupplierReturnDetail> SupplierReturnDetails { get; set; } = new List<SupplierReturnDetail>();

    public virtual Warehouse Warehouse { get; set; } = null!;
}
