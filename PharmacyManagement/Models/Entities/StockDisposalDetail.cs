using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class StockDisposalDetail
{
    public int DisposalDetailId { get; set; }

    public int DisposalId { get; set; }

    public int BatchId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitCost { get; set; }

    public byte ReasonType { get; set; }

    public string? ReasonNote { get; set; }

    public virtual MedicineBatch Batch { get; set; } = null!;

    public virtual StockDisposal Disposal { get; set; } = null!;
}
