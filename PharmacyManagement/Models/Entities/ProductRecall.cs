using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class ProductRecall
{
    public int RecallId { get; set; }

    public string RecallCode { get; set; } = null!;

    public int MedicineId { get; set; }

    public string? NoticeNumber { get; set; }

    public DateOnly? NoticeDate { get; set; }

    public string Reason { get; set; } = null!;

    public byte Status { get; set; }

    public int CreatedByEmployeeId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Employee CreatedByEmployee { get; set; } = null!;

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual ICollection<RecallBatch> RecallBatches { get; set; } = new List<RecallBatch>();
}
