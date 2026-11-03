using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class StocktakeDetail
{
    public int StocktakeDetailId { get; set; }

    public int StocktakeId { get; set; }

    public int BatchId { get; set; }

    public decimal SystemQuantity { get; set; }

    public decimal ActualQuantity { get; set; }

    public decimal? Difference { get; set; }

    public string? Reason { get; set; }

    public virtual MedicineBatch Batch { get; set; } = null!;

    public virtual Stocktake Stocktake { get; set; } = null!;
}
