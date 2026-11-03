using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Warehouse
{
    public int WarehouseId { get; set; }

    public string WarehouseCode { get; set; } = null!;

    public string WarehouseName { get; set; } = null!;

    public string? Address { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<InventoryPolicy> InventoryPolicies { get; set; } = new List<InventoryPolicy>();

    public virtual ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    public virtual ICollection<MedicineBatch> MedicineBatches { get; set; } = new List<MedicineBatch>();

    public virtual ICollection<StockDisposal> StockDisposals { get; set; } = new List<StockDisposal>();

    public virtual ICollection<Stocktake> Stocktakes { get; set; } = new List<Stocktake>();

    public virtual ICollection<StorageEnvironmentLog> StorageEnvironmentLogs { get; set; } = new List<StorageEnvironmentLog>();

    public virtual ICollection<StorageLocation> StorageLocations { get; set; } = new List<StorageLocation>();
}
