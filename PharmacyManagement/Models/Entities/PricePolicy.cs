using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class PricePolicy
{
    public int PricePolicyId { get; set; }

    public string PolicyName { get; set; } = null!;

    public decimal MaxDiscountPercent { get; set; }

    public int ReturnWithinDays { get; set; }

    public bool IsActive { get; set; }

    public DateOnly? EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public string? ReturnConditions { get; set; }
}
