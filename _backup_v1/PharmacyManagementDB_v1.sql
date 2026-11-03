
/* ============================================================
   PharmacyManagementDB - ASP.NET Core MVC / SQL Server
   Schema draft for an advanced university pharmacy project.
   Run in SQL Server Management Studio (SSMS).
   ============================================================ */

IF DB_ID(N'PharmacyManagementDB') IS NULL
    CREATE DATABASE PharmacyManagementDB;
GO
USE PharmacyManagementDB;
GO

/* 1. ASP.NET Core Identity (string keys, compatible with IdentityUser) */
CREATE TABLE dbo.AspNetRoles (
    Id nvarchar(450) NOT NULL CONSTRAINT PK_AspNetRoles PRIMARY KEY,
    Name nvarchar(256) NULL,
    NormalizedName nvarchar(256) NULL,
    ConcurrencyStamp nvarchar(max) NULL
);
GO
CREATE UNIQUE INDEX RoleNameIndex ON dbo.AspNetRoles(NormalizedName)
WHERE NormalizedName IS NOT NULL;
GO

CREATE TABLE dbo.AspNetUsers (
    Id nvarchar(450) NOT NULL CONSTRAINT PK_AspNetUsers PRIMARY KEY,
    UserName nvarchar(256) NULL,
    NormalizedUserName nvarchar(256) NULL,
    Email nvarchar(256) NULL,
    NormalizedEmail nvarchar(256) NULL,
    EmailConfirmed bit NOT NULL CONSTRAINT DF_AspNetUsers_EmailConfirmed DEFAULT 0,
    PasswordHash nvarchar(max) NULL,
    SecurityStamp nvarchar(max) NULL,
    ConcurrencyStamp nvarchar(max) NULL,
    PhoneNumber nvarchar(max) NULL,
    PhoneNumberConfirmed bit NOT NULL CONSTRAINT DF_AspNetUsers_PhoneConfirmed DEFAULT 0,
    TwoFactorEnabled bit NOT NULL CONSTRAINT DF_AspNetUsers_TwoFactor DEFAULT 0,
    LockoutEnd datetimeoffset NULL,
    LockoutEnabled bit NOT NULL CONSTRAINT DF_AspNetUsers_LockoutEnabled DEFAULT 1,
    AccessFailedCount int NOT NULL CONSTRAINT DF_AspNetUsers_AccessFailed DEFAULT 0
);
GO
CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers(NormalizedUserName)
WHERE NormalizedUserName IS NOT NULL;
GO
CREATE INDEX EmailIndex ON dbo.AspNetUsers(NormalizedEmail);
GO

CREATE TABLE dbo.AspNetRoleClaims (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY,
    RoleId nvarchar(450) NOT NULL,
    ClaimType nvarchar(max) NULL,
    ClaimValue nvarchar(max) NULL,
    CONSTRAINT FK_AspNetRoleClaims_Roles FOREIGN KEY (RoleId)
        REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE
);
GO
CREATE TABLE dbo.AspNetUserClaims (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetUserClaims PRIMARY KEY,
    UserId nvarchar(450) NOT NULL,
    ClaimType nvarchar(max) NULL,
    ClaimValue nvarchar(max) NULL,
    CONSTRAINT FK_AspNetUserClaims_Users FOREIGN KEY (UserId)
        REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
);
GO
CREATE TABLE dbo.AspNetUserLogins (
    LoginProvider nvarchar(450) NOT NULL,
    ProviderKey nvarchar(450) NOT NULL,
    ProviderDisplayName nvarchar(max) NULL,
    UserId nvarchar(450) NOT NULL,
    CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT FK_AspNetUserLogins_Users FOREIGN KEY (UserId)
        REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
);
GO
CREATE TABLE dbo.AspNetUserRoles (
    UserId nvarchar(450) NOT NULL,
    RoleId nvarchar(450) NOT NULL,
    CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_AspNetUserRoles_Users FOREIGN KEY (UserId)
        REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
    CONSTRAINT FK_AspNetUserRoles_Roles FOREIGN KEY (RoleId)
        REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE
);
GO
CREATE TABLE dbo.AspNetUserTokens (
    UserId nvarchar(450) NOT NULL,
    LoginProvider nvarchar(450) NOT NULL,
    Name nvarchar(450) NOT NULL,
    Value nvarchar(max) NULL,
    CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT FK_AspNetUserTokens_Users FOREIGN KEY (UserId)
        REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
);
GO

/* 2. Organization, employees and fine-grained permissions */
CREATE TABLE dbo.Employees (
    EmployeeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    UserId nvarchar(450) NULL,
    EmployeeCode varchar(30) NOT NULL,
    FullName nvarchar(150) NOT NULL,
    DateOfBirth date NULL,
    Gender tinyint NULL,
    Phone varchar(20) NULL,
    Email varchar(256) NULL,
    Address nvarchar(300) NULL,
    JobTitle nvarchar(100) NULL,
    HireDate date NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT 1,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Employees_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Employees_Code UNIQUE(EmployeeCode),
    CONSTRAINT UQ_Employees_User UNIQUE(UserId),
    CONSTRAINT FK_Employees_AspNetUsers FOREIGN KEY(UserId) REFERENCES dbo.AspNetUsers(Id)
);
GO
CREATE TABLE dbo.Permissions (
    PermissionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY,
    PermissionCode varchar(80) NOT NULL,
    PermissionName nvarchar(150) NOT NULL,
    ModuleName nvarchar(80) NOT NULL,
    Description nvarchar(300) NULL,
    CONSTRAINT UQ_Permissions_Code UNIQUE(PermissionCode)
);
GO
CREATE TABLE dbo.RolePermissions (
    RoleId nvarchar(450) NOT NULL,
    PermissionId int NOT NULL,
    CanView bit NOT NULL CONSTRAINT DF_RolePermissions_View DEFAULT 0,
    CanCreate bit NOT NULL CONSTRAINT DF_RolePermissions_Create DEFAULT 0,
    CanUpdate bit NOT NULL CONSTRAINT DF_RolePermissions_Update DEFAULT 0,
    CanDelete bit NOT NULL CONSTRAINT DF_RolePermissions_Delete DEFAULT 0,
    CONSTRAINT PK_RolePermissions PRIMARY KEY(RoleId, PermissionId),
    CONSTRAINT FK_RolePermissions_Roles FOREIGN KEY(RoleId) REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE,
    CONSTRAINT FK_RolePermissions_Permissions FOREIGN KEY(PermissionId) REFERENCES dbo.Permissions(PermissionId) ON DELETE CASCADE
);
GO

