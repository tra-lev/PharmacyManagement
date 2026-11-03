using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class Employee
{
    public int EmployeeId { get; set; }

    public string? UserId { get; set; }

    public string EmployeeCode { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public DateOnly? DateOfBirth { get; set; }

    public byte? Gender { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? JobTitle { get; set; }

    public DateOnly? HireDate { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<CustomerReminder> CustomerReminders { get; set; } = new List<CustomerReminder>();

    public virtual ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();

    public virtual ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    public virtual ICollection<MedicinePriceHistory> MedicinePriceHistories { get; set; } = new List<MedicinePriceHistory>();

    public virtual ICollection<PharmacySetting> PharmacySettings { get; set; } = new List<PharmacySetting>();

    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();

    public virtual ICollection<ProductRecall> ProductRecalls { get; set; } = new List<ProductRecall>();

    public virtual ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();

    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();

    public virtual ICollection<Return> Returns { get; set; } = new List<Return>();

    public virtual ICollection<SalesInvoice> SalesInvoiceCancelledByEmployees { get; set; } = new List<SalesInvoice>();

    public virtual ICollection<SalesInvoice> SalesInvoiceEmployees { get; set; } = new List<SalesInvoice>();

    public virtual Shift? Shift { get; set; }

    public virtual ICollection<ShiftClosing> ShiftClosings { get; set; } = new List<ShiftClosing>();

    public virtual ICollection<StockDisposal> StockDisposals { get; set; } = new List<StockDisposal>();

    public virtual ICollection<Stocktake> StocktakeAdjustedByEmployees { get; set; } = new List<Stocktake>();

    public virtual ICollection<Stocktake> StocktakeApprovedByEmployees { get; set; } = new List<Stocktake>();

    public virtual ICollection<Stocktake> StocktakeCreatedByEmployees { get; set; } = new List<Stocktake>();

    public virtual ICollection<StorageEnvironmentLog> StorageEnvironmentLogs { get; set; } = new List<StorageEnvironmentLog>();

    public virtual ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();

    public virtual ICollection<SupplierReturn> SupplierReturns { get; set; } = new List<SupplierReturn>();
}
