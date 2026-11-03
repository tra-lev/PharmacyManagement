using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class StorageEnvironmentLog
{
    public long EnvironmentLogId { get; set; }

    public int WarehouseId { get; set; }

    public int? LocationId { get; set; }

    public int RecordedByEmployeeId { get; set; }

    public DateTime RecordedAt { get; set; }

    public decimal? TemperatureC { get; set; }

    public decimal? HumidityPercent { get; set; }

    public string? Notes { get; set; }

    public virtual StorageLocation? Location { get; set; }

    public virtual Employee RecordedByEmployee { get; set; } = null!;

    public virtual Warehouse Warehouse { get; set; } = null!;
}
