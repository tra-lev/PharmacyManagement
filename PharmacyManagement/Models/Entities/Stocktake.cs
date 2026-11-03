using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Stocktake
{
    public int StocktakeId { get; set; }

    public string StocktakeCode { get; set; } = null!;

    public int WarehouseId { get; set; }

    public int CreatedByEmployeeId { get; set; }

    public int? ApprovedByEmployeeId { get; set; }

    public DateTime StocktakeDate { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public int? LocationId { get; set; }

    public int? CategoryId { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string? ApprovalNote { get; set; }

    public int? AdjustedByEmployeeId { get; set; }

    public DateTime? AdjustedAt { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Employee? AdjustedByEmployee { get; set; }

    public virtual Employee? ApprovedByEmployee { get; set; }

    public virtual Category? Category { get; set; }

    public virtual Employee CreatedByEmployee { get; set; } = null!;

    public virtual StorageLocation? Location { get; set; }

    public virtual ICollection<StocktakeDetail> StocktakeDetails { get; set; } = new List<StocktakeDetail>();

    public virtual Warehouse Warehouse { get; set; } = null!;
}
