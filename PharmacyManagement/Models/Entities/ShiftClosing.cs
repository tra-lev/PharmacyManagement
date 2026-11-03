using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class ShiftClosing
{
    public int ShiftClosingId { get; set; }

    public int ShiftId { get; set; }

    public int ClosedByEmployeeId { get; set; }

    public decimal ExpectedCash { get; set; }

    public decimal ActualCash { get; set; }

    public decimal? CashDifference { get; set; }

    public decimal TotalNonCash { get; set; }

    public DateTime ClosedAt { get; set; }

    public string? Notes { get; set; }

    public decimal OpeningCash { get; set; }

    public decimal TotalCashSales { get; set; }

    public decimal TotalCashRefunds { get; set; }

    public virtual Employee ClosedByEmployee { get; set; } = null!;

    public virtual Shift Shift { get; set; } = null!;
}
