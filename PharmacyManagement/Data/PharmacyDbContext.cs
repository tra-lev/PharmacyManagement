using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Models.Entities;

namespace PharmacyManagement.Data;

public partial class PharmacyDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>
{
    public PharmacyDbContext(DbContextOptions<PharmacyDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ActiveIngredient> ActiveIngredients { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerAllergy> CustomerAllergies { get; set; }

    public virtual DbSet<CustomerReminder> CustomerReminders { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<GoodsReceipt> GoodsReceipts { get; set; }

    public virtual DbSet<GoodsReceiptDetail> GoodsReceiptDetails { get; set; }

    public virtual DbSet<IngredientInteraction> IngredientInteractions { get; set; }

    public virtual DbSet<IntegrationLog> IntegrationLogs { get; set; }

    public virtual DbSet<InventoryPolicy> InventoryPolicies { get; set; }

    public virtual DbSet<InventoryTransaction> InventoryTransactions { get; set; }

    public virtual DbSet<Manufacturer> Manufacturers { get; set; }

    public virtual DbSet<Medicine> Medicines { get; set; }

    public virtual DbSet<MedicineBatch> MedicineBatches { get; set; }

    public virtual DbSet<MedicineContraindication> MedicineContraindications { get; set; }

    public virtual DbSet<MedicineIngredient> MedicineIngredients { get; set; }

    public virtual DbSet<MedicinePriceHistory> MedicinePriceHistories { get; set; }

    public virtual DbSet<MedicineUnitConversion> MedicineUnitConversions { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<PharmacySetting> PharmacySettings { get; set; }

    public virtual DbSet<Prescription> Prescriptions { get; set; }

    public virtual DbSet<PrescriptionDetail> PrescriptionDetails { get; set; }

    public virtual DbSet<PricePolicy> PricePolicies { get; set; }

    public virtual DbSet<ProductRecall> ProductRecalls { get; set; }

    public virtual DbSet<Promotion> Promotions { get; set; }

    public virtual DbSet<PurchaseOrder> PurchaseOrders { get; set; }

    public virtual DbSet<PurchaseOrderDetail> PurchaseOrderDetails { get; set; }

    public virtual DbSet<RecallBatch> RecallBatches { get; set; }

    public virtual DbSet<Return> Returns { get; set; }

    public virtual DbSet<ReturnDetail> ReturnDetails { get; set; }

    public virtual DbSet<RolePermission> RolePermissions { get; set; }

    public virtual DbSet<SalesInvoice> SalesInvoices { get; set; }

    public virtual DbSet<SalesInvoiceDetail> SalesInvoiceDetails { get; set; }

    public virtual DbSet<SalesIssueAllocation> SalesIssueAllocations { get; set; }

    public virtual DbSet<Shift> Shifts { get; set; }

    public virtual DbSet<ShiftClosing> ShiftClosings { get; set; }

    public virtual DbSet<StockDisposal> StockDisposals { get; set; }

    public virtual DbSet<StockDisposalDetail> StockDisposalDetails { get; set; }

    public virtual DbSet<Stocktake> Stocktakes { get; set; }

    public virtual DbSet<StocktakeDetail> StocktakeDetails { get; set; }

    public virtual DbSet<StorageEnvironmentLog> StorageEnvironmentLogs { get; set; }

    public virtual DbSet<StorageLocation> StorageLocations { get; set; }

    public virtual DbSet<Supplier> Suppliers { get; set; }

    public virtual DbSet<SupplierPayment> SupplierPayments { get; set; }

    public virtual DbSet<SupplierReturn> SupplierReturns { get; set; }

    public virtual DbSet<SupplierReturnDetail> SupplierReturnDetails { get; set; }

    public virtual DbSet<Unit> Units { get; set; }

    public virtual DbSet<Warehouse> Warehouses { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity: AspNetUsers, AspNetRoles, ...

        modelBuilder.Entity<ActiveIngredient>(entity =>
        {
            entity.HasKey(e => e.IngredientId);

            entity.HasIndex(e => e.IngredientName, "UQ_ActiveIngredients_Name").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.IngredientName).HasMaxLength(200);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(e => new { e.EntityName, e.EntityId, e.CreatedAt }, "IX_AuditLogs_Entity");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_AuditLogs_UserId");

            entity.Property(e => e.ActionType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_AuditLogs_Created");
            entity.Property(e => e.EntityId).HasMaxLength(100);
            entity.Property(e => e.EntityName)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .IsUnicode(false);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(e => e.CategoryCode, "UQ_Categories_Code").IsUnique();

            entity.Property(e => e.CategoryCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.CategoryName).HasMaxLength(120);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Categories_IsActive");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasIndex(e => e.Phone, "IX_Customers_Phone");

            entity.HasIndex(e => e.CustomerCode, "UQ_Customers_Code").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.ChronicConditions).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_Customers_CreatedAt");
            entity.Property(e => e.CustomerCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Email)
                .HasMaxLength(256)
                .IsUnicode(false);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Customers_IsActive");
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<CustomerAllergy>(entity =>
        {
            entity.HasIndex(e => e.IngredientId, "IX_CustomerAllergies_IngredientId");

            entity.HasIndex(e => new { e.CustomerId, e.IngredientId }, "UQ_CustomerAllergies").IsUnique();

            entity.Property(e => e.ReactionDescription).HasMaxLength(500);
            entity.Property(e => e.RecordedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_CustomerAllergies_Recorded");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerAllergies)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK_CustomerAllergies_Customers");

            entity.HasOne(d => d.Ingredient).WithMany(p => p.CustomerAllergies)
                .HasForeignKey(d => d.IngredientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CustomerAllergies_Ingredients");
        });

        modelBuilder.Entity<CustomerReminder>(entity =>
        {
            entity.HasKey(e => e.ReminderId);

            entity.HasIndex(e => e.CreatedByEmployeeId, "IX_CustomerReminders_CreatedBy");

            entity.HasIndex(e => e.CustomerId, "IX_CustomerReminders_CustomerId");

            entity.HasIndex(e => new { e.Status, e.ReminderDate }, "IX_CustomerReminders_Due");

            entity.HasIndex(e => e.MedicineId, "IX_CustomerReminders_MedicineId");

            entity.Property(e => e.Message).HasMaxLength(500);
            entity.Property(e => e.ReminderDate).HasPrecision(0);
            entity.Property(e => e.SentAt).HasPrecision(0);

            entity.HasOne(d => d.CreatedByEmployee).WithMany(p => p.CustomerReminders)
                .HasForeignKey(d => d.CreatedByEmployeeId)
                .HasConstraintName("FK_CustomerReminders_Employees");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerReminders)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CustomerReminders_Customers");

            entity.HasOne(d => d.Medicine).WithMany(p => p.CustomerReminders)
                .HasForeignKey(d => d.MedicineId)
                .HasConstraintName("FK_CustomerReminders_Medicines");
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasIndex(e => e.EmployeeCode, "UQ_Employees_Code").IsUnique();

            entity.HasIndex(e => e.UserId, "UX_Employees_UserId")
                .IsUnique()
                .HasFilter("([UserId] IS NOT NULL)");

            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_Employees_CreatedAt");
            entity.Property(e => e.Email)
                .HasMaxLength(256)
                .IsUnicode(false);
            entity.Property(e => e.EmployeeCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Employees_IsActive");
            entity.Property(e => e.JobTitle).HasMaxLength(100);
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<GoodsReceipt>(entity =>
        {
            entity.HasIndex(e => e.ReceiptDate, "IX_GoodsReceipts_Date");

            entity.HasIndex(e => e.PurchaseOrderId, "IX_GoodsReceipts_OrderId");

            entity.HasIndex(e => e.ReceivedByEmployeeId, "IX_GoodsReceipts_ReceivedBy");

            entity.HasIndex(e => e.SupplierId, "IX_GoodsReceipts_SupplierId");

            entity.HasIndex(e => e.GoodsReceiptCode, "UQ_GoodsReceipts_Code").IsUnique();

            entity.Property(e => e.GoodsReceiptCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.ReceiptDate)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_GoodsReceipts_Date");
            entity.Property(e => e.SupplierInvoiceNumber)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.PurchaseOrder).WithMany(p => p.GoodsReceipts)
                .HasForeignKey(d => d.PurchaseOrderId)
                .HasConstraintName("FK_GoodsReceipts_Orders");

            entity.HasOne(d => d.ReceivedByEmployee).WithMany(p => p.GoodsReceipts)
                .HasForeignKey(d => d.ReceivedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GoodsReceipts_Employees");

            entity.HasOne(d => d.Supplier).WithMany(p => p.GoodsReceipts)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GoodsReceipts_Suppliers");
        });

        modelBuilder.Entity<GoodsReceiptDetail>(entity =>
        {
            entity.HasIndex(e => e.LocationId, "IX_GoodsReceiptDetails_LocationId");

            entity.HasIndex(e => e.MedicineId, "IX_GoodsReceiptDetails_MedicineId");

            entity.HasIndex(e => e.PurchaseOrderDetailId, "IX_GoodsReceiptDetails_OrderDetailId");

            entity.HasIndex(e => e.GoodsReceiptId, "IX_GoodsReceiptDetails_ReceiptId");

            entity.HasIndex(e => e.UnitId, "IX_GoodsReceiptDetails_UnitId");

            entity.Property(e => e.BatchNumber)
                .HasMaxLength(60)
                .IsUnicode(false);
            entity.Property(e => e.ConversionToBase)
                .HasDefaultValue(1m, "DF_GoodsReceiptDetails_Conversion")
                .HasColumnType("decimal(18, 6)");
            entity.Property(e => e.DamagedQuantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.QualityNote).HasMaxLength(500);
            entity.Property(e => e.QuantityReceived).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.SubTotal)
                .HasComputedColumnSql("(([QuantityReceived]-[DamagedQuantity])*[UnitCost])", true)
                .HasColumnType("decimal(38, 5)");
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.GoodsReceipt).WithMany(p => p.GoodsReceiptDetails)
                .HasForeignKey(d => d.GoodsReceiptId)
                .HasConstraintName("FK_GoodsReceiptDetails_Receipts");

            entity.HasOne(d => d.Location).WithMany(p => p.GoodsReceiptDetails)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("FK_GoodsReceiptDetails_Locations");

            entity.HasOne(d => d.Medicine).WithMany(p => p.GoodsReceiptDetails)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GoodsReceiptDetails_Medicines");

            entity.HasOne(d => d.PurchaseOrderDetail).WithMany(p => p.GoodsReceiptDetails)
                .HasForeignKey(d => d.PurchaseOrderDetailId)
                .HasConstraintName("FK_GoodsReceiptDetails_OrderDetails");

            entity.HasOne(d => d.Unit).WithMany(p => p.GoodsReceiptDetails)
                .HasForeignKey(d => d.UnitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_GoodsReceiptDetails_Units");
        });

        modelBuilder.Entity<IngredientInteraction>(entity =>
        {
            entity.HasKey(e => e.InteractionId);

            entity.HasIndex(e => e.IngredientId2, "IX_IngredientInteractions_Ingredient2");

            entity.HasIndex(e => new { e.IngredientId1, e.IngredientId2 }, "UQ_IngredientInteractions_Pair").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Recommendation).HasMaxLength(500);

            entity.HasOne(d => d.IngredientId1Navigation).WithMany(p => p.IngredientInteractionIngredientId1Navigations)
                .HasForeignKey(d => d.IngredientId1)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_IngredientInteractions_Ingredient1");

            entity.HasOne(d => d.IngredientId2Navigation).WithMany(p => p.IngredientInteractionIngredientId2Navigations)
                .HasForeignKey(d => d.IngredientId2)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_IngredientInteractions_Ingredient2");
        });

        modelBuilder.Entity<IntegrationLog>(entity =>
        {
            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_IntegrationLogs_Status");

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_IntegrationLogs_Created");
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.IntegrationType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.LastAttemptAt).HasPrecision(0);
            entity.Property(e => e.ReferenceType)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<InventoryPolicy>(entity =>
        {
            entity.HasIndex(e => e.WarehouseId, "IX_InventoryPolicies_WarehouseId");

            entity.HasIndex(e => new { e.MedicineId, e.WarehouseId }, "UQ_InventoryPolicies").IsUnique();

            entity.Property(e => e.MinimumQuantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_InventoryPolicies_Updated");

            entity.HasOne(d => d.Medicine).WithMany(p => p.InventoryPolicies)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InventoryPolicies_Medicines");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.InventoryPolicies)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InventoryPolicies_Warehouses");
        });

        modelBuilder.Entity<InventoryTransaction>(entity =>
        {
            entity.HasIndex(e => new { e.BatchId, e.TransactionDate }, "IX_InventoryTransactions_BatchDate");

            entity.HasIndex(e => e.CreatedByEmployeeId, "IX_InventoryTransactions_CreatedBy");

            entity.HasIndex(e => new { e.MedicineId, e.TransactionDate }, "IX_InventoryTransactions_MedicineDate");

            entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId }, "IX_InventoryTransactions_Reference");

            entity.HasIndex(e => e.WarehouseId, "IX_InventoryTransactions_WarehouseId");

            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.QuantityChange).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.ReferenceType)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.TransactionDate)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_InventoryTransactions_Date");

            entity.HasOne(d => d.Batch).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InventoryTransactions_Batches");

            entity.HasOne(d => d.CreatedByEmployee).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.CreatedByEmployeeId)
                .HasConstraintName("FK_InventoryTransactions_Employees");

            entity.HasOne(d => d.Medicine).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InventoryTransactions_Medicines");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InventoryTransactions_Warehouses");
        });

        modelBuilder.Entity<Manufacturer>(entity =>
        {
            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.Country).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Manufacturers_IsActive");
            entity.Property(e => e.ManufacturerName).HasMaxLength(200);
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Medicine>(entity =>
        {
            entity.HasIndex(e => e.BaseUnitId, "IX_Medicines_BaseUnitId");

            entity.HasIndex(e => e.CategoryId, "IX_Medicines_CategoryId");

            entity.HasIndex(e => e.ManufacturerId, "IX_Medicines_ManufacturerId");

            entity.HasIndex(e => e.MedicineName, "IX_Medicines_Name");

            entity.HasIndex(e => e.MedicineCode, "UQ_Medicines_Code").IsUnique();

            entity.HasIndex(e => e.Barcode, "UX_Medicines_Barcode")
                .IsUnique()
                .HasFilter("([Barcode] IS NOT NULL)");

            entity.Property(e => e.Barcode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Contraindications).HasMaxLength(1000);
            entity.Property(e => e.CountryOfOrigin).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_Medicines_CreatedAt");
            entity.Property(e => e.DefaultPurchasePrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DefaultSellingPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DosageForm).HasMaxLength(100);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.Interactions).HasMaxLength(1000);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Medicines_IsActive");
            entity.Property(e => e.MaxDailyDose).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.MaxSingleDose).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.MedicineCode)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.MedicineName).HasMaxLength(250);
            entity.Property(e => e.RegistrationNumber)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.Strength).HasMaxLength(100);
            entity.Property(e => e.UsageInstructions).HasMaxLength(1000);

            entity.HasOne(d => d.BaseUnit).WithMany(p => p.Medicines)
                .HasForeignKey(d => d.BaseUnitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Medicines_Units");

            entity.HasOne(d => d.Category).WithMany(p => p.Medicines)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Medicines_Categories");

            entity.HasOne(d => d.Manufacturer).WithMany(p => p.Medicines)
                .HasForeignKey(d => d.ManufacturerId)
                .HasConstraintName("FK_Medicines_Manufacturers");
        });

        modelBuilder.Entity<MedicineBatch>(entity =>
        {
            entity.HasKey(e => e.BatchId);

            entity.HasIndex(e => new { e.BatchNumber, e.MedicineId }, "IX_MedicineBatches_BatchNumber");

            entity.HasIndex(e => e.ExpiryDate, "IX_MedicineBatches_Expiry");

            entity.HasIndex(e => new { e.MedicineId, e.Status, e.ExpiryDate }, "IX_MedicineBatches_FEFO");

            entity.HasIndex(e => e.LocationId, "IX_MedicineBatches_LocationId");

            entity.HasIndex(e => e.GoodsReceiptId, "IX_MedicineBatches_ReceiptId");

            entity.HasIndex(e => e.WarehouseId, "IX_MedicineBatches_WarehouseId");

            entity.HasIndex(e => e.GoodsReceiptDetailId, "UX_MedicineBatches_ReceiptDetail")
                .IsUnique()
                .HasFilter("([GoodsReceiptDetailId] IS NOT NULL)");

            entity.Property(e => e.BatchNumber)
                .HasMaxLength(60)
                .IsUnicode(false);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.QuantityOnHand).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status).HasDefaultValue((byte)1, "DF_MedicineBatches_Status");
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.GoodsReceiptDetail).WithOne(p => p.MedicineBatch)
                .HasForeignKey<MedicineBatch>(d => d.GoodsReceiptDetailId)
                .HasConstraintName("FK_MedicineBatches_ReceiptDetails");

            entity.HasOne(d => d.GoodsReceipt).WithMany(p => p.MedicineBatches)
                .HasForeignKey(d => d.GoodsReceiptId)
                .HasConstraintName("FK_MedicineBatches_Receipts");

            entity.HasOne(d => d.Location).WithMany(p => p.MedicineBatches)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("FK_MedicineBatches_Locations");

            entity.HasOne(d => d.Medicine).WithMany(p => p.MedicineBatches)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MedicineBatches_Medicines");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.MedicineBatches)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MedicineBatches_Warehouses");
        });

        modelBuilder.Entity<MedicineContraindication>(entity =>
        {
            entity.HasIndex(e => new { e.MedicineId, e.ConditionKeyword }, "UQ_MedicineContraindications").IsUnique();

            entity.Property(e => e.ConditionKeyword).HasMaxLength(150);
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasOne(d => d.Medicine).WithMany(p => p.MedicineContraindications)
                .HasForeignKey(d => d.MedicineId)
                .HasConstraintName("FK_MedicineContraindications_Medicines");
        });

        modelBuilder.Entity<MedicineIngredient>(entity =>
        {
            entity.HasKey(e => new { e.MedicineId, e.IngredientId });

            entity.HasIndex(e => e.IngredientId, "IX_MedicineIngredients_IngredientId");

            entity.Property(e => e.IngredientStrength).HasMaxLength(100);

            entity.HasOne(d => d.Ingredient).WithMany(p => p.MedicineIngredients)
                .HasForeignKey(d => d.IngredientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MedicineIngredients_Ingredients");

            entity.HasOne(d => d.Medicine).WithMany(p => p.MedicineIngredients)
                .HasForeignKey(d => d.MedicineId)
                .HasConstraintName("FK_MedicineIngredients_Medicines");
        });

        modelBuilder.Entity<MedicinePriceHistory>(entity =>
        {
            entity.HasKey(e => e.PriceHistoryId);

            entity.HasIndex(e => e.ChangedByEmployeeId, "IX_MedicinePriceHistories_ChangedBy");

            entity.HasIndex(e => new { e.MedicineId, e.UnitId, e.EffectiveFrom }, "IX_MedicinePriceHistories_Medicine");

            entity.HasIndex(e => e.UnitId, "IX_MedicinePriceHistories_UnitId");

            entity.Property(e => e.ChangeReason).HasMaxLength(500);
            entity.Property(e => e.EffectiveFrom).HasPrecision(0);
            entity.Property(e => e.EffectiveTo).HasPrecision(0);
            entity.Property(e => e.OldSellingPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PurchasePrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.SellingPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.ChangedByEmployee).WithMany(p => p.MedicinePriceHistories)
                .HasForeignKey(d => d.ChangedByEmployeeId)
                .HasConstraintName("FK_MedicinePriceHistories_Employees");

            entity.HasOne(d => d.Medicine).WithMany(p => p.MedicinePriceHistories)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MedicinePriceHistories_Medicines");

            entity.HasOne(d => d.Unit).WithMany(p => p.MedicinePriceHistories)
                .HasForeignKey(d => d.UnitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MedicinePriceHistories_Units");
        });

        modelBuilder.Entity<MedicineUnitConversion>(entity =>
        {
            entity.HasKey(e => e.ConversionId);

            entity.HasIndex(e => e.UnitId, "IX_MedicineUnitConversions_UnitId");

            entity.HasIndex(e => new { e.MedicineId, e.UnitId }, "UQ_MedicineUnitConversions").IsUnique();

            entity.Property(e => e.ConversionToBase).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.SellingPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Medicine).WithMany(p => p.MedicineUnitConversions)
                .HasForeignKey(d => d.MedicineId)
                .HasConstraintName("FK_MedicineUnitConversions_Medicines");

            entity.HasOne(d => d.Unit).WithMany(p => p.MedicineUnitConversions)
                .HasForeignKey(d => d.UnitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MedicineUnitConversions_Units");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasIndex(e => new { e.InvoiceId, e.PaidAt }, "IX_Payments_Invoice");

            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.PaidAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_Payments_PaidAt");
            entity.Property(e => e.Status).HasDefaultValue((byte)1, "DF_Payments_Status");
            entity.Property(e => e.TenderedAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TransactionReference)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.Invoice).WithMany(p => p.Payments)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payments_Invoices");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasIndex(e => e.PermissionCode, "UQ_Permissions_Code").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(300);
            entity.Property(e => e.ModuleName).HasMaxLength(80);
            entity.Property(e => e.PermissionCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.PermissionName).HasMaxLength(150);
        });

        modelBuilder.Entity<PharmacySetting>(entity =>
        {
            entity.HasKey(e => e.SettingKey);

            entity.HasIndex(e => e.UpdatedByEmployeeId, "IX_PharmacySettings_UpdatedBy");

            entity.Property(e => e.SettingKey)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.Description).HasMaxLength(300);
            entity.Property(e => e.SettingValue).HasMaxLength(500);
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_PharmacySettings_Updated");

            entity.HasOne(d => d.UpdatedByEmployee).WithMany(p => p.PharmacySettings)
                .HasForeignKey(d => d.UpdatedByEmployeeId)
                .HasConstraintName("FK_PharmacySettings_Employees");
        });

        modelBuilder.Entity<Prescription>(entity =>
        {
            entity.HasIndex(e => e.CheckedByEmployeeId, "IX_Prescriptions_CheckedBy");

            entity.HasIndex(e => new { e.CustomerId, e.PrescriptionDate }, "IX_Prescriptions_Customer");

            entity.HasIndex(e => e.PrescriptionCode, "UQ_Prescriptions_Code").IsUnique();

            entity.Property(e => e.ClinicName).HasMaxLength(200);
            entity.Property(e => e.Diagnosis).HasMaxLength(500);
            entity.Property(e => e.DoctorName).HasMaxLength(150);
            entity.Property(e => e.ImagePath).HasMaxLength(500);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.PrescriptionCode)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.HasOne(d => d.CheckedByEmployee).WithMany(p => p.Prescriptions)
                .HasForeignKey(d => d.CheckedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Prescriptions_Employees");

            entity.HasOne(d => d.Customer).WithMany(p => p.Prescriptions)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Prescriptions_Customers");
        });

        modelBuilder.Entity<PrescriptionDetail>(entity =>
        {
            entity.HasIndex(e => e.MedicineId, "IX_PrescriptionDetails_MedicineId");

            entity.HasIndex(e => e.PrescriptionId, "IX_PrescriptionDetails_PrescriptionId");

            entity.HasIndex(e => e.UnitId, "IX_PrescriptionDetails_UnitId");

            entity.Property(e => e.Dosage).HasMaxLength(300);
            entity.Property(e => e.DosePerTime).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.Frequency).HasMaxLength(200);
            entity.Property(e => e.Instructions).HasMaxLength(500);
            entity.Property(e => e.PrescribedQuantity).HasColumnType("decimal(18, 3)");

            entity.HasOne(d => d.Medicine).WithMany(p => p.PrescriptionDetails)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PrescriptionDetails_Medicines");

            entity.HasOne(d => d.Prescription).WithMany(p => p.PrescriptionDetails)
                .HasForeignKey(d => d.PrescriptionId)
                .HasConstraintName("FK_PrescriptionDetails_Prescriptions");

            entity.HasOne(d => d.Unit).WithMany(p => p.PrescriptionDetails)
                .HasForeignKey(d => d.UnitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PrescriptionDetails_Units");
        });

        modelBuilder.Entity<PricePolicy>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_PricePolicies_Active");
            entity.Property(e => e.MaxDiscountPercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.PolicyName).HasMaxLength(150);
            entity.Property(e => e.ReturnConditions).HasMaxLength(1000);
        });

        modelBuilder.Entity<ProductRecall>(entity =>
        {
            entity.HasKey(e => e.RecallId);

            entity.HasIndex(e => e.CreatedByEmployeeId, "IX_ProductRecalls_CreatedBy");

            entity.HasIndex(e => e.MedicineId, "IX_ProductRecalls_MedicineId");

            entity.HasIndex(e => e.RecallCode, "UQ_ProductRecalls_Code").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_ProductRecalls_Created");
            entity.Property(e => e.NoticeNumber)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.RecallCode)
                .HasMaxLength(40)
                .IsUnicode(false);

            entity.HasOne(d => d.CreatedByEmployee).WithMany(p => p.ProductRecalls)
                .HasForeignKey(d => d.CreatedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProductRecalls_Employees");

            entity.HasOne(d => d.Medicine).WithMany(p => p.ProductRecalls)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProductRecalls_Medicines");
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.HasIndex(e => e.CategoryId, "IX_Promotions_CategoryId");

            entity.HasIndex(e => e.CreatedByEmployeeId, "IX_Promotions_CreatedBy");

            entity.HasIndex(e => new { e.EffectiveFrom, e.EffectiveTo }, "IX_Promotions_Effective");

            entity.HasIndex(e => e.MedicineId, "IX_Promotions_MedicineId");

            entity.HasIndex(e => e.PromotionCode, "UQ_Promotions_Code").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_Promotions_CreatedAt");
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DiscountPercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.EffectiveFrom).HasPrecision(0);
            entity.Property(e => e.EffectiveTo).HasPrecision(0);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Promotions_IsActive");
            entity.Property(e => e.PromotionCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.PromotionName).HasMaxLength(200);

            entity.HasOne(d => d.Category).WithMany(p => p.Promotions)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("FK_Promotions_Categories");

            entity.HasOne(d => d.CreatedByEmployee).WithMany(p => p.Promotions)
                .HasForeignKey(d => d.CreatedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Promotions_Employees");

            entity.HasOne(d => d.Medicine).WithMany(p => p.Promotions)
                .HasForeignKey(d => d.MedicineId)
                .HasConstraintName("FK_Promotions_Medicines");
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasIndex(e => e.CreatedByEmployeeId, "IX_PurchaseOrders_CreatedBy");

            entity.HasIndex(e => e.OrderDate, "IX_PurchaseOrders_Date");

            entity.HasIndex(e => e.SupplierId, "IX_PurchaseOrders_SupplierId");

            entity.HasIndex(e => e.PurchaseOrderCode, "UQ_PurchaseOrders_Code").IsUnique();

            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.OrderDate)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_PurchaseOrders_OrderDate");
            entity.Property(e => e.PurchaseOrderCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.CreatedByEmployee).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.CreatedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrders_Employees");

            entity.HasOne(d => d.Supplier).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrders_Suppliers");
        });

        modelBuilder.Entity<PurchaseOrderDetail>(entity =>
        {
            entity.HasIndex(e => e.MedicineId, "IX_PurchaseOrderDetails_MedicineId");

            entity.HasIndex(e => e.PurchaseOrderId, "IX_PurchaseOrderDetails_OrderId");

            entity.HasIndex(e => e.UnitId, "IX_PurchaseOrderDetails_UnitId");

            entity.Property(e => e.ExpectedUnitPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.OrderedQuantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.ReceivedQuantity).HasColumnType("decimal(18, 3)");

            entity.HasOne(d => d.Medicine).WithMany(p => p.PurchaseOrderDetails)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrderDetails_Medicines");

            entity.HasOne(d => d.PurchaseOrder).WithMany(p => p.PurchaseOrderDetails)
                .HasForeignKey(d => d.PurchaseOrderId)
                .HasConstraintName("FK_PurchaseOrderDetails_Orders");

            entity.HasOne(d => d.Unit).WithMany(p => p.PurchaseOrderDetails)
                .HasForeignKey(d => d.UnitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PurchaseOrderDetails_Units");
        });

        modelBuilder.Entity<RecallBatch>(entity =>
        {
            entity.HasIndex(e => e.BatchId, "IX_RecallBatches_BatchId");

            entity.HasIndex(e => new { e.RecallId, e.BatchId }, "UQ_RecallBatches").IsUnique();

            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.QuantityOnHandAtRecall).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.QuantityRecovered).HasColumnType("decimal(18, 3)");

            entity.HasOne(d => d.Batch).WithMany(p => p.RecallBatches)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RecallBatches_Batches");

            entity.HasOne(d => d.Recall).WithMany(p => p.RecallBatches)
                .HasForeignKey(d => d.RecallId)
                .HasConstraintName("FK_RecallBatches_Recalls");
        });

        modelBuilder.Entity<Return>(entity =>
        {
            entity.HasIndex(e => new { e.InvoiceId, e.ReturnDate }, "IX_Returns_Invoice");

            entity.HasIndex(e => e.ProcessedByEmployeeId, "IX_Returns_ProcessedBy");

            entity.HasIndex(e => new { e.ShiftId, e.RefundMethod }, "IX_Returns_Shift");

            entity.HasIndex(e => e.ReturnCode, "UQ_Returns_Code").IsUnique();

            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RefundAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.RefundMethod).HasDefaultValue((byte)1, "DF_Returns_RefundMethod");
            entity.Property(e => e.RefundReference)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.ReturnCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.ReturnDate)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_Returns_Date");

            entity.HasOne(d => d.Invoice).WithMany(p => p.Returns)
                .HasForeignKey(d => d.InvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Returns_Invoices");

            entity.HasOne(d => d.ProcessedByEmployee).WithMany(p => p.Returns)
                .HasForeignKey(d => d.ProcessedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Returns_Employees");

            entity.HasOne(d => d.Shift).WithMany(p => p.Returns)
                .HasForeignKey(d => d.ShiftId)
                .HasConstraintName("FK_Returns_Shifts");
        });

        modelBuilder.Entity<ReturnDetail>(entity =>
        {
            entity.HasIndex(e => e.BatchId, "IX_ReturnDetails_BatchId");

            entity.HasIndex(e => e.InvoiceDetailId, "IX_ReturnDetails_InvoiceDetailId");

            entity.HasIndex(e => e.ReturnId, "IX_ReturnDetails_ReturnId");

            entity.Property(e => e.ConditionNote).HasMaxLength(500);
            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.UnitRefundPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Batch).WithMany(p => p.ReturnDetails)
                .HasForeignKey(d => d.BatchId)
                .HasConstraintName("FK_ReturnDetails_Batches");

            entity.HasOne(d => d.InvoiceDetail).WithMany(p => p.ReturnDetails)
                .HasForeignKey(d => d.InvoiceDetailId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ReturnDetails_InvoiceDetails");

            entity.HasOne(d => d.Return).WithMany(p => p.ReturnDetails)
                .HasForeignKey(d => d.ReturnId)
                .HasConstraintName("FK_ReturnDetails_Returns");
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(e => new { e.RoleId, e.PermissionId });

            entity.HasIndex(e => e.PermissionId, "IX_RolePermissions_PermissionId");

            entity.HasOne(d => d.Permission).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.PermissionId)
                .HasConstraintName("FK_RolePermissions_Permissions");
        });

        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            entity.HasKey(e => e.InvoiceId);

            entity.HasIndex(e => e.CancelledByEmployeeId, "IX_SalesInvoices_CancelledBy");

            entity.HasIndex(e => new { e.CustomerId, e.InvoiceDate }, "IX_SalesInvoices_CustomerDate");

            entity.HasIndex(e => e.InvoiceDate, "IX_SalesInvoices_Date");

            entity.HasIndex(e => new { e.EmployeeId, e.InvoiceDate }, "IX_SalesInvoices_EmployeeDate");

            entity.HasIndex(e => e.PrescriptionId, "IX_SalesInvoices_PrescriptionId");

            entity.HasIndex(e => new { e.ShiftId, e.Status }, "IX_SalesInvoices_Shift");

            entity.HasIndex(e => new { e.Status, e.InvoiceDate }, "IX_SalesInvoices_Status");

            entity.HasIndex(e => e.InvoiceCode, "UQ_SalesInvoices_Code").IsUnique();

            entity.HasIndex(e => e.EinvoiceNumber, "UX_SalesInvoices_EInvoiceNumber")
                .IsUnique()
                .HasFilter("([EInvoiceNumber] IS NOT NULL)");

            entity.Property(e => e.BuyerAddress).HasMaxLength(300);
            entity.Property(e => e.BuyerName).HasMaxLength(200);
            entity.Property(e => e.BuyerTaxCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.CancelReason).HasMaxLength(500);
            entity.Property(e => e.CancelledAt).HasPrecision(0);
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.EinvoiceIssuedAt)
                .HasPrecision(0)
                .HasColumnName("EInvoiceIssuedAt");
            entity.Property(e => e.EinvoiceNumber)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("EInvoiceNumber");
            entity.Property(e => e.EinvoiceStatus).HasColumnName("EInvoiceStatus");
            entity.Property(e => e.InvoiceCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.InvoiceDate)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_SalesInvoices_Date");
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.SubTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TaxAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.CancelledByEmployee).WithMany(p => p.SalesInvoiceCancelledByEmployees)
                .HasForeignKey(d => d.CancelledByEmployeeId)
                .HasConstraintName("FK_SalesInvoices_Cancellers");

            entity.HasOne(d => d.Customer).WithMany(p => p.SalesInvoices)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK_SalesInvoices_Customers");

            entity.HasOne(d => d.Employee).WithMany(p => p.SalesInvoiceEmployees)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoices_Employees");

            entity.HasOne(d => d.Prescription).WithMany(p => p.SalesInvoices)
                .HasForeignKey(d => d.PrescriptionId)
                .HasConstraintName("FK_SalesInvoices_Prescriptions");

            entity.HasOne(d => d.Shift).WithMany(p => p.SalesInvoices)
                .HasForeignKey(d => d.ShiftId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoices_Shifts");
        });

        modelBuilder.Entity<SalesInvoiceDetail>(entity =>
        {
            entity.HasKey(e => e.InvoiceDetailId);

            entity.HasIndex(e => e.InvoiceId, "IX_SalesInvoiceDetails_InvoiceId");

            entity.HasIndex(e => e.MedicineId, "IX_SalesInvoiceDetails_MedicineId");

            entity.HasIndex(e => e.PromotionId, "IX_SalesInvoiceDetails_PromotionId");

            entity.HasIndex(e => e.UnitId, "IX_SalesInvoiceDetails_UnitId");

            entity.Property(e => e.ConversionToBase).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.LineTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Invoice).WithMany(p => p.SalesInvoiceDetails)
                .HasForeignKey(d => d.InvoiceId)
                .HasConstraintName("FK_SalesInvoiceDetails_Invoices");

            entity.HasOne(d => d.Medicine).WithMany(p => p.SalesInvoiceDetails)
                .HasForeignKey(d => d.MedicineId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoiceDetails_Medicines");

            entity.HasOne(d => d.Promotion).WithMany(p => p.SalesInvoiceDetails)
                .HasForeignKey(d => d.PromotionId)
                .HasConstraintName("FK_SalesInvoiceDetails_Promotions");

            entity.HasOne(d => d.Unit).WithMany(p => p.SalesInvoiceDetails)
                .HasForeignKey(d => d.UnitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoiceDetails_Units");
        });

        modelBuilder.Entity<SalesIssueAllocation>(entity =>
        {
            entity.HasKey(e => e.AllocationId);

            entity.HasIndex(e => e.BatchId, "IX_SalesIssueAllocations_BatchId");

            entity.HasIndex(e => e.InvoiceDetailId, "IX_SalesIssueAllocations_DetailId");

            entity.Property(e => e.AllocatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_SalesIssueAllocations_Date");
            entity.Property(e => e.QuantityBase).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.UnitCostAtSale).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Batch).WithMany(p => p.SalesIssueAllocations)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesIssueAllocations_Batches");

            entity.HasOne(d => d.InvoiceDetail).WithMany(p => p.SalesIssueAllocations)
                .HasForeignKey(d => d.InvoiceDetailId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesIssueAllocations_Details");
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasIndex(e => e.ShiftCode, "UQ_Shifts_Code").IsUnique();

            entity.HasIndex(e => e.EmployeeId, "UX_Shifts_OpenPerEmployee")
                .IsUnique()
                .HasFilter("([ClosedAt] IS NULL)");

            entity.Property(e => e.ClosedAt).HasPrecision(0);
            entity.Property(e => e.HandedOverAt).HasPrecision(0);
            entity.Property(e => e.HandoverNote).HasMaxLength(1000);
            entity.Property(e => e.OpenedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_Shifts_Opened");
            entity.Property(e => e.OpeningCash).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ShiftCode)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.HasOne(d => d.Employee).WithOne(p => p.Shift)
                .HasForeignKey<Shift>(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Shifts_Employees");
        });

        modelBuilder.Entity<ShiftClosing>(entity =>
        {
            entity.HasIndex(e => e.ClosedByEmployeeId, "IX_ShiftClosings_ClosedBy");

            entity.HasIndex(e => e.ShiftId, "UQ_ShiftClosings_Shift").IsUnique();

            entity.Property(e => e.ActualCash).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CashDifference)
                .HasComputedColumnSql("([ActualCash]-[ExpectedCash])", true)
                .HasColumnType("decimal(19, 2)");
            entity.Property(e => e.ClosedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_ShiftClosings_Date");
            entity.Property(e => e.ExpectedCash).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.OpeningCash).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TotalCashRefunds).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TotalCashSales).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TotalNonCash).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.ClosedByEmployee).WithMany(p => p.ShiftClosings)
                .HasForeignKey(d => d.ClosedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShiftClosings_Employees");

            entity.HasOne(d => d.Shift).WithOne(p => p.ShiftClosing)
                .HasForeignKey<ShiftClosing>(d => d.ShiftId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShiftClosings_Shifts");
        });

        modelBuilder.Entity<StockDisposal>(entity =>
        {
            entity.HasKey(e => e.DisposalId);

            entity.HasIndex(e => e.CreatedByEmployeeId, "IX_StockDisposals_CreatedBy");

            entity.HasIndex(e => e.WarehouseId, "IX_StockDisposals_WarehouseId");

            entity.HasIndex(e => e.DisposalCode, "UQ_StockDisposals_Code").IsUnique();

            entity.Property(e => e.DisposalCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.DisposalDate)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_StockDisposals_Date");
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.Reason).HasMaxLength(500);

            entity.HasOne(d => d.CreatedByEmployee).WithMany(p => p.StockDisposals)
                .HasForeignKey(d => d.CreatedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDisposals_Employees");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StockDisposals)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDisposals_Warehouses");
        });

        modelBuilder.Entity<StockDisposalDetail>(entity =>
        {
            entity.HasKey(e => e.DisposalDetailId);

            entity.HasIndex(e => e.BatchId, "IX_StockDisposalDetails_BatchId");

            entity.HasIndex(e => new { e.DisposalId, e.BatchId }, "UQ_StockDisposalDetails").IsUnique();

            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.ReasonNote).HasMaxLength(500);
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Batch).WithMany(p => p.StockDisposalDetails)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StockDisposalDetails_Batches");

            entity.HasOne(d => d.Disposal).WithMany(p => p.StockDisposalDetails)
                .HasForeignKey(d => d.DisposalId)
                .HasConstraintName("FK_StockDisposalDetails_Disposals");
        });

        modelBuilder.Entity<Stocktake>(entity =>
        {
            entity.HasIndex(e => e.AdjustedByEmployeeId, "IX_Stocktakes_AdjustedBy");

            entity.HasIndex(e => e.ApprovedByEmployeeId, "IX_Stocktakes_ApprovedBy");

            entity.HasIndex(e => e.CategoryId, "IX_Stocktakes_CategoryId");

            entity.HasIndex(e => e.CreatedByEmployeeId, "IX_Stocktakes_CreatedBy");

            entity.HasIndex(e => e.LocationId, "IX_Stocktakes_LocationId");

            entity.HasIndex(e => e.WarehouseId, "IX_Stocktakes_WarehouseId");

            entity.HasIndex(e => e.StocktakeCode, "UQ_Stocktakes_Code").IsUnique();

            entity.Property(e => e.AdjustedAt).HasPrecision(0);
            entity.Property(e => e.ApprovalNote).HasMaxLength(500);
            entity.Property(e => e.ApprovedAt).HasPrecision(0);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.StocktakeCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.StocktakeDate)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_Stocktakes_Date");

            entity.HasOne(d => d.AdjustedByEmployee).WithMany(p => p.StocktakeAdjustedByEmployees)
                .HasForeignKey(d => d.AdjustedByEmployeeId)
                .HasConstraintName("FK_Stocktakes_Adjusters");

            entity.HasOne(d => d.ApprovedByEmployee).WithMany(p => p.StocktakeApprovedByEmployees)
                .HasForeignKey(d => d.ApprovedByEmployeeId)
                .HasConstraintName("FK_Stocktakes_Approvers");

            entity.HasOne(d => d.Category).WithMany(p => p.Stocktakes)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("FK_Stocktakes_Categories");

            entity.HasOne(d => d.CreatedByEmployee).WithMany(p => p.StocktakeCreatedByEmployees)
                .HasForeignKey(d => d.CreatedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Stocktakes_Creators");

            entity.HasOne(d => d.Location).WithMany(p => p.Stocktakes)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("FK_Stocktakes_Locations");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.Stocktakes)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Stocktakes_Warehouses");
        });

        modelBuilder.Entity<StocktakeDetail>(entity =>
        {
            entity.HasIndex(e => e.BatchId, "IX_StocktakeDetails_BatchId");

            entity.HasIndex(e => new { e.StocktakeId, e.BatchId }, "UQ_StocktakeDetails").IsUnique();

            entity.Property(e => e.ActualQuantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.Difference)
                .HasComputedColumnSql("([ActualQuantity]-[SystemQuantity])", true)
                .HasColumnType("decimal(19, 3)");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.SystemQuantity).HasColumnType("decimal(18, 3)");

            entity.HasOne(d => d.Batch).WithMany(p => p.StocktakeDetails)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StocktakeDetails_Batches");

            entity.HasOne(d => d.Stocktake).WithMany(p => p.StocktakeDetails)
                .HasForeignKey(d => d.StocktakeId)
                .HasConstraintName("FK_StocktakeDetails_Stocktakes");
        });

        modelBuilder.Entity<StorageEnvironmentLog>(entity =>
        {
            entity.HasKey(e => e.EnvironmentLogId);

            entity.HasIndex(e => e.LocationId, "IX_StorageEnvironmentLogs_LocationId");

            entity.HasIndex(e => e.RecordedByEmployeeId, "IX_StorageEnvironmentLogs_RecordedBy");

            entity.HasIndex(e => new { e.WarehouseId, e.RecordedAt }, "IX_StorageEnvironmentLogs_WarehouseDate");

            entity.Property(e => e.HumidityPercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.RecordedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_StorageEnvironmentLogs_Date");
            entity.Property(e => e.TemperatureC).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.Location).WithMany(p => p.StorageEnvironmentLogs)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("FK_StorageEnvironmentLogs_Locations");

            entity.HasOne(d => d.RecordedByEmployee).WithMany(p => p.StorageEnvironmentLogs)
                .HasForeignKey(d => d.RecordedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StorageEnvironmentLogs_Employees");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StorageEnvironmentLogs)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StorageEnvironmentLogs_Warehouses");
        });

        modelBuilder.Entity<StorageLocation>(entity =>
        {
            entity.HasKey(e => e.LocationId);

            entity.HasIndex(e => e.ParentLocationId, "IX_StorageLocations_Parent");

            entity.HasIndex(e => new { e.WarehouseId, e.LocationCode }, "UQ_StorageLocations").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(300);
            entity.Property(e => e.LocationCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.LocationName).HasMaxLength(100);
            entity.Property(e => e.LocationType).HasDefaultValue((byte)2, "DF_StorageLocations_Type");

            entity.HasOne(d => d.ParentLocation).WithMany(p => p.InverseParentLocation)
                .HasForeignKey(d => d.ParentLocationId)
                .HasConstraintName("FK_StorageLocations_Parent");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StorageLocations)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StorageLocations_Warehouses");
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasIndex(e => e.SupplierCode, "UQ_Suppliers_Code").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.ContactPerson).HasMaxLength(150);
            entity.Property(e => e.Email)
                .HasMaxLength(256)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Suppliers_IsActive");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.SupplierCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.SupplierName).HasMaxLength(200);
            entity.Property(e => e.TaxCode)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<SupplierPayment>(entity =>
        {
            entity.HasIndex(e => e.EmployeeId, "IX_SupplierPayments_EmployeeId");

            entity.HasIndex(e => e.GoodsReceiptId, "IX_SupplierPayments_ReceiptId");

            entity.HasIndex(e => e.SupplierReturnId, "IX_SupplierPayments_ReturnId");

            entity.HasIndex(e => new { e.SupplierId, e.PaidAt }, "IX_SupplierPayments_SupplierDate");

            entity.HasIndex(e => e.SupplierPaymentCode, "UQ_SupplierPayments_Code").IsUnique();

            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.PaidAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_SupplierPayments_Date");
            entity.Property(e => e.Status).HasDefaultValue((byte)1, "DF_SupplierPayments_Status");
            entity.Property(e => e.SupplierPaymentCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.TransactionReference)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.Employee).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierPayments_Employees");

            entity.HasOne(d => d.GoodsReceipt).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.GoodsReceiptId)
                .HasConstraintName("FK_SupplierPayments_Receipts");

            entity.HasOne(d => d.Supplier).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierPayments_Suppliers");

            entity.HasOne(d => d.SupplierReturn).WithMany(p => p.SupplierPayments)
                .HasForeignKey(d => d.SupplierReturnId)
                .HasConstraintName("FK_SupplierPayments_Returns");
        });

        modelBuilder.Entity<SupplierReturn>(entity =>
        {
            entity.HasIndex(e => e.CreatedByEmployeeId, "IX_SupplierReturns_CreatedBy");

            entity.HasIndex(e => e.ReturnDate, "IX_SupplierReturns_Date");

            entity.HasIndex(e => e.GoodsReceiptId, "IX_SupplierReturns_ReceiptId");

            entity.HasIndex(e => e.SupplierId, "IX_SupplierReturns_SupplierId");

            entity.HasIndex(e => e.SupplierReturnCode, "UQ_SupplierReturns_Code").IsUnique();

            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.ReturnDate)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())", "DF_SupplierReturns_Date");
            entity.Property(e => e.SupplierReturnCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.CreatedByEmployee).WithMany(p => p.SupplierReturns)
                .HasForeignKey(d => d.CreatedByEmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierReturns_Employees");

            entity.HasOne(d => d.GoodsReceipt).WithMany(p => p.SupplierReturns)
                .HasForeignKey(d => d.GoodsReceiptId)
                .HasConstraintName("FK_SupplierReturns_Receipts");

            entity.HasOne(d => d.Supplier).WithMany(p => p.SupplierReturns)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierReturns_Suppliers");
        });

        modelBuilder.Entity<SupplierReturnDetail>(entity =>
        {
            entity.HasIndex(e => e.BatchId, "IX_SupplierReturnDetails_BatchId");

            entity.HasIndex(e => e.SupplierReturnId, "IX_SupplierReturnDetails_ReturnId");

            entity.Property(e => e.LineTotal)
                .HasComputedColumnSql("([Quantity]*[UnitCost])", true)
                .HasColumnType("decimal(37, 5)");
            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Batch).WithMany(p => p.SupplierReturnDetails)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SupplierReturnDetails_Batches");

            entity.HasOne(d => d.SupplierReturn).WithMany(p => p.SupplierReturnDetails)
                .HasForeignKey(d => d.SupplierReturnId)
                .HasConstraintName("FK_SupplierReturnDetails_Returns");
        });

        modelBuilder.Entity<Unit>(entity =>
        {
            entity.HasIndex(e => e.UnitCode, "UQ_Units_Code").IsUnique();

            entity.Property(e => e.UnitCode)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.UnitName).HasMaxLength(50);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.HasIndex(e => e.WarehouseCode, "UQ_Warehouses_Code").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Warehouses_IsActive");
            entity.Property(e => e.WarehouseCode)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.WarehouseName).HasMaxLength(150);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