/* 3. Master data */
CREATE TABLE dbo.Categories (
    CategoryId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
    CategoryCode varchar(30) NOT NULL,
    CategoryName nvarchar(120) NOT NULL,
    Description nvarchar(500) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Categories_IsActive DEFAULT 1,
    CONSTRAINT UQ_Categories_Code UNIQUE(CategoryCode)
);
GO
CREATE TABLE dbo.Units (
    UnitId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Units PRIMARY KEY,
    UnitCode varchar(20) NOT NULL,
    UnitName nvarchar(50) NOT NULL,
    CONSTRAINT UQ_Units_Code UNIQUE(UnitCode)
);
GO
CREATE TABLE dbo.Manufacturers (
    ManufacturerId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Manufacturers PRIMARY KEY,
    ManufacturerName nvarchar(200) NOT NULL,
    Country nvarchar(100) NULL,
    Address nvarchar(300) NULL,
    Phone varchar(20) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Manufacturers_IsActive DEFAULT 1
);
GO
CREATE TABLE dbo.ActiveIngredients (
    IngredientId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ActiveIngredients PRIMARY KEY,
    IngredientName nvarchar(200) NOT NULL,
    Description nvarchar(1000) NULL,
    CONSTRAINT UQ_ActiveIngredients_Name UNIQUE(IngredientName)
);
GO
CREATE TABLE dbo.Medicines (
    MedicineId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Medicines PRIMARY KEY,
    MedicineCode varchar(40) NOT NULL,
    Barcode varchar(50) NULL,
    MedicineName nvarchar(250) NOT NULL,
    CategoryId int NOT NULL,
    ManufacturerId int NULL,
    BaseUnitId int NOT NULL,
    Strength nvarchar(100) NULL,
    DosageForm nvarchar(100) NULL,
    RegistrationNumber varchar(80) NULL,
    CountryOfOrigin nvarchar(100) NULL,
    DefaultPurchasePrice decimal(18,2) NOT NULL CONSTRAINT DF_Medicines_PurchasePrice DEFAULT 0,
    DefaultSellingPrice decimal(18,2) NOT NULL CONSTRAINT DF_Medicines_SellingPrice DEFAULT 0,
    IsPrescription bit NOT NULL CONSTRAINT DF_Medicines_IsPrescription DEFAULT 0,
    IsControlledDrug bit NOT NULL CONSTRAINT DF_Medicines_IsControlled DEFAULT 0,
    IsInterconnected bit NOT NULL CONSTRAINT DF_Medicines_IsInterconnected DEFAULT 0,
    UsageInstructions nvarchar(1000) NULL,
    Contraindications nvarchar(1000) NULL,
    Interactions nvarchar(1000) NULL,
    ImageUrl nvarchar(500) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Medicines_IsActive DEFAULT 1,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Medicines_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Medicines_Code UNIQUE(MedicineCode),
    CONSTRAINT FK_Medicines_Categories FOREIGN KEY(CategoryId) REFERENCES dbo.Categories(CategoryId),
    CONSTRAINT FK_Medicines_Manufacturers FOREIGN KEY(ManufacturerId) REFERENCES dbo.Manufacturers(ManufacturerId),
    CONSTRAINT FK_Medicines_Units FOREIGN KEY(BaseUnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_Medicines_Prices CHECK(DefaultPurchasePrice >= 0 AND DefaultSellingPrice >= 0)
);
GO
CREATE UNIQUE INDEX UX_Medicines_Barcode ON dbo.Medicines(Barcode) WHERE Barcode IS NOT NULL;
GO
CREATE TABLE dbo.MedicineIngredients (
    MedicineId int NOT NULL,
    IngredientId int NOT NULL,
    IngredientStrength nvarchar(100) NULL,
    CONSTRAINT PK_MedicineIngredients PRIMARY KEY(MedicineId, IngredientId),
    CONSTRAINT FK_MedicineIngredients_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId) ON DELETE CASCADE,
    CONSTRAINT FK_MedicineIngredients_Ingredients FOREIGN KEY(IngredientId) REFERENCES dbo.ActiveIngredients(IngredientId)
);
GO
CREATE TABLE dbo.MedicineUnitConversions (
    ConversionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MedicineUnitConversions PRIMARY KEY,
    MedicineId int NOT NULL,
    UnitId int NOT NULL,
    ConversionToBase decimal(18,6) NOT NULL,
    SellingPrice decimal(18,2) NULL,
    IsDefaultSaleUnit bit NOT NULL CONSTRAINT DF_MedicineUnitConversions_Default DEFAULT 0,
    CONSTRAINT UQ_MedicineUnitConversions UNIQUE(MedicineId, UnitId),
    CONSTRAINT FK_MedicineUnitConversions_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId) ON DELETE CASCADE,
    CONSTRAINT FK_MedicineUnitConversions_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_MedicineUnitConversions_Ratio CHECK(ConversionToBase > 0),
    CONSTRAINT CK_MedicineUnitConversions_Price CHECK(SellingPrice IS NULL OR SellingPrice >= 0)
);
GO

