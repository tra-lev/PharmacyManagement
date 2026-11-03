using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class RecallBatch
{
    public int RecallBatchId { get; set; }

    public int RecallId { get; set; }

    public int BatchId { get; set; }

    public decimal QuantityOnHandAtRecall { get; set; }

    public decimal QuantityRecovered { get; set; }

    public string? Notes { get; set; }

    public virtual MedicineBatch Batch { get; set; } = null!;

    public virtual ProductRecall Recall { get; set; } = null!;
}
