using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class ActiveIngredient
{
    public int IngredientId { get; set; }

    public string IngredientName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<CustomerAllergy> CustomerAllergies { get; set; } = new List<CustomerAllergy>();

    public virtual ICollection<IngredientInteraction> IngredientInteractionIngredientId1Navigations { get; set; } = new List<IngredientInteraction>();

    public virtual ICollection<IngredientInteraction> IngredientInteractionIngredientId2Navigations { get; set; } = new List<IngredientInteraction>();

    public virtual ICollection<MedicineIngredient> MedicineIngredients { get; set; } = new List<MedicineIngredient>();
}