/* 4. Suppliers, customers and allergy information */
CREATE TABLE dbo.Suppliers (
    SupplierId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY,
    SupplierCode varchar(30) NOT NULL,
    SupplierName nvarchar(200) NOT NULL,
    Phone varchar(20) NULL,
    Email varchar(256) NULL,
    TaxCode varchar(30) NULL,
    Address nvarchar(300) NULL,
    ContactPerson nvarchar(150) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Suppliers_IsActive DEFAULT 1,
    CONSTRAINT UQ_Suppliers_Code UNIQUE(SupplierCode)
);
GO
CREATE TABLE dbo.Customers (
    CustomerId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    CustomerCode varchar(30) NOT NULL,
    FullName nvarchar(150) NOT NULL,
    Phone varchar(20) NULL,
    Email varchar(256) NULL,
    DateOfBirth date NULL,
    Gender tinyint NULL,
    Address nvarchar(300) NULL,
    ChronicConditions nvarchar(500) NULL,
    Notes nvarchar(1000) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT 1,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Customers_Code UNIQUE(CustomerCode)
);
GO
CREATE INDEX IX_Customers_Phone ON dbo.Customers(Phone);
GO
CREATE TABLE dbo.CustomerAllergies (
    CustomerAllergyId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerAllergies PRIMARY KEY,
    CustomerId int NOT NULL,
    IngredientId int NOT NULL,
    ReactionDescription nvarchar(500) NULL,
    RecordedAt datetime2(0) NOT NULL CONSTRAINT DF_CustomerAllergies_Recorded DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CustomerAllergies UNIQUE(CustomerId, IngredientId),
    CONSTRAINT FK_CustomerAllergies_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(CustomerId) ON DELETE CASCADE,
    CONSTRAINT FK_CustomerAllergies_Ingredients FOREIGN KEY(IngredientId) REFERENCES dbo.ActiveIngredients(IngredientId)
);
GO

/* 5. Warehouse and storage locations */
CREATE TABLE dbo.Warehouses (
    WarehouseId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
    WarehouseCode varchar(30) NOT NULL,
    WarehouseName nvarchar(150) NOT NULL,
    Address nvarchar(300) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Warehouses_IsActive DEFAULT 1,
    CONSTRAINT UQ_Warehouses_Code UNIQUE(WarehouseCode)
);
GO
CREATE TABLE dbo.StorageLocations (
    LocationId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StorageLocations PRIMARY KEY,
    WarehouseId int NOT NULL,
    LocationCode varchar(30) NOT NULL,
    LocationName nvarchar(100) NOT NULL,
    CabinetName nvarchar(100) NULL,
    ShelfName nvarchar(100) NULL,
    Description nvarchar(300) NULL,
    CONSTRAINT UQ_StorageLocations UNIQUE(WarehouseId, LocationCode),
    CONSTRAINT FK_StorageLocations_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId)
);
GO

