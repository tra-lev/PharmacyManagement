using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class StorageLocation
{
    public int LocationId { get; set; }

    public int WarehouseId { get; set; }

    public string LocationCode { get; set; } = null!;

    public string LocationName { get; set; } = null!;

    public string? Description { get; set; }

    public byte LocationType { get; set; }

    public int? ParentLocationId { get; set; }

    public virtual ICollection<GoodsReceiptDetail> GoodsReceiptDetails { get; set; } = new List<GoodsReceiptDetail>();

    public virtual ICollection<StorageLocation> InverseParentLocation { get; set; } = new List<StorageLocation>();

    public virtual ICollection<MedicineBatch> MedicineBatches { get; set; } = new List<MedicineBatch>();

    public virtual StorageLocation? ParentLocation { get; set; }

    public virtual ICollection<Stocktake> Stocktakes { get; set; } = new List<Stocktake>();

    public virtual ICollection<StorageEnvironmentLog> StorageEnvironmentLogs { get; set; } = new List<StorageEnvironmentLog>();

    public virtual Warehouse Warehouse { get; set; } = null!;
}
