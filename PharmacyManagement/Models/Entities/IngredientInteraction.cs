using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class IngredientInteraction
{
    public int InteractionId { get; set; }

    public int IngredientId1 { get; set; }

    public int IngredientId2 { get; set; }

    public byte Severity { get; set; }

    public string Description { get; set; } = null!;

    public string? Recommendation { get; set; }

    public virtual ActiveIngredient IngredientId1Navigation { get; set; } = null!;

    public virtual ActiveIngredient IngredientId2Navigation { get; set; } = null!;
}
