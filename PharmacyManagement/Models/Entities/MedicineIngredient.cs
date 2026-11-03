using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class MedicineIngredient
{
    public int MedicineId { get; set; }

    public int IngredientId { get; set; }

    public string? IngredientStrength { get; set; }

    public virtual ActiveIngredient Ingredient { get; set; } = null!;

    public virtual Medicine Medicine { get; set; } = null!;
}
