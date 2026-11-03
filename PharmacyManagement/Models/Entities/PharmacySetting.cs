using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class PharmacySetting
{
    public string SettingKey { get; set; } = null!;

    public string SettingValue { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int? UpdatedByEmployeeId { get; set; }

    public virtual Employee? UpdatedByEmployee { get; set; }
}
