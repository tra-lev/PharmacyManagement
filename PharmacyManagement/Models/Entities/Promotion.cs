using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Promotion
{
    public int PromotionId { get; set; }

    public string PromotionCode { get; set; } = null!;

    public string PromotionName { get; set; } = null!;

    public int? MedicineId { get; set; }

    public int? CategoryId { get; set; }

    public decimal? DiscountPercent { get; set; }

    public decimal? DiscountAmount { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime EffectiveTo { get; set; }

    public bool IsActive { get; set; }

    public int CreatedByEmployeeId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Category? Category { get; set; }

    public virtual Employee CreatedByEmployee { get; set; } = null!;

    public virtual Medicine? Medicine { get; set; }

    public virtual ICollection<SalesInvoiceDetail> SalesInvoiceDetails { get; set; } = new List<SalesInvoiceDetail>();
}
