using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Medicine
{
    public int MedicineId { get; set; }

    public string MedicineCode { get; set; } = null!;

    public string? Barcode { get; set; }

    public string MedicineName { get; set; } = null!;

    public int CategoryId { get; set; }

    public int? ManufacturerId { get; set; }

    public int BaseUnitId { get; set; }

    public string? Strength { get; set; }

    public string? DosageForm { get; set; }

    public string? RegistrationNumber { get; set; }

    public string? CountryOfOrigin { get; set; }

    public decimal DefaultPurchasePrice { get; set; }

    public decimal DefaultSellingPrice { get; set; }

    public bool IsPrescription { get; set; }

    public bool IsControlledDrug { get; set; }

    public bool IsInterconnected { get; set; }

    public string? UsageInstructions { get; set; }

    public string? Contraindications { get; set; }

    public string? Interactions { get; set; }

    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal? MaxSingleDose { get; set; }

    public decimal? MaxDailyDose { get; set; }

    public byte? MinPatientAgeYears { get; set; }

    public virtual Unit BaseUnit { get; set; } = null!;

    public virtual Category Category { get; set; } = null!;

    public virtual ICollection<CustomerReminder> CustomerReminders { get; set; } = new List<CustomerReminder>();

    public virtual ICollection<GoodsReceiptDetail> GoodsReceiptDetails { get; set; } = new List<GoodsReceiptDetail>();

    public virtual ICollection<InventoryPolicy> InventoryPolicies { get; set; } = new List<InventoryPolicy>();

    public virtual ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    public virtual Manufacturer? Manufacturer { get; set; }

    public virtual ICollection<MedicineBatch> MedicineBatches { get; set; } = new List<MedicineBatch>();

    public virtual ICollection<MedicineContraindication> MedicineContraindications { get; set; } = new List<MedicineContraindication>();

    public virtual ICollection<MedicineIngredient> MedicineIngredients { get; set; } = new List<MedicineIngredient>();

    public virtual ICollection<MedicinePriceHistory> MedicinePriceHistories { get; set; } = new List<MedicinePriceHistory>();

    public virtual ICollection<MedicineUnitConversion> MedicineUnitConversions { get; set; } = new List<MedicineUnitConversion>();

    public virtual ICollection<PrescriptionDetail> PrescriptionDetails { get; set; } = new List<PrescriptionDetail>();

    public virtual ICollection<ProductRecall> ProductRecalls { get; set; } = new List<ProductRecall>();

    public virtual ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();

    public virtual ICollection<PurchaseOrderDetail> PurchaseOrderDetails { get; set; } = new List<PurchaseOrderDetail>();

    public virtual ICollection<SalesInvoiceDetail> SalesInvoiceDetails { get; set; } = new List<SalesInvoiceDetail>();
}
