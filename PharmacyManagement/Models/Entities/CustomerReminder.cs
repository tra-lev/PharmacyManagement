using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class CustomerReminder
{
    public long ReminderId { get; set; }

    public int CustomerId { get; set; }

    public int? MedicineId { get; set; }

    public DateTime ReminderDate { get; set; }

    public string Message { get; set; } = null!;

    public byte Channel { get; set; }

    public byte Status { get; set; }

    public DateTime? SentAt { get; set; }

    public int? CreatedByEmployeeId { get; set; }

    public int? DaysSupply { get; set; }

    public virtual Employee? CreatedByEmployee { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual Medicine? Medicine { get; set; }
}
