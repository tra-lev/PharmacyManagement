using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class CustomerAllergy
{
    public int CustomerAllergyId { get; set; }

    public int CustomerId { get; set; }

    public int IngredientId { get; set; }

    public string? ReactionDescription { get; set; }

    public DateTime RecordedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual ActiveIngredient Ingredient { get; set; } = null!;
}
