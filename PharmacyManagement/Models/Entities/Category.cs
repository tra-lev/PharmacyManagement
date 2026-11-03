using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Category
{
    public int CategoryId { get; set; }

    public string CategoryCode { get; set; } = null!;

    public string CategoryName { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Medicine> Medicines { get; set; } = new List<Medicine>();

    public virtual ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();

    public virtual ICollection<Stocktake> Stocktakes { get; set; } = new List<Stocktake>();
}
