using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class StockDisposal
{
    public int DisposalId { get; set; }

    public string DisposalCode { get; set; } = null!;

    public int WarehouseId { get; set; }

    public int CreatedByEmployeeId { get; set; }

    public DateTime DisposalDate { get; set; }

    public string Reason { get; set; } = null!;

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public virtual Employee CreatedByEmployee { get; set; } = null!;

    public virtual ICollection<StockDisposalDetail> StockDisposalDetails { get; set; } = new List<StockDisposalDetail>();

    public virtual Warehouse Warehouse { get; set; } = null!;
}