/* 6. Purchase orders and goods receiving */
CREATE TABLE dbo.PurchaseOrders (
    PurchaseOrderId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseOrders PRIMARY KEY,
    PurchaseOrderCode varchar(30) NOT NULL,
    SupplierId int NOT NULL,
    CreatedByEmployeeId int NOT NULL,
    OrderDate datetime2(0) NOT NULL CONSTRAINT DF_PurchaseOrders_OrderDate DEFAULT SYSDATETIME(),
    ExpectedDeliveryDate date NULL,
    Status tinyint NOT NULL CONSTRAINT DF_PurchaseOrders_Status DEFAULT 0,
    Notes nvarchar(1000) NULL,
    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_PurchaseOrders_Total DEFAULT 0,
    CONSTRAINT UQ_PurchaseOrders_Code UNIQUE(PurchaseOrderCode),
    CONSTRAINT FK_PurchaseOrders_Suppliers FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(SupplierId),
    CONSTRAINT FK_PurchaseOrders_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_PurchaseOrders_Total CHECK(TotalAmount >= 0)
);
GO
CREATE TABLE dbo.PurchaseOrderDetails (
    PurchaseOrderDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseOrderDetails PRIMARY KEY,
    PurchaseOrderId int NOT NULL,
    MedicineId int NOT NULL,
    OrderedQuantity decimal(18,3) NOT NULL,
    UnitId int NOT NULL,
    ExpectedUnitPrice decimal(18,2) NOT NULL,
    ReceivedQuantity decimal(18,3) NOT NULL CONSTRAINT DF_PurchaseOrderDetails_Received DEFAULT 0,
    CONSTRAINT FK_PurchaseOrderDetails_Orders FOREIGN KEY(PurchaseOrderId) REFERENCES dbo.PurchaseOrders(PurchaseOrderId) ON DELETE CASCADE,
    CONSTRAINT FK_PurchaseOrderDetails_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_PurchaseOrderDetails_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_PurchaseOrderDetails_Qty CHECK(OrderedQuantity > 0 AND ReceivedQuantity >= 0),
    CONSTRAINT CK_PurchaseOrderDetails_Price CHECK(ExpectedUnitPrice >= 0)
);
GO
CREATE TABLE dbo.GoodsReceipts (
    GoodsReceiptId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_GoodsReceipts PRIMARY KEY,
    GoodsReceiptCode varchar(30) NOT NULL,
    PurchaseOrderId int NULL,
    SupplierId int NOT NULL,
    ReceivedByEmployeeId int NOT NULL,
    ReceiptDate datetime2(0) NOT NULL CONSTRAINT DF_GoodsReceipts_Date DEFAULT SYSDATETIME(),
    SupplierInvoiceNumber varchar(50) NULL,
    Status tinyint NOT NULL CONSTRAINT DF_GoodsReceipts_Status DEFAULT 0,
    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_GoodsReceipts_Total DEFAULT 0,
    Notes nvarchar(1000) NULL,
    CONSTRAINT UQ_GoodsReceipts_Code UNIQUE(GoodsReceiptCode),
    CONSTRAINT FK_GoodsReceipts_Orders FOREIGN KEY(PurchaseOrderId) REFERENCES dbo.PurchaseOrders(PurchaseOrderId),
    CONSTRAINT FK_GoodsReceipts_Suppliers FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(SupplierId),
    CONSTRAINT FK_GoodsReceipts_Employees FOREIGN KEY(ReceivedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_GoodsReceipts_Total CHECK(TotalAmount >= 0)
);
GO

/* 7. Batch inventory */
CREATE TABLE dbo.MedicineBatches (
    BatchId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MedicineBatches PRIMARY KEY,
    MedicineId int NOT NULL,
    GoodsReceiptId int NULL,
    GoodsReceiptDetailId int NULL,
    WarehouseId int NOT NULL,
    LocationId int NULL,
    BatchNumber varchar(60) NOT NULL,
    ManufactureDate date NULL,
    ExpiryDate date NOT NULL,
    ReceivedDate date NOT NULL,
    UnitCost decimal(18,2) NOT NULL,
    QuantityOnHand decimal(18,3) NOT NULL CONSTRAINT DF_MedicineBatches_Qty DEFAULT 0,
    Status tinyint NOT NULL CONSTRAINT DF_MedicineBatches_Status DEFAULT 1,
    Notes nvarchar(500) NULL,
    CONSTRAINT FK_MedicineBatches_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_MedicineBatches_Receipts FOREIGN KEY(GoodsReceiptId) REFERENCES dbo.GoodsReceipts(GoodsReceiptId),
    CONSTRAINT FK_MedicineBatches_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_MedicineBatches_Locations FOREIGN KEY(LocationId) REFERENCES dbo.StorageLocations(LocationId),
    CONSTRAINT UQ_MedicineBatches_MedicineBatch UNIQUE(MedicineId, BatchNumber, WarehouseId),
    CONSTRAINT CK_MedicineBatches_Qty CHECK(QuantityOnHand >= 0),
    CONSTRAINT CK_MedicineBatches_Cost CHECK(UnitCost >= 0),
    CONSTRAINT CK_MedicineBatches_Dates CHECK(ExpiryDate > ReceivedDate)
);
GO
CREATE INDEX IX_MedicineBatches_FEFO ON dbo.MedicineBatches(MedicineId, ExpiryDate, QuantityOnHand);
GO
CREATE TABLE dbo.GoodsReceiptDetails (
    GoodsReceiptDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_GoodsReceiptDetails PRIMARY KEY,
    GoodsReceiptId int NOT NULL,
    PurchaseOrderDetailId int NULL,
    MedicineId int NOT NULL,
    BatchId int NULL,
    UnitId int NOT NULL,
    QuantityReceived decimal(18,3) NOT NULL,
    UnitCost decimal(18,2) NOT NULL,
    DamagedQuantity decimal(18,3) NOT NULL CONSTRAINT DF_GoodsReceiptDetails_Damaged DEFAULT 0,
    SubTotal AS (QuantityReceived * UnitCost) PERSISTED,
    QualityNote nvarchar(500) NULL,
    CONSTRAINT FK_GoodsReceiptDetails_Receipts FOREIGN KEY(GoodsReceiptId) REFERENCES dbo.GoodsReceipts(GoodsReceiptId) ON DELETE CASCADE,
    CONSTRAINT FK_GoodsReceiptDetails_OrderDetails FOREIGN KEY(PurchaseOrderDetailId) REFERENCES dbo.PurchaseOrderDetails(PurchaseOrderDetailId),
    CONSTRAINT FK_GoodsReceiptDetails_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_GoodsReceiptDetails_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT FK_GoodsReceiptDetails_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_GoodsReceiptDetails_Qty CHECK(QuantityReceived > 0 AND DamagedQuantity >= 0 AND DamagedQuantity <= QuantityReceived),
    CONSTRAINT CK_GoodsReceiptDetails_Cost CHECK(UnitCost >= 0)
);
GO
ALTER TABLE dbo.MedicineBatches
ADD CONSTRAINT FK_MedicineBatches_ReceiptDetails FOREIGN KEY(GoodsReceiptDetailId)
REFERENCES dbo.GoodsReceiptDetails(GoodsReceiptDetailId);
GO

/* 8. Inventory ledger and stocktaking */
CREATE TABLE dbo.InventoryTransactions (
    InventoryTransactionId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryTransactions PRIMARY KEY,
    MedicineId int NOT NULL,
    BatchId int NOT NULL,
    WarehouseId int NOT NULL,
    TransactionType tinyint NOT NULL,
    QuantityChange decimal(18,3) NOT NULL,
    ReferenceType varchar(30) NOT NULL,
    ReferenceId bigint NOT NULL,
    TransactionDate datetime2(0) NOT NULL CONSTRAINT DF_InventoryTransactions_Date DEFAULT SYSDATETIME(),
    CreatedByEmployeeId int NULL,
    Notes nvarchar(500) NULL,
    CONSTRAINT FK_InventoryTransactions_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_InventoryTransactions_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT FK_InventoryTransactions_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_InventoryTransactions_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_InventoryTransactions_NonZero CHECK(QuantityChange <> 0)
);
GO
CREATE INDEX IX_InventoryTransactions_MedicineDate ON dbo.InventoryTransactions(MedicineId, TransactionDate);
GO
CREATE TABLE dbo.Stocktakes (
    StocktakeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Stocktakes PRIMARY KEY,
    StocktakeCode varchar(30) NOT NULL,
    WarehouseId int NOT NULL,
    CreatedByEmployeeId int NOT NULL,
    ApprovedByEmployeeId int NULL,
    StocktakeDate datetime2(0) NOT NULL CONSTRAINT DF_Stocktakes_Date DEFAULT SYSDATETIME(),
    Status tinyint NOT NULL CONSTRAINT DF_Stocktakes_Status DEFAULT 0,
    Notes nvarchar(1000) NULL,
    CONSTRAINT UQ_Stocktakes_Code UNIQUE(StocktakeCode),
    CONSTRAINT FK_Stocktakes_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_Stocktakes_Creators FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT FK_Stocktakes_Approvers FOREIGN KEY(ApprovedByEmployeeId) REFERENCES dbo.Employees(EmployeeId)
);
GO
CREATE TABLE dbo.StocktakeDetails (
    StocktakeDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StocktakeDetails PRIMARY KEY,
    StocktakeId int NOT NULL,
    BatchId int NOT NULL,
    SystemQuantity decimal(18,3) NOT NULL,
    ActualQuantity decimal(18,3) NOT NULL,
    Difference AS (ActualQuantity - SystemQuantity) PERSISTED,
    Reason nvarchar(500) NULL,
    CONSTRAINT UQ_StocktakeDetails UNIQUE(StocktakeId, BatchId),
    CONSTRAINT FK_StocktakeDetails_Stocktakes FOREIGN KEY(StocktakeId) REFERENCES dbo.Stocktakes(StocktakeId) ON DELETE CASCADE,
    CONSTRAINT FK_StocktakeDetails_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT CK_StocktakeDetails_Qty CHECK(SystemQuantity >= 0 AND ActualQuantity >= 0)
);
GO

/* 9. Shifts */
CREATE TABLE dbo.Shifts (
    ShiftId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Shifts PRIMARY KEY,
    ShiftCode varchar(30) NOT NULL,
    EmployeeId int NOT NULL,
    OpenedAt datetime2(0) NOT NULL CONSTRAINT DF_Shifts_Opened DEFAULT SYSDATETIME(),
    OpeningCash decimal(18,2) NOT NULL CONSTRAINT DF_Shifts_OpeningCash DEFAULT 0,
    ClosedAt datetime2(0) NULL,
    Status tinyint NOT NULL CONSTRAINT DF_Shifts_Status DEFAULT 0,
    HandoverNote nvarchar(1000) NULL,
    CONSTRAINT UQ_Shifts_Code UNIQUE(ShiftCode),
    CONSTRAINT FK_Shifts_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_Shifts_Cash CHECK(OpeningCash >= 0)
);
GO
CREATE TABLE dbo.ShiftClosings (
    ShiftClosingId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ShiftClosings PRIMARY KEY,
    ShiftId int NOT NULL,
    ClosedByEmployeeId int NOT NULL,
    ExpectedCash decimal(18,2) NOT NULL,
    ActualCash decimal(18,2) NOT NULL,
    CashDifference AS (ActualCash - ExpectedCash) PERSISTED,
    TotalNonCash decimal(18,2) NOT NULL CONSTRAINT DF_ShiftClosings_NonCash DEFAULT 0,
    ClosedAt datetime2(0) NOT NULL CONSTRAINT DF_ShiftClosings_Date DEFAULT SYSDATETIME(),
    Notes nvarchar(1000) NULL,
    CONSTRAINT UQ_ShiftClosings_Shift UNIQUE(ShiftId),
    CONSTRAINT FK_ShiftClosings_Shifts FOREIGN KEY(ShiftId) REFERENCES dbo.Shifts(ShiftId),
    CONSTRAINT FK_ShiftClosings_Employees FOREIGN KEY(ClosedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_ShiftClosings_Amounts CHECK(ExpectedCash >= 0 AND ActualCash >= 0 AND TotalNonCash >= 0)
);
GO

/* 10. Prescriptions */
CREATE TABLE dbo.Prescriptions (
    PrescriptionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Prescriptions PRIMARY KEY,
    PrescriptionCode varchar(30) NOT NULL,
    CustomerId int NULL,
    DoctorName nvarchar(150) NULL,
    ClinicName nvarchar(200) NULL,
    Diagnosis nvarchar(500) NULL,
    PrescriptionDate date NULL,
    ImagePath nvarchar(500) NULL,
    CheckedByEmployeeId int NULL,
    Status tinyint NOT NULL CONSTRAINT DF_Prescriptions_Status DEFAULT 0,
    Notes nvarchar(1000) NULL,
    CONSTRAINT UQ_Prescriptions_Code UNIQUE(PrescriptionCode),
    CONSTRAINT FK_Prescriptions_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(CustomerId),
    CONSTRAINT FK_Prescriptions_Employees FOREIGN KEY(CheckedByEmployeeId) REFERENCES dbo.Employees(EmployeeId)
);
GO
CREATE TABLE dbo.PrescriptionDetails (
    PrescriptionDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PrescriptionDetails PRIMARY KEY,
    PrescriptionId int NOT NULL,
    MedicineId int NOT NULL,
    PrescribedQuantity decimal(18,3) NOT NULL,
    UnitId int NOT NULL,
    Dosage nvarchar(300) NULL,
    Frequency nvarchar(200) NULL,
    DurationDays int NULL,
    Instructions nvarchar(500) NULL,
    CONSTRAINT FK_PrescriptionDetails_Prescriptions FOREIGN KEY(PrescriptionId) REFERENCES dbo.Prescriptions(PrescriptionId) ON DELETE CASCADE,
    CONSTRAINT FK_PrescriptionDetails_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_PrescriptionDetails_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_PrescriptionDetails_Qty CHECK(PrescribedQuantity > 0),
    CONSTRAINT CK_PrescriptionDetails_Duration CHECK(DurationDays IS NULL OR DurationDays > 0)
);
GO

/* 11. Sales invoices, lines, batch allocations and payments */
CREATE TABLE dbo.SalesInvoices (
    InvoiceId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoices PRIMARY KEY,
    InvoiceCode varchar(30) NOT NULL,
    CustomerId int NULL,
    EmployeeId int NOT NULL,
    ShiftId int NULL,
    PrescriptionId int NULL,
    InvoiceDate datetime2(0) NOT NULL CONSTRAINT DF_SalesInvoices_Date DEFAULT SYSDATETIME(),
    SubTotal decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_SubTotal DEFAULT 0,
    DiscountAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_Discount DEFAULT 0,
    TaxAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_Tax DEFAULT 0,
    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_Total DEFAULT 0,
    Status tinyint NOT NULL CONSTRAINT DF_SalesInvoices_Status DEFAULT 0,
    Notes nvarchar(1000) NULL,
    CONSTRAINT UQ_SalesInvoices_Code UNIQUE(InvoiceCode),
    CONSTRAINT FK_SalesInvoices_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(CustomerId),
    CONSTRAINT FK_SalesInvoices_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT FK_SalesInvoices_Shifts FOREIGN KEY(ShiftId) REFERENCES dbo.Shifts(ShiftId),
    CONSTRAINT FK_SalesInvoices_Prescriptions FOREIGN KEY(PrescriptionId) REFERENCES dbo.Prescriptions(PrescriptionId),
    CONSTRAINT CK_SalesInvoices_Amounts CHECK(SubTotal >= 0 AND DiscountAmount >= 0 AND TaxAmount >= 0 AND TotalAmount >= 0)
);
GO
CREATE INDEX IX_SalesInvoices_Date ON dbo.SalesInvoices(InvoiceDate);
GO
CREATE TABLE dbo.SalesInvoiceDetails (
    InvoiceDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoiceDetails PRIMARY KEY,
    InvoiceId int NOT NULL,
    MedicineId int NOT NULL,
    UnitId int NOT NULL,
    Quantity decimal(18,3) NOT NULL,
    ConversionToBase decimal(18,6) NOT NULL,
    UnitPrice decimal(18,2) NOT NULL,
    DiscountAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoiceDetails_Discount DEFAULT 0,
    LineTotal decimal(18,2) NOT NULL,
    CONSTRAINT FK_SalesInvoiceDetails_Invoices FOREIGN KEY(InvoiceId) REFERENCES dbo.SalesInvoices(InvoiceId) ON DELETE CASCADE,
    CONSTRAINT FK_SalesInvoiceDetails_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_SalesInvoiceDetails_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_SalesInvoiceDetails_Qty CHECK(Quantity > 0 AND ConversionToBase > 0),
    CONSTRAINT CK_SalesInvoiceDetails_Amounts CHECK(UnitPrice >= 0 AND DiscountAmount >= 0 AND LineTotal >= 0)
);
GO
CREATE TABLE dbo.SalesIssueAllocations (
    AllocationId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesIssueAllocations PRIMARY KEY,
    InvoiceDetailId int NOT NULL,
    BatchId int NOT NULL,
    QuantityBase decimal(18,3) NOT NULL,
    UnitCostAtSale decimal(18,2) NOT NULL,
    AllocatedAt datetime2(0) NOT NULL CONSTRAINT DF_SalesIssueAllocations_Date DEFAULT SYSDATETIME(),
    CONSTRAINT FK_SalesIssueAllocations_Details FOREIGN KEY(InvoiceDetailId) REFERENCES dbo.SalesInvoiceDetails(InvoiceDetailId),
    CONSTRAINT FK_SalesIssueAllocations_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT CK_SalesIssueAllocations_Qty CHECK(QuantityBase > 0 AND UnitCostAtSale >= 0)
);
GO
CREATE TABLE dbo.Payments (
    PaymentId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
    InvoiceId int NOT NULL,
    PaymentMethod tinyint NOT NULL,
    Amount decimal(18,2) NOT NULL,
    TransactionReference varchar(100) NULL,
    PaidAt datetime2(0) NOT NULL CONSTRAINT DF_Payments_PaidAt DEFAULT SYSDATETIME(),
    Status tinyint NOT NULL CONSTRAINT DF_Payments_Status DEFAULT 1,
    Notes nvarchar(500) NULL,
    CONSTRAINT FK_Payments_Invoices FOREIGN KEY(InvoiceId) REFERENCES dbo.SalesInvoices(InvoiceId),
    CONSTRAINT CK_Payments_Amount CHECK(Amount > 0)
);
GO

/* 12. Returns */
CREATE TABLE dbo.Returns (
    ReturnId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Returns PRIMARY KEY,
    ReturnCode varchar(30) NOT NULL,
    InvoiceId int NOT NULL,
    ProcessedByEmployeeId int NOT NULL,
    ReturnDate datetime2(0) NOT NULL CONSTRAINT DF_Returns_Date DEFAULT SYSDATETIME(),
    Reason nvarchar(500) NOT NULL,
    RefundAmount decimal(18,2) NOT NULL CONSTRAINT DF_Returns_Refund DEFAULT 0,
    Status tinyint NOT NULL CONSTRAINT DF_Returns_Status DEFAULT 0,
    CONSTRAINT UQ_Returns_Code UNIQUE(ReturnCode),
    CONSTRAINT FK_Returns_Invoices FOREIGN KEY(InvoiceId) REFERENCES dbo.SalesInvoices(InvoiceId),
    CONSTRAINT FK_Returns_Employees FOREIGN KEY(ProcessedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_Returns_Refund CHECK(RefundAmount >= 0)
);
GO
CREATE TABLE dbo.ReturnDetails (
    ReturnDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReturnDetails PRIMARY KEY,
    ReturnId int NOT NULL,
    InvoiceDetailId int NOT NULL,
    BatchId int NULL,
    Quantity decimal(18,3) NOT NULL,
    UnitRefundPrice decimal(18,2) NOT NULL,
    RestockStatus tinyint NOT NULL CONSTRAINT DF_ReturnDetails_Restock DEFAULT 0,
    ConditionNote nvarchar(500) NULL,
    CONSTRAINT FK_ReturnDetails_Returns FOREIGN KEY(ReturnId) REFERENCES dbo.Returns(ReturnId) ON DELETE CASCADE,
    CONSTRAINT FK_ReturnDetails_InvoiceDetails FOREIGN KEY(InvoiceDetailId) REFERENCES dbo.SalesInvoiceDetails(InvoiceDetailId),
    CONSTRAINT FK_ReturnDetails_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT CK_ReturnDetails_Qty CHECK(Quantity > 0 AND UnitRefundPrice >= 0)
);
GO


/* 13. Supplier returns and supplier settlements */
CREATE TABLE dbo.SupplierReturns (
    SupplierReturnId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierReturns PRIMARY KEY,
    SupplierReturnCode varchar(30) NOT NULL,
    SupplierId int NOT NULL,
    GoodsReceiptId int NULL,
    CreatedByEmployeeId int NOT NULL,
    ReturnDate datetime2(0) NOT NULL CONSTRAINT DF_SupplierReturns_Date DEFAULT SYSDATETIME(),
    Reason nvarchar(500) NOT NULL,
    Status tinyint NOT NULL CONSTRAINT DF_SupplierReturns_Status DEFAULT 0,
    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_SupplierReturns_Total DEFAULT 0,
    CONSTRAINT UQ_SupplierReturns_Code UNIQUE(SupplierReturnCode),
    CONSTRAINT FK_SupplierReturns_Suppliers FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(SupplierId),
    CONSTRAINT FK_SupplierReturns_Receipts FOREIGN KEY(GoodsReceiptId) REFERENCES dbo.GoodsReceipts(GoodsReceiptId),
    CONSTRAINT FK_SupplierReturns_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_SupplierReturns_Total CHECK(TotalAmount >= 0)
);
GO
CREATE TABLE dbo.SupplierReturnDetails (
    SupplierReturnDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierReturnDetails PRIMARY KEY,
    SupplierReturnId int NOT NULL,
    BatchId int NOT NULL,
    Quantity decimal(18,3) NOT NULL,
    UnitCost decimal(18,2) NOT NULL,
    LineTotal AS (Quantity * UnitCost) PERSISTED,
    Reason nvarchar(500) NULL,
    CONSTRAINT FK_SupplierReturnDetails_Returns FOREIGN KEY(SupplierReturnId) REFERENCES dbo.SupplierReturns(SupplierReturnId) ON DELETE CASCADE,
    CONSTRAINT FK_SupplierReturnDetails_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT CK_SupplierReturnDetails_Qty CHECK(Quantity > 0 AND UnitCost >= 0)
);
GO
CREATE TABLE dbo.SupplierPayments (
    SupplierPaymentId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierPayments PRIMARY KEY,
    SupplierId int NOT NULL,
    GoodsReceiptId int NULL,
    SupplierReturnId int NULL,
    EmployeeId int NOT NULL,
    PaymentType tinyint NOT NULL, -- 1 = payment to supplier, 2 = refund received
    PaymentMethod tinyint NOT NULL,
    Amount decimal(18,2) NOT NULL,
    TransactionReference varchar(100) NULL,
    PaidAt datetime2(0) NOT NULL CONSTRAINT DF_SupplierPayments_Date DEFAULT SYSDATETIME(),
    Status tinyint NOT NULL CONSTRAINT DF_SupplierPayments_Status DEFAULT 1,
    Notes nvarchar(500) NULL,
    CONSTRAINT FK_SupplierPayments_Suppliers FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(SupplierId),
    CONSTRAINT FK_SupplierPayments_Receipts FOREIGN KEY(GoodsReceiptId) REFERENCES dbo.GoodsReceipts(GoodsReceiptId),
    CONSTRAINT FK_SupplierPayments_Returns FOREIGN KEY(SupplierReturnId) REFERENCES dbo.SupplierReturns(SupplierReturnId),
    CONSTRAINT FK_SupplierPayments_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_SupplierPayments_Amount CHECK(Amount > 0),
    CONSTRAINT CK_SupplierPayments_Type CHECK(PaymentType IN (1,2))
);
GO

/* 14. Inventory rules, pricing and environment logs */
CREATE TABLE dbo.InventoryPolicies (
    InventoryPolicyId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryPolicies PRIMARY KEY,
    MedicineId int NOT NULL,
    WarehouseId int NOT NULL,
    MinimumQuantity decimal(18,3) NOT NULL CONSTRAINT DF_InventoryPolicies_Min DEFAULT 0,
    ExpiryWarningDays int NOT NULL CONSTRAINT DF_InventoryPolicies_Expiry DEFAULT 90,
    UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_InventoryPolicies_Updated DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_InventoryPolicies UNIQUE(MedicineId, WarehouseId),
    CONSTRAINT FK_InventoryPolicies_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_InventoryPolicies_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT CK_InventoryPolicies_Values CHECK(MinimumQuantity >= 0 AND ExpiryWarningDays >= 0)
);
GO
CREATE TABLE dbo.PricePolicies (
    PricePolicyId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PricePolicies PRIMARY KEY,
    PolicyName nvarchar(150) NOT NULL,
    MaxDiscountPercent decimal(5,2) NOT NULL CONSTRAINT DF_PricePolicies_Discount DEFAULT 0,
    ReturnWithinDays int NOT NULL CONSTRAINT DF_PricePolicies_ReturnDays DEFAULT 0,
    IsActive bit NOT NULL CONSTRAINT DF_PricePolicies_Active DEFAULT 1,
    EffectiveFrom date NULL,
    EffectiveTo date NULL,
    CONSTRAINT CK_PricePolicies_Discount CHECK(MaxDiscountPercent BETWEEN 0 AND 100),
    CONSTRAINT CK_PricePolicies_ReturnDays CHECK(ReturnWithinDays >= 0)
);
GO
CREATE TABLE dbo.MedicinePriceHistories (
    PriceHistoryId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MedicinePriceHistories PRIMARY KEY,
    MedicineId int NOT NULL,
    UnitId int NOT NULL,
    PurchasePrice decimal(18,2) NULL,
    SellingPrice decimal(18,2) NOT NULL,
    EffectiveFrom datetime2(0) NOT NULL,
    EffectiveTo datetime2(0) NULL,
    ChangedByEmployeeId int NULL,
    CONSTRAINT FK_MedicinePriceHistories_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_MedicinePriceHistories_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT FK_MedicinePriceHistories_Employees FOREIGN KEY(ChangedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_MedicinePriceHistories_Prices CHECK(SellingPrice >= 0 AND (PurchasePrice IS NULL OR PurchasePrice >= 0))
);
GO
CREATE TABLE dbo.StorageEnvironmentLogs (
    EnvironmentLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StorageEnvironmentLogs PRIMARY KEY,
    WarehouseId int NOT NULL,
    LocationId int NULL,
    RecordedByEmployeeId int NOT NULL,
    RecordedAt datetime2(0) NOT NULL CONSTRAINT DF_StorageEnvironmentLogs_Date DEFAULT SYSDATETIME(),
    TemperatureC decimal(5,2) NULL,
    HumidityPercent decimal(5,2) NULL,
    Notes nvarchar(500) NULL,
    CONSTRAINT FK_StorageEnvironmentLogs_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_StorageEnvironmentLogs_Locations FOREIGN KEY(LocationId) REFERENCES dbo.StorageLocations(LocationId),
    CONSTRAINT FK_StorageEnvironmentLogs_Employees FOREIGN KEY(RecordedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_StorageEnvironmentLogs_Humidity CHECK(HumidityPercent IS NULL OR HumidityPercent BETWEEN 0 AND 100)
);
GO

/* 15. Product recalls and traceability */
CREATE TABLE dbo.ProductRecalls (
    RecallId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductRecalls PRIMARY KEY,
    RecallCode varchar(40) NOT NULL,
    MedicineId int NOT NULL,
    NoticeNumber varchar(80) NULL,
    NoticeDate date NULL,
    Reason nvarchar(1000) NOT NULL,
    Status tinyint NOT NULL CONSTRAINT DF_ProductRecalls_Status DEFAULT 0,
    CreatedByEmployeeId int NOT NULL,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_ProductRecalls_Created DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_ProductRecalls_Code UNIQUE(RecallCode),
    CONSTRAINT FK_ProductRecalls_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_ProductRecalls_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId)
);
GO
CREATE TABLE dbo.RecallBatches (
    RecallBatchId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecallBatches PRIMARY KEY,
    RecallId int NOT NULL,
    BatchId int NOT NULL,
    QuantityOnHandAtRecall decimal(18,3) NOT NULL CONSTRAINT DF_RecallBatches_Qty DEFAULT 0,
    QuantityRecovered decimal(18,3) NOT NULL CONSTRAINT DF_RecallBatches_Recovered DEFAULT 0,
    Notes nvarchar(500) NULL,
    CONSTRAINT UQ_RecallBatches UNIQUE(RecallId, BatchId),
    CONSTRAINT FK_RecallBatches_Recalls FOREIGN KEY(RecallId) REFERENCES dbo.ProductRecalls(RecallId) ON DELETE CASCADE,
    CONSTRAINT FK_RecallBatches_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT CK_RecallBatches_Qty CHECK(QuantityOnHandAtRecall >= 0 AND QuantityRecovered >= 0 AND QuantityRecovered <= QuantityOnHandAtRecall)
);
GO

/* 16. Customer reminders, integration and audit trail */
CREATE TABLE dbo.CustomerReminders (
    ReminderId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerReminders PRIMARY KEY,
    CustomerId int NOT NULL,
    MedicineId int NULL,
    ReminderDate datetime2(0) NOT NULL,
    Message nvarchar(500) NOT NULL,
    Channel tinyint NOT NULL CONSTRAINT DF_CustomerReminders_Channel DEFAULT 0,
    Status tinyint NOT NULL CONSTRAINT DF_CustomerReminders_Status DEFAULT 0,
    SentAt datetime2(0) NULL,
    CreatedByEmployeeId int NULL,
    CONSTRAINT FK_CustomerReminders_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(CustomerId),
    CONSTRAINT FK_CustomerReminders_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_CustomerReminders_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId)
);
GO
CREATE TABLE dbo.IntegrationLogs (
    IntegrationLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationLogs PRIMARY KEY,
    IntegrationType varchar(50) NOT NULL,
    ReferenceType varchar(30) NOT NULL,
    ReferenceId bigint NOT NULL,
    RequestPayload nvarchar(max) NULL,
    ResponsePayload nvarchar(max) NULL,
    Status tinyint NOT NULL CONSTRAINT DF_IntegrationLogs_Status DEFAULT 0,
    RetryCount int NOT NULL CONSTRAINT DF_IntegrationLogs_Retry DEFAULT 0,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_IntegrationLogs_Created DEFAULT SYSDATETIME(),
    LastAttemptAt datetime2(0) NULL,
    ErrorMessage nvarchar(2000) NULL,
    CONSTRAINT CK_IntegrationLogs_Retry CHECK(RetryCount >= 0)
);
GO
CREATE TABLE dbo.AuditLogs (
    AuditLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
    UserId nvarchar(450) NULL,
    ActionType varchar(50) NOT NULL,
    EntityName varchar(100) NOT NULL,
    EntityId nvarchar(100) NULL,
    OldValues nvarchar(max) NULL,
    NewValues nvarchar(max) NULL,
    IpAddress varchar(45) NULL,
    UserAgent nvarchar(500) NULL,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_AuditLogs_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_AuditLogs_Users FOREIGN KEY(UserId) REFERENCES dbo.AspNetUsers(Id)
);
GO
CREATE INDEX IX_AuditLogs_Entity ON dbo.AuditLogs(EntityName, EntityId, CreatedAt);
GO

CREATE INDEX IX_SupplierReturns_Date ON dbo.SupplierReturns(ReturnDate);
CREATE INDEX IX_SupplierPayments_SupplierDate ON dbo.SupplierPayments(SupplierId, PaidAt);
GO

/* Helpful lookup indexes */
CREATE INDEX IX_Medicines_Name ON dbo.Medicines(MedicineName);
CREATE INDEX IX_MedicineBatches_Expiry ON dbo.MedicineBatches(ExpiryDate);
CREATE INDEX IX_PurchaseOrders_Date ON dbo.PurchaseOrders(OrderDate);
CREATE INDEX IX_GoodsReceipts_Date ON dbo.GoodsReceipts(ReceiptDate);
CREATE INDEX IX_Prescriptions_Customer ON dbo.Prescriptions(CustomerId, PrescriptionDate);
CREATE INDEX IX_Payments_Invoice ON dbo.Payments(InvoiceId, PaidAt);
CREATE INDEX IX_Returns_Invoice ON dbo.Returns(InvoiceId, ReturnDate);
GO

/* IMPORTANT BUSINESS RULES TO ENFORCE IN APPLICATION SERVICES:
   1. Post a goods receipt and increase batch stock in one SQL transaction.
   2. Confirm payment, allocate batches by FEFO, write negative inventory
      ledger rows, and update batch quantities in one SQL transaction.
   3. Never issue expired or recalled batches.
   4. Validate total allocated quantity equals invoice quantity converted
      to base units.
   5. Stocktake adjustments require approval and a ledger entry.
   6. Returned medicine must be quarantined/pending inspection before
      it can be added back to sellable stock.
   7. Do not hard-delete posted invoices, receipts or inventory movements.
   8. Do not store plaintext passwords; use ASP.NET Core Identity.
*/
GO
