using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class InventoryTransaction
{
    public long InventoryTransactionId { get; set; }

    public int MedicineId { get; set; }

    public int BatchId { get; set; }

    public int WarehouseId { get; set; }

    public byte TransactionType { get; set; }

    public decimal QuantityChange { get; set; }

    public string ReferenceType { get; set; } = null!;

    public long ReferenceId { get; set; }

    public DateTime TransactionDate { get; set; }

    public int? CreatedByEmployeeId { get; set; }

    public string? Notes { get; set; }

    public virtual MedicineBatch Batch { get; set; } = null!;

    public virtual Employee? CreatedByEmployee { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual Warehouse Warehouse { get; set; } = null!;
}
