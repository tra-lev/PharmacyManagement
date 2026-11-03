/* ============================================================================
   PharmacyManagementDB — PHIÊN BẢN 2 (v2)
   Website Quản lý Hiệu thuốc — ASP.NET Core MVC · .NET 10 · EF Core 10
   Phương pháp: DATABASE-FIRST (script này là nguồn gốc duy nhất của schema;
   EF Core chỉ scaffold Entity/DbContext, KHÔNG dùng Migrations).

   - Khớp Chương 2 bản chốt (BFD 78 chức năng lá, Bảng 2.3, SYS-01…SYS-14).
   - Mọi thay đổi so với v1 có nhãn [G-xx] / [DB-xx] / [Q-xx] / [P3]
     và được liệt kê trong CHANGELOG_DB.md. Giá trị trạng thái: ENUMS.md.
   - Chỉ chạy trên DATABASE TRỐNG (SSMS, chạy cả file). Không seed tài khoản
     người dùng: tài khoản Owner đầu tiên tạo bằng ASP.NET Core Identity
     trong ứng dụng (mật khẩu được băm).
   - Ngày giờ: mọi DEFAULT dùng SYSDATETIME() = giờ máy chủ (Việt Nam). [DB-06]
   ============================================================================ */

/* ---- (TÙY CHỌN) Xóa DB cũ — CHỈ dùng trên máy cá nhân, bỏ comment khi cần ----
USE master;
GO
IF DB_ID(N'PharmacyManagementDB') IS NOT NULL
BEGIN
    ALTER DATABASE PharmacyManagementDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE PharmacyManagementDB;
END
GO
-------------------------------------------------------------------------------- */

IF DB_ID(N'PharmacyManagementDB') IS NULL
    CREATE DATABASE PharmacyManagementDB;
GO
USE PharmacyManagementDB;
GO

/* Chặn chạy nhầm trên DB đã có bảng: dừng toàn bộ script */
IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'PharmacyManagementDB đã có bảng. Script v2 chỉ chạy trên database trống.', 16, 1);
    SET NOEXEC ON;
END
GO

/* ============================================================================
   1. ASP.NET CORE IDENTITY (giữ nguyên v1, khóa string)
   ============================================================================ */
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
/* [G-15] 1.3.4 — mỗi tài khoản đúng MỘT vai trò (Owner / Manager / Pharmacist) */
CREATE UNIQUE INDEX UX_AspNetUserRoles_UserId ON dbo.AspNetUserRoles(UserId);
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

/* ============================================================================
   2. NHÂN VIÊN VÀ PHÂN QUYỀN CHI TIẾT (D4)
   ============================================================================ */
CREATE TABLE dbo.Employees (
    EmployeeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    UserId nvarchar(450) NULL,
    EmployeeCode varchar(30) NOT NULL,
    FullName nvarchar(150) NOT NULL,
    DateOfBirth date NULL,
    Gender tinyint NULL,                                   -- [DB-12] 1 Nam, 2 Nữ, 3 Khác
    Phone varchar(20) NULL,
    Email varchar(256) NULL,
    Address nvarchar(300) NULL,
    JobTitle nvarchar(100) NULL,
    HireDate date NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT 1,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Employees_CreatedAt DEFAULT SYSDATETIME(),  -- [DB-06]
    CONSTRAINT UQ_Employees_Code UNIQUE(EmployeeCode),
    CONSTRAINT FK_Employees_AspNetUsers FOREIGN KEY(UserId) REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT CK_Employees_Gender CHECK(Gender IS NULL OR Gender IN (1,2,3))
);
GO
/* [DB-01] Thay UNIQUE(UserId) của v1: cho phép nhiều nhân viên chưa có tài khoản (UserId NULL) */
CREATE UNIQUE INDEX UX_Employees_UserId ON dbo.Employees(UserId) WHERE UserId IS NOT NULL;
GO

CREATE TABLE dbo.Permissions (
    PermissionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY,
    PermissionCode varchar(80) NOT NULL,                   -- 'F' + mã chức năng BFD, ví dụ F3.2.4
    PermissionName nvarchar(150) NOT NULL,
    ModuleName nvarchar(80) NOT NULL,
    Description nvarchar(300) NULL,
    IsSensitive bit NOT NULL CONSTRAINT DF_Permissions_IsSensitive DEFAULT 0,  -- [P3] 1.3.3 quyền thao tác nhạy cảm, SYS-09
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

/* ============================================================================
   3. DANH MỤC THUỐC (D1)
   ============================================================================ */
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
/* [DB-13] Quy ước giá: DefaultSellingPrice = giá bán của ĐƠN VỊ CƠ SỞ (BaseUnitId).
   MedicineUnitConversions chỉ chứa đơn vị KHÁC đơn vị cơ sở.
   DefaultPurchasePrice = giá nhập tham khảo theo đơn vị cơ sở.
   [Q-14] Manager nhập giá ban đầu (1.1.4); đổi giá về sau chỉ Owner (1.5.4) và
   mỗi lần đổi ghi MedicinePriceHistories. */
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
    Contraindications nvarchar(1000) NULL,                 -- mô tả hiển thị; từ khóa kiểm tra ở MedicineContraindications
    Interactions nvarchar(1000) NULL,                      -- mô tả hiển thị; kiểm tra tự động ở IngredientInteractions
    ImageUrl nvarchar(500) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Medicines_IsActive DEFAULT 1,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Medicines_CreatedAt DEFAULT SYSDATETIME(),  -- [DB-06]
    MaxSingleDose decimal(18,3) NULL,                      -- [G-07] liều tối đa 1 lần (đơn vị cơ sở)
    MaxDailyDose decimal(18,3) NULL,                       -- [G-07] liều tối đa 1 ngày (đơn vị cơ sở)
    MinPatientAgeYears tinyint NULL,                       -- [G-07] tuổi tối thiểu (thông tin người bệnh, 3.2.2.2)
    CONSTRAINT UQ_Medicines_Code UNIQUE(MedicineCode),
    CONSTRAINT FK_Medicines_Categories FOREIGN KEY(CategoryId) REFERENCES dbo.Categories(CategoryId),
    CONSTRAINT FK_Medicines_Manufacturers FOREIGN KEY(ManufacturerId) REFERENCES dbo.Manufacturers(ManufacturerId),
    CONSTRAINT FK_Medicines_Units FOREIGN KEY(BaseUnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_Medicines_Prices CHECK(DefaultPurchasePrice >= 0 AND DefaultSellingPrice >= 0),
    CONSTRAINT CK_Medicines_Doses CHECK((MaxSingleDose IS NULL OR MaxSingleDose > 0)
        AND (MaxDailyDose IS NULL OR MaxDailyDose > 0)
        AND (MaxSingleDose IS NULL OR MaxDailyDose IS NULL OR MaxSingleDose <= MaxDailyDose)),
    CONSTRAINT CK_Medicines_MinAge CHECK(MinPatientAgeYears IS NULL OR MinPatientAgeYears <= 120)
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
    UnitId int NOT NULL,                                   -- [DB-13] chỉ đơn vị KHÁC đơn vị cơ sở
    ConversionToBase decimal(18,6) NOT NULL,
    SellingPrice decimal(18,2) NULL,                       -- [DB-13] NULL = DefaultSellingPrice × ConversionToBase
    IsDefaultSaleUnit bit NOT NULL CONSTRAINT DF_MedicineUnitConversions_Default DEFAULT 0,
    CONSTRAINT UQ_MedicineUnitConversions UNIQUE(MedicineId, UnitId),
    CONSTRAINT FK_MedicineUnitConversions_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId) ON DELETE CASCADE,
    CONSTRAINT FK_MedicineUnitConversions_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_MedicineUnitConversions_Ratio CHECK(ConversionToBase > 0),
    CONSTRAINT CK_MedicineUnitConversions_Price CHECK(SellingPrice IS NULL OR SellingPrice >= 0)
);
GO
/* [G-06] 3.1.3.1 — tương tác thuốc theo cặp hoạt chất (Hệ thống cảnh báo, Pharmacist quyết định) */
CREATE TABLE dbo.IngredientInteractions (
    InteractionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_IngredientInteractions PRIMARY KEY,
    IngredientId1 int NOT NULL,
    IngredientId2 int NOT NULL,
    Severity tinyint NOT NULL,                             -- 1 Nhẹ, 2 Trung bình, 3 Nghiêm trọng
    Description nvarchar(1000) NOT NULL,
    Recommendation nvarchar(500) NULL,
    CONSTRAINT UQ_IngredientInteractions_Pair UNIQUE(IngredientId1, IngredientId2),
    CONSTRAINT FK_IngredientInteractions_Ingredient1 FOREIGN KEY(IngredientId1) REFERENCES dbo.ActiveIngredients(IngredientId),
    CONSTRAINT FK_IngredientInteractions_Ingredient2 FOREIGN KEY(IngredientId2) REFERENCES dbo.ActiveIngredients(IngredientId),
    CONSTRAINT CK_IngredientInteractions_Order CHECK(IngredientId1 < IngredientId2),
    CONSTRAINT CK_IngredientInteractions_Severity CHECK(Severity IN (1,2,3))
);
GO
/* [G-08][Q-13] 3.2.2.3 — từ khóa chống chỉ định; so khớp (đã chuẩn hóa hoa/thường, dấu)
   với Prescriptions.Diagnosis và Customers.ChronicConditions. Chỉ cảnh báo. */
CREATE TABLE dbo.MedicineContraindications (
    MedicineContraindicationId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MedicineContraindications PRIMARY KEY,
    MedicineId int NOT NULL,
    ConditionKeyword nvarchar(150) NOT NULL,
    Note nvarchar(500) NULL,
    CONSTRAINT UQ_MedicineContraindications UNIQUE(MedicineId, ConditionKeyword),
    CONSTRAINT FK_MedicineContraindications_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId) ON DELETE CASCADE
);
GO
/* [G-05] 1.5.4 — chương trình khuyến mãi (Owner). MedicineId và CategoryId cùng NULL = toàn bộ thuốc */
CREATE TABLE dbo.Promotions (
    PromotionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Promotions PRIMARY KEY,
    PromotionCode varchar(30) NOT NULL,
    PromotionName nvarchar(200) NOT NULL,
    MedicineId int NULL,
    CategoryId int NULL,
    DiscountPercent decimal(5,2) NULL,
    DiscountAmount decimal(18,2) NULL,
    EffectiveFrom datetime2(0) NOT NULL,
    EffectiveTo datetime2(0) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Promotions_IsActive DEFAULT 1,
    CreatedByEmployeeId int NOT NULL,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Promotions_CreatedAt DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_Promotions_Code UNIQUE(PromotionCode),
    CONSTRAINT FK_Promotions_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_Promotions_Categories FOREIGN KEY(CategoryId) REFERENCES dbo.Categories(CategoryId),
    CONSTRAINT FK_Promotions_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_Promotions_OneDiscount CHECK((DiscountPercent IS NOT NULL AND DiscountAmount IS NULL)
        OR (DiscountPercent IS NULL AND DiscountAmount IS NOT NULL)),
    CONSTRAINT CK_Promotions_Values CHECK((DiscountPercent IS NULL OR DiscountPercent BETWEEN 0 AND 100)
        AND (DiscountAmount IS NULL OR DiscountAmount >= 0)),
    CONSTRAINT CK_Promotions_Dates CHECK(EffectiveTo >= EffectiveFrom),
    CONSTRAINT CK_Promotions_Scope CHECK(MedicineId IS NULL OR CategoryId IS NULL)
);
GO

/* ============================================================================
   4. ĐỐI TÁC (D1) VÀ TIỀN SỬ DỊ ỨNG (D3)
   ============================================================================ */
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
    Gender tinyint NULL,                                   -- [DB-12] 1 Nam, 2 Nữ, 3 Khác
    Address nvarchar(300) NULL,
    ChronicConditions nvarchar(500) NULL,                  -- [Q-13] dùng để so từ khóa chống chỉ định
    Notes nvarchar(1000) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT 1,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSDATETIME(),  -- [DB-06]
    CONSTRAINT UQ_Customers_Code UNIQUE(CustomerCode),
    CONSTRAINT CK_Customers_Gender CHECK(Gender IS NULL OR Gender IN (1,2,3))
);
GO
CREATE INDEX IX_Customers_Phone ON dbo.Customers(Phone);
GO
CREATE TABLE dbo.CustomerAllergies (
    CustomerAllergyId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerAllergies PRIMARY KEY,
    CustomerId int NOT NULL,
    IngredientId int NOT NULL,
    ReactionDescription nvarchar(500) NULL,
    RecordedAt datetime2(0) NOT NULL CONSTRAINT DF_CustomerAllergies_Recorded DEFAULT SYSDATETIME(),  -- [DB-06]
    CONSTRAINT UQ_CustomerAllergies UNIQUE(CustomerId, IngredientId),
    CONSTRAINT FK_CustomerAllergies_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(CustomerId) ON DELETE CASCADE,
    CONSTRAINT FK_CustomerAllergies_Ingredients FOREIGN KEY(IngredientId) REFERENCES dbo.ActiveIngredients(IngredientId)
);
GO

/* ============================================================================
   5. KHO VÀ VỊ TRÍ LƯU TRỮ (D1 — 1.5.1)
   ============================================================================ */
CREATE TABLE dbo.Warehouses (
    WarehouseId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
    WarehouseCode varchar(30) NOT NULL,
    WarehouseName nvarchar(150) NOT NULL,
    Address nvarchar(300) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Warehouses_IsActive DEFAULT 1,
    CONSTRAINT UQ_Warehouses_Code UNIQUE(WarehouseCode)
);
GO
/* [G-03] 1.5.1 "mã, tên tủ; mã, tên kệ và tủ trực thuộc": cây Tủ (1) → Kệ (2).
   Bỏ CabinetName/ShelfName của v1 (trùng thông tin với cây tủ–kệ).
   Lô thuốc (2.2.4) được xếp vào vị trí loại Kệ — kiểm tra ở service. */
CREATE TABLE dbo.StorageLocations (
    LocationId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StorageLocations PRIMARY KEY,
    WarehouseId int NOT NULL,
    LocationCode varchar(30) NOT NULL,
    LocationName nvarchar(100) NOT NULL,
    Description nvarchar(300) NULL,
    LocationType tinyint NOT NULL CONSTRAINT DF_StorageLocations_Type DEFAULT 2,  -- 1 Tủ, 2 Kệ
    ParentLocationId int NULL,
    CONSTRAINT UQ_StorageLocations UNIQUE(WarehouseId, LocationCode),
    CONSTRAINT FK_StorageLocations_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_StorageLocations_Parent FOREIGN KEY(ParentLocationId) REFERENCES dbo.StorageLocations(LocationId),
    CONSTRAINT CK_StorageLocations_Type CHECK(LocationType IN (1,2)),
    CONSTRAINT CK_StorageLocations_Tree CHECK((LocationType = 1 AND ParentLocationId IS NULL)
        OR (LocationType = 2 AND ParentLocationId IS NOT NULL))
);
GO

/* ============================================================================
   6. ĐẶT HÀNG VÀ NHẬP KHO (D2 — 2.1, 2.2)
   ============================================================================ */
CREATE TABLE dbo.PurchaseOrders (
    PurchaseOrderId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseOrders PRIMARY KEY,
    PurchaseOrderCode varchar(30) NOT NULL,
    SupplierId int NOT NULL,
    CreatedByEmployeeId int NOT NULL,
    OrderDate datetime2(0) NOT NULL CONSTRAINT DF_PurchaseOrders_OrderDate DEFAULT SYSDATETIME(),
    ExpectedDeliveryDate date NULL,
    Status tinyint NOT NULL CONSTRAINT DF_PurchaseOrders_Status DEFAULT 0,  -- [DB-12] 0 Nháp, 1 Chờ giao, 2 Giao thiếu, 3 Đã giao, 4 Hủy
    Notes nvarchar(1000) NULL,
    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_PurchaseOrders_Total DEFAULT 0,
    CONSTRAINT UQ_PurchaseOrders_Code UNIQUE(PurchaseOrderCode),
    CONSTRAINT FK_PurchaseOrders_Suppliers FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(SupplierId),
    CONSTRAINT FK_PurchaseOrders_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_PurchaseOrders_Total CHECK(TotalAmount >= 0),
    CONSTRAINT CK_PurchaseOrders_Status CHECK(Status IN (0,1,2,3,4))
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
/* [G-12] Status 0 = đang kiểm nhận (biên bản nhận hàng 2.2.1.3),
          Status 1 = đã lập phiếu nhập kho và cộng tồn (2.2.2–2.2.4), 2 = hủy */
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
    CONSTRAINT CK_GoodsReceipts_Total CHECK(TotalAmount >= 0),
    CONSTRAINT CK_GoodsReceipts_Status CHECK(Status IN (0,1,2))
);
GO
/* [G-12][DB-03] Tạo TRƯỚC MedicineBatches; bỏ cột BatchId (vòng FK của v1).
   QuantityReceived = số lượng thực giao (theo UnitId); DamagedQuantity = hỏng, không nhập kho.
   Số lượng nhập kho (đơn vị cơ sở) = (QuantityReceived − DamagedQuantity) × ConversionToBase.
   BatchNumber/ManufactureDate/ExpiryDate/LocationId nhập ở 2.2.1.2, 2.2.3, 2.2.4;
   bắt buộc khi phiếu chuyển Status = 1 (kiểm tra ở service). */
CREATE TABLE dbo.GoodsReceiptDetails (
    GoodsReceiptDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_GoodsReceiptDetails PRIMARY KEY,
    GoodsReceiptId int NOT NULL,
    PurchaseOrderDetailId int NULL,
    MedicineId int NOT NULL,
    UnitId int NOT NULL,
    ConversionToBase decimal(18,6) NOT NULL CONSTRAINT DF_GoodsReceiptDetails_Conversion DEFAULT 1,
    QuantityReceived decimal(18,3) NOT NULL,
    UnitCost decimal(18,2) NOT NULL,
    DamagedQuantity decimal(18,3) NOT NULL CONSTRAINT DF_GoodsReceiptDetails_Damaged DEFAULT 0,
    SubTotal AS ((QuantityReceived - DamagedQuantity) * UnitCost) PERSISTED,
    QualityNote nvarchar(500) NULL,
    BatchNumber varchar(60) NULL,
    ManufactureDate date NULL,
    ExpiryDate date NULL,
    LocationId int NULL,
    CONSTRAINT FK_GoodsReceiptDetails_Receipts FOREIGN KEY(GoodsReceiptId) REFERENCES dbo.GoodsReceipts(GoodsReceiptId) ON DELETE CASCADE,
    CONSTRAINT FK_GoodsReceiptDetails_OrderDetails FOREIGN KEY(PurchaseOrderDetailId) REFERENCES dbo.PurchaseOrderDetails(PurchaseOrderDetailId),
    CONSTRAINT FK_GoodsReceiptDetails_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_GoodsReceiptDetails_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT FK_GoodsReceiptDetails_Locations FOREIGN KEY(LocationId) REFERENCES dbo.StorageLocations(LocationId),
    CONSTRAINT CK_GoodsReceiptDetails_Qty CHECK(QuantityReceived > 0 AND DamagedQuantity >= 0 AND DamagedQuantity <= QuantityReceived),
    CONSTRAINT CK_GoodsReceiptDetails_Cost CHECK(UnitCost >= 0),
    CONSTRAINT CK_GoodsReceiptDetails_Conversion CHECK(ConversionToBase > 0),
    CONSTRAINT CK_GoodsReceiptDetails_Dates CHECK(ManufactureDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate > ManufactureDate)
);
GO

/* ============================================================================
   7. TỒN KHO THEO LÔ (D2)
   ============================================================================ */
/* [DB-03] Liên kết một chiều Batch → GoodsReceiptDetail.
   [DB-04] Bỏ UNIQUE(MedicineId, BatchNumber, WarehouseId): cùng số lô được nhập ở nhiều phiếu.
   [DB-07] RowVersion + trừ tồn bằng UPDATE có điều kiện trong transaction (xem ghi chú cuối file).
   Quy ước: QuantityOnHand và UnitCost tính theo ĐƠN VỊ CƠ SỞ;
   UnitCost = GoodsReceiptDetails.UnitCost / ConversionToBase. */
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
    Status tinyint NOT NULL CONSTRAINT DF_MedicineBatches_Status DEFAULT 1,  -- [DB-12] 1 Khả dụng, 2 Bị khóa, 3 Đang thu hồi, 4 Ngừng sử dụng
    Notes nvarchar(500) NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT FK_MedicineBatches_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_MedicineBatches_Receipts FOREIGN KEY(GoodsReceiptId) REFERENCES dbo.GoodsReceipts(GoodsReceiptId),
    CONSTRAINT FK_MedicineBatches_ReceiptDetails FOREIGN KEY(GoodsReceiptDetailId) REFERENCES dbo.GoodsReceiptDetails(GoodsReceiptDetailId),
    CONSTRAINT FK_MedicineBatches_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_MedicineBatches_Locations FOREIGN KEY(LocationId) REFERENCES dbo.StorageLocations(LocationId),
    CONSTRAINT CK_MedicineBatches_Qty CHECK(QuantityOnHand >= 0),
    CONSTRAINT CK_MedicineBatches_Cost CHECK(UnitCost >= 0),
    CONSTRAINT CK_MedicineBatches_Dates CHECK(ExpiryDate > ReceivedDate),
    CONSTRAINT CK_MedicineBatches_MfgDate CHECK(ManufactureDate IS NULL OR ManufactureDate < ExpiryDate),
    CONSTRAINT CK_MedicineBatches_Status CHECK(Status IN (1,2,3,4))
);
GO
CREATE UNIQUE INDEX UX_MedicineBatches_ReceiptDetail ON dbo.MedicineBatches(GoodsReceiptDetailId)
WHERE GoodsReceiptDetailId IS NOT NULL;
GO
/* [DB-14] FEFO: thay IX_MedicineBatches_FEFO của v1 */
CREATE INDEX IX_MedicineBatches_FEFO ON dbo.MedicineBatches(MedicineId, Status, ExpiryDate)
INCLUDE (QuantityOnHand, WarehouseId, LocationId);
GO
/* [DB-04] tra cứu số lô khi thu hồi (2.3.3.2) */
CREATE INDEX IX_MedicineBatches_BatchNumber ON dbo.MedicineBatches(BatchNumber, MedicineId);
GO

/* ============================================================================
   8. SỔ BIẾN ĐỘNG KHO, KIỂM KÊ, HỦY THUỐC (D2)
   ============================================================================ */
/* [DB-12] TransactionType ↔ ReferenceType ↔ dấu số lượng:
   1 Nhập mua           GoodsReceiptDetail    > 0
   2 Xuất bán (FEFO)    SalesIssueAllocation  < 0
   3 Trả nhà cung cấp   SupplierReturnDetail  < 0
   4 Hủy thuốc          StockDisposalDetail   < 0
   5 Thu hồi            RecallBatch           < 0
   6 Điều chỉnh kiểm kê StocktakeDetail       ≠ 0
   7 Nhập lại hàng trả  ReturnDetail          > 0 */
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
    CreatedByEmployeeId int NULL,                          -- NULL = Hệ thống
    Notes nvarchar(500) NULL,
    CONSTRAINT FK_InventoryTransactions_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_InventoryTransactions_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT FK_InventoryTransactions_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_InventoryTransactions_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_InventoryTransactions_NonZero CHECK(QuantityChange <> 0),
    CONSTRAINT CK_InventoryTransactions_Type CHECK(TransactionType IN (1,2,3,4,5,6,7)),
    CONSTRAINT CK_InventoryTransactions_TypeRef CHECK(
           (TransactionType = 1 AND ReferenceType = 'GoodsReceiptDetail'   AND QuantityChange > 0)
        OR (TransactionType = 2 AND ReferenceType = 'SalesIssueAllocation' AND QuantityChange < 0)
        OR (TransactionType = 3 AND ReferenceType = 'SupplierReturnDetail' AND QuantityChange < 0)
        OR (TransactionType = 4 AND ReferenceType = 'StockDisposalDetail'  AND QuantityChange < 0)
        OR (TransactionType = 5 AND ReferenceType = 'RecallBatch'          AND QuantityChange < 0)
        OR (TransactionType = 6 AND ReferenceType = 'StocktakeDetail')
        OR (TransactionType = 7 AND ReferenceType = 'ReturnDetail'         AND QuantityChange > 0))
);
GO
CREATE INDEX IX_InventoryTransactions_MedicineDate ON dbo.InventoryTransactions(MedicineId, TransactionDate);
GO

/* [G-04] phạm vi kiểm kê (2.4.1.1); [G-14] QT5: Manager lập → Owner duyệt → Manager điều chỉnh */
CREATE TABLE dbo.Stocktakes (
    StocktakeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Stocktakes PRIMARY KEY,
    StocktakeCode varchar(30) NOT NULL,
    WarehouseId int NOT NULL,
    CreatedByEmployeeId int NOT NULL,
    ApprovedByEmployeeId int NULL,                         -- Owner duyệt / từ chối
    StocktakeDate datetime2(0) NOT NULL CONSTRAINT DF_Stocktakes_Date DEFAULT SYSDATETIME(),
    Status tinyint NOT NULL CONSTRAINT DF_Stocktakes_Status DEFAULT 0,  -- 0 Đang kiểm đếm, 1 Chờ duyệt, 2 Đã duyệt, 3 Đã điều chỉnh, 4 Từ chối
    Notes nvarchar(1000) NULL,
    LocationId int NULL,                                   -- phạm vi: tủ hoặc kệ
    CategoryId int NULL,                                   -- phạm vi: nhóm thuốc
    ApprovedAt datetime2(0) NULL,
    ApprovalNote nvarchar(500) NULL,
    AdjustedByEmployeeId int NULL,                         -- Manager điều chỉnh tồn (2.4.3)
    AdjustedAt datetime2(0) NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT UQ_Stocktakes_Code UNIQUE(StocktakeCode),
    CONSTRAINT FK_Stocktakes_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_Stocktakes_Creators FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT FK_Stocktakes_Approvers FOREIGN KEY(ApprovedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT FK_Stocktakes_Locations FOREIGN KEY(LocationId) REFERENCES dbo.StorageLocations(LocationId),
    CONSTRAINT FK_Stocktakes_Categories FOREIGN KEY(CategoryId) REFERENCES dbo.Categories(CategoryId),
    CONSTRAINT FK_Stocktakes_Adjusters FOREIGN KEY(AdjustedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_Stocktakes_Status CHECK(Status IN (0,1,2,3,4)),
    CONSTRAINT CK_Stocktakes_Approval CHECK(Status NOT IN (2,3,4) OR (ApprovedByEmployeeId IS NOT NULL AND ApprovedAt IS NOT NULL)),
    CONSTRAINT CK_Stocktakes_Reject CHECK(Status <> 4 OR ApprovalNote IS NOT NULL),
    CONSTRAINT CK_Stocktakes_Adjust CHECK(Status <> 3 OR (AdjustedByEmployeeId IS NOT NULL AND AdjustedAt IS NOT NULL))
);
GO
/* Điều chỉnh tồn theo Difference (không ghi đè bằng ActualQuantity) */
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

/* [G-01] 2.3.2 Lập biên bản hủy thuốc (Manager) */
CREATE TABLE dbo.StockDisposals (
    DisposalId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockDisposals PRIMARY KEY,
    DisposalCode varchar(30) NOT NULL,
    WarehouseId int NOT NULL,
    CreatedByEmployeeId int NOT NULL,
    DisposalDate datetime2(0) NOT NULL CONSTRAINT DF_StockDisposals_Date DEFAULT SYSDATETIME(),
    Reason nvarchar(500) NOT NULL,
    Status tinyint NOT NULL CONSTRAINT DF_StockDisposals_Status DEFAULT 0,  -- 0 Nháp, 1 Đã ghi sổ (đã trừ tồn), 2 Hủy
    Notes nvarchar(1000) NULL,
    CONSTRAINT UQ_StockDisposals_Code UNIQUE(DisposalCode),
    CONSTRAINT FK_StockDisposals_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT FK_StockDisposals_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_StockDisposals_Status CHECK(Status IN (0,1,2))
);
GO
CREATE TABLE dbo.StockDisposalDetails (
    DisposalDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockDisposalDetails PRIMARY KEY,
    DisposalId int NOT NULL,
    BatchId int NOT NULL,
    Quantity decimal(18,3) NOT NULL,                       -- đơn vị cơ sở
    UnitCost decimal(18,2) NOT NULL,
    ReasonType tinyint NOT NULL,                           -- 1 Hết hạn, 2 Hỏng, 3 Móp méo, 4 Khác
    ReasonNote nvarchar(500) NULL,
    CONSTRAINT UQ_StockDisposalDetails UNIQUE(DisposalId, BatchId),
    CONSTRAINT FK_StockDisposalDetails_Disposals FOREIGN KEY(DisposalId) REFERENCES dbo.StockDisposals(DisposalId) ON DELETE CASCADE,
    CONSTRAINT FK_StockDisposalDetails_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT CK_StockDisposalDetails_Qty CHECK(Quantity > 0 AND UnitCost >= 0),
    CONSTRAINT CK_StockDisposalDetails_Reason CHECK(ReasonType IN (1,2,3,4))
);
GO

/* ============================================================================
   9. CA LÀM VIỆC (D4 — 1.4)
   ============================================================================ */
/* [DB-10] Mỗi nhân viên chỉ một ca chưa chốt. Người bán (Pharmacist, Manager, Owner)
   tự mở → bàn giao → chốt ca của mình. */
CREATE TABLE dbo.Shifts (
    ShiftId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Shifts PRIMARY KEY,
    ShiftCode varchar(30) NOT NULL,
    EmployeeId int NOT NULL,
    OpenedAt datetime2(0) NOT NULL CONSTRAINT DF_Shifts_Opened DEFAULT SYSDATETIME(),
    OpeningCash decimal(18,2) NOT NULL CONSTRAINT DF_Shifts_OpeningCash DEFAULT 0,
    ClosedAt datetime2(0) NULL,
    Status tinyint NOT NULL CONSTRAINT DF_Shifts_Status DEFAULT 0,  -- 0 Đang mở, 1 Đã bàn giao, 2 Đã chốt
    HandoverNote nvarchar(1000) NULL,
    HandedOverAt datetime2(0) NULL,                        -- 1.4.2
    CONSTRAINT UQ_Shifts_Code UNIQUE(ShiftCode),
    CONSTRAINT FK_Shifts_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_Shifts_Cash CHECK(OpeningCash >= 0),
    CONSTRAINT CK_Shifts_Status CHECK(Status IN (0,1,2)),
    CONSTRAINT CK_Shifts_Lifecycle CHECK((Status = 0 AND ClosedAt IS NULL)
        OR (Status = 1 AND HandedOverAt IS NOT NULL AND ClosedAt IS NULL)
        OR (Status = 2 AND ClosedAt IS NOT NULL))
);
GO
CREATE UNIQUE INDEX UX_Shifts_OpenPerEmployee ON dbo.Shifts(EmployeeId) WHERE ClosedAt IS NULL;
GO
/* [Q-16] 1.4.3 Chốt tiền: ExpectedCash = OpeningCash + tiền mặt bán trong ca − tiền mặt hoàn trong ca.
   OpeningCash được chụp lại từ Shifts để biên bản chốt tiền tự đầy đủ. */
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
    OpeningCash decimal(18,2) NOT NULL CONSTRAINT DF_ShiftClosings_Opening DEFAULT 0,
    TotalCashSales decimal(18,2) NOT NULL CONSTRAINT DF_ShiftClosings_CashSales DEFAULT 0,
    TotalCashRefunds decimal(18,2) NOT NULL CONSTRAINT DF_ShiftClosings_CashRefunds DEFAULT 0,
    CONSTRAINT UQ_ShiftClosings_Shift UNIQUE(ShiftId),
    CONSTRAINT FK_ShiftClosings_Shifts FOREIGN KEY(ShiftId) REFERENCES dbo.Shifts(ShiftId),
    CONSTRAINT FK_ShiftClosings_Employees FOREIGN KEY(ClosedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_ShiftClosings_Amounts CHECK(ExpectedCash >= 0 AND ActualCash >= 0 AND TotalNonCash >= 0
        AND OpeningCash >= 0 AND TotalCashSales >= 0 AND TotalCashRefunds >= 0),
    CONSTRAINT CK_ShiftClosings_Expected CHECK(ExpectedCash = OpeningCash + TotalCashSales - TotalCashRefunds)
);
GO

/* ============================================================================
   10. ĐƠN THUỐC (D3 — 3.2)
   ============================================================================ */
/* Chỉ lưu đơn HỢP LỆ (3.2.3 sau 3.2.2). Thông tin bác sĩ, cơ sở, ngày kê bắt buộc (3.2.2.1);
   khách hàng bắt buộc (sổ theo dõi 4.3.3). */
CREATE TABLE dbo.Prescriptions (
    PrescriptionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Prescriptions PRIMARY KEY,
    PrescriptionCode varchar(30) NOT NULL,
    CustomerId int NOT NULL,
    DoctorName nvarchar(150) NOT NULL,
    ClinicName nvarchar(200) NOT NULL,
    Diagnosis nvarchar(500) NULL,                          -- [Q-13] dùng để so từ khóa chống chỉ định
    PrescriptionDate date NOT NULL,
    ImagePath nvarchar(500) NULL,
    CheckedByEmployeeId int NOT NULL,
    Status tinyint NOT NULL CONSTRAINT DF_Prescriptions_Status DEFAULT 0,  -- 0 Đã lưu (hợp lệ), 1 Đã bán, 2 Hủy
    Notes nvarchar(1000) NULL,
    CONSTRAINT UQ_Prescriptions_Code UNIQUE(PrescriptionCode),
    CONSTRAINT FK_Prescriptions_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(CustomerId),
    CONSTRAINT FK_Prescriptions_Employees FOREIGN KEY(CheckedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_Prescriptions_Status CHECK(Status IN (0,1,2))
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
    DosePerTime decimal(18,3) NULL,                        -- [G-07] liều mỗi lần (đơn vị cơ sở)
    TimesPerDay tinyint NULL,                              -- [G-07] số lần/ngày
    CONSTRAINT FK_PrescriptionDetails_Prescriptions FOREIGN KEY(PrescriptionId) REFERENCES dbo.Prescriptions(PrescriptionId) ON DELETE CASCADE,
    CONSTRAINT FK_PrescriptionDetails_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_PrescriptionDetails_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT CK_PrescriptionDetails_Qty CHECK(PrescribedQuantity > 0),
    CONSTRAINT CK_PrescriptionDetails_Duration CHECK(DurationDays IS NULL OR DurationDays > 0),
    CONSTRAINT CK_PrescriptionDetails_Dose CHECK((DosePerTime IS NULL OR DosePerTime > 0) AND (TimesPerDay IS NULL OR TimesPerDay > 0))
);
GO

/* ============================================================================
   11. HÓA ĐƠN, PHÂN BỔ LÔ, THANH TOÁN (D3)
   ============================================================================ */
/* Status: 0 Chờ thanh toán (3.1.4/3.2.4) → 1 Đã thanh toán (đủ tiền → FEFO trừ tồn) | 2 Đã hủy.
   [G-10] chỉ hủy được hóa đơn CHỜ THANH TOÁN; hóa đơn đã thanh toán xử lý qua đổi trả.
   [G-09] HĐĐT mô phỏng (3.3.3). ShiftId bắt buộc: 3.1.4 "ca làm việc hiện tại". */
CREATE TABLE dbo.SalesInvoices (
    InvoiceId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoices PRIMARY KEY,
    InvoiceCode varchar(30) NOT NULL,
    CustomerId int NULL,
    EmployeeId int NOT NULL,
    ShiftId int NOT NULL,
    PrescriptionId int NULL,
    InvoiceDate datetime2(0) NOT NULL CONSTRAINT DF_SalesInvoices_Date DEFAULT SYSDATETIME(),
    SubTotal decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_SubTotal DEFAULT 0,
    DiscountAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_Discount DEFAULT 0,
    TaxAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_Tax DEFAULT 0,
    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_Total DEFAULT 0,
    Status tinyint NOT NULL CONSTRAINT DF_SalesInvoices_Status DEFAULT 0,
    Notes nvarchar(1000) NULL,
    EInvoiceNumber varchar(50) NULL,
    EInvoiceStatus tinyint NOT NULL CONSTRAINT DF_SalesInvoices_EInvoiceStatus DEFAULT 0,  -- 0 Chưa xuất, 1 Đã xuất, 2 Lỗi
    EInvoiceIssuedAt datetime2(0) NULL,
    BuyerName nvarchar(200) NULL,
    BuyerTaxCode varchar(30) NULL,
    BuyerAddress nvarchar(300) NULL,
    CancelledByEmployeeId int NULL,
    CancelledAt datetime2(0) NULL,
    CancelReason nvarchar(500) NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT UQ_SalesInvoices_Code UNIQUE(InvoiceCode),
    CONSTRAINT FK_SalesInvoices_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(CustomerId),
    CONSTRAINT FK_SalesInvoices_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT FK_SalesInvoices_Shifts FOREIGN KEY(ShiftId) REFERENCES dbo.Shifts(ShiftId),
    CONSTRAINT FK_SalesInvoices_Prescriptions FOREIGN KEY(PrescriptionId) REFERENCES dbo.Prescriptions(PrescriptionId),
    CONSTRAINT FK_SalesInvoices_Cancellers FOREIGN KEY(CancelledByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_SalesInvoices_Amounts CHECK(SubTotal >= 0 AND DiscountAmount >= 0 AND TaxAmount >= 0 AND TotalAmount >= 0),
    CONSTRAINT CK_SalesInvoices_Status CHECK(Status IN (0,1,2)),
    CONSTRAINT CK_SalesInvoices_EInvoiceStatus CHECK(EInvoiceStatus IN (0,1,2)),
    CONSTRAINT CK_SalesInvoices_EInvoice CHECK(EInvoiceStatus <> 1 OR (EInvoiceNumber IS NOT NULL AND EInvoiceIssuedAt IS NOT NULL)),
    CONSTRAINT CK_SalesInvoices_Cancel CHECK(
           (Status = 2 AND CancelledByEmployeeId IS NOT NULL AND CancelledAt IS NOT NULL AND CancelReason IS NOT NULL)
        OR (Status <> 2 AND CancelledByEmployeeId IS NULL AND CancelledAt IS NULL AND CancelReason IS NULL))
);
GO
CREATE INDEX IX_SalesInvoices_Date ON dbo.SalesInvoices(InvoiceDate);
GO
CREATE UNIQUE INDEX UX_SalesInvoices_EInvoiceNumber ON dbo.SalesInvoices(EInvoiceNumber) WHERE EInvoiceNumber IS NOT NULL;
GO
/* [Q-14] UnitPrice, ConversionToBase lưu tại thời điểm bán — không đổi khi giá hiện hành thay đổi */
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
    PromotionId int NULL,                                  -- [G-05] khuyến mãi đã áp dụng
    CONSTRAINT FK_SalesInvoiceDetails_Invoices FOREIGN KEY(InvoiceId) REFERENCES dbo.SalesInvoices(InvoiceId) ON DELETE CASCADE,
    CONSTRAINT FK_SalesInvoiceDetails_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_SalesInvoiceDetails_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT FK_SalesInvoiceDetails_Promotions FOREIGN KEY(PromotionId) REFERENCES dbo.Promotions(PromotionId),
    CONSTRAINT CK_SalesInvoiceDetails_Qty CHECK(Quantity > 0 AND ConversionToBase > 0),
    CONSTRAINT CK_SalesInvoiceDetails_Amounts CHECK(UnitPrice >= 0 AND DiscountAmount >= 0 AND LineTotal >= 0)
);
GO
/* 2.3.4.1–2.3.4.2: phiếu xuất bán theo lô (FEFO), tạo khi hóa đơn thanh toán đủ */
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
/* 3.3.2.1–3.3.2.3: một hóa đơn nhiều thanh toán. TenderedAmount = tiền khách đưa (chỉ tiền mặt) */
CREATE TABLE dbo.Payments (
    PaymentId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
    InvoiceId int NOT NULL,
    PaymentMethod tinyint NOT NULL,                        -- 1 Tiền mặt, 2 Chuyển khoản/QR, 3 Thẻ
    Amount decimal(18,2) NOT NULL,
    TransactionReference varchar(100) NULL,
    PaidAt datetime2(0) NOT NULL CONSTRAINT DF_Payments_PaidAt DEFAULT SYSDATETIME(),
    Status tinyint NOT NULL CONSTRAINT DF_Payments_Status DEFAULT 1,  -- 1 Thành công, 2 Đã hủy
    Notes nvarchar(500) NULL,
    TenderedAmount decimal(18,2) NULL,                     -- [P3]
    CONSTRAINT FK_Payments_Invoices FOREIGN KEY(InvoiceId) REFERENCES dbo.SalesInvoices(InvoiceId),
    CONSTRAINT CK_Payments_Amount CHECK(Amount > 0),
    CONSTRAINT CK_Payments_Method CHECK(PaymentMethod IN (1,2,3)),
    CONSTRAINT CK_Payments_Status CHECK(Status IN (1,2)),
    CONSTRAINT CK_Payments_Tendered CHECK(TenderedAmount IS NULL OR (PaymentMethod = 1 AND TenderedAmount >= Amount))
);
GO

/* ============================================================================
   12. ĐỔI TRẢ (D3 — 3.4)
   ============================================================================ */
/* 3.4.1 (Pharmacist) chỉ đọc hóa đơn gốc → 3.4.2–3.4.3 (Manager) lập phiếu hoàn tiền.
   [G-11][Q-16] Hoàn tiền mặt gắn với ca ĐANG MỞ của chính người hoàn tiền (ProcessedByEmployeeId);
   chuyển khoản/thẻ không tính vào tiền mặt của ca. */
CREATE TABLE dbo.Returns (
    ReturnId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Returns PRIMARY KEY,
    ReturnCode varchar(30) NOT NULL,
    InvoiceId int NOT NULL,
    ProcessedByEmployeeId int NOT NULL,
    ReturnDate datetime2(0) NOT NULL CONSTRAINT DF_Returns_Date DEFAULT SYSDATETIME(),
    Reason nvarchar(500) NOT NULL,
    RefundAmount decimal(18,2) NOT NULL CONSTRAINT DF_Returns_Refund DEFAULT 0,
    Status tinyint NOT NULL CONSTRAINT DF_Returns_Status DEFAULT 0,  -- 0 Đã hoàn tiền – chờ nhập kho, 1 Hoàn tất, 2 Hủy
    ShiftId int NULL,
    RefundMethod tinyint NOT NULL CONSTRAINT DF_Returns_RefundMethod DEFAULT 1,  -- 1 Tiền mặt, 2 Chuyển khoản/QR, 3 Thẻ
    RefundReference varchar(100) NULL,                     -- mã giao dịch hoàn (chuyển khoản/thẻ)
    CONSTRAINT UQ_Returns_Code UNIQUE(ReturnCode),
    CONSTRAINT FK_Returns_Invoices FOREIGN KEY(InvoiceId) REFERENCES dbo.SalesInvoices(InvoiceId),
    CONSTRAINT FK_Returns_Employees FOREIGN KEY(ProcessedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT FK_Returns_Shifts FOREIGN KEY(ShiftId) REFERENCES dbo.Shifts(ShiftId),
    CONSTRAINT CK_Returns_Refund CHECK(RefundAmount >= 0),
    CONSTRAINT CK_Returns_Status CHECK(Status IN (0,1,2)),
    CONSTRAINT CK_Returns_RefundMethod CHECK(RefundMethod IN (1,2,3)),
    CONSTRAINT CK_Returns_CashShift CHECK(RefundMethod <> 1 OR ShiftId IS NOT NULL)
);
GO
/* Quantity tính theo đơn vị của dòng hóa đơn gốc; BatchId do Manager nhập ở 3.4.4 */
CREATE TABLE dbo.ReturnDetails (
    ReturnDetailId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReturnDetails PRIMARY KEY,
    ReturnId int NOT NULL,
    InvoiceDetailId int NOT NULL,
    BatchId int NULL,
    Quantity decimal(18,3) NOT NULL,
    UnitRefundPrice decimal(18,2) NOT NULL,
    RestockStatus tinyint NOT NULL CONSTRAINT DF_ReturnDetails_Restock DEFAULT 0,  -- 0 Chờ nhập, 1 Đã nhập lại, 2 Không nhập
    ConditionNote nvarchar(500) NULL,
    CONSTRAINT FK_ReturnDetails_Returns FOREIGN KEY(ReturnId) REFERENCES dbo.Returns(ReturnId) ON DELETE CASCADE,
    CONSTRAINT FK_ReturnDetails_InvoiceDetails FOREIGN KEY(InvoiceDetailId) REFERENCES dbo.SalesInvoiceDetails(InvoiceDetailId),
    CONSTRAINT FK_ReturnDetails_Batches FOREIGN KEY(BatchId) REFERENCES dbo.MedicineBatches(BatchId),
    CONSTRAINT CK_ReturnDetails_Qty CHECK(Quantity > 0 AND UnitRefundPrice >= 0),
    CONSTRAINT CK_ReturnDetails_Restock CHECK(RestockStatus IN (0,1,2)),
    CONSTRAINT CK_ReturnDetails_RestockBatch CHECK(RestockStatus <> 1 OR BatchId IS NOT NULL)
);
GO

/* ============================================================================
   13. TRẢ HÀNG VÀ CÔNG NỢ NHÀ CUNG CẤP (D2 — 2.2.5, 2.3.1, 2.3.5)
   ============================================================================ */
CREATE TABLE dbo.SupplierReturns (
    SupplierReturnId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierReturns PRIMARY KEY,
    SupplierReturnCode varchar(30) NOT NULL,
    SupplierId int NOT NULL,
    GoodsReceiptId int NULL,
    CreatedByEmployeeId int NOT NULL,
    ReturnDate datetime2(0) NOT NULL CONSTRAINT DF_SupplierReturns_Date DEFAULT SYSDATETIME(),
    Reason nvarchar(500) NOT NULL,
    Status tinyint NOT NULL CONSTRAINT DF_SupplierReturns_Status DEFAULT 0,  -- 0 Nháp, 1 Đã xuất trả, 2 Đã quyết toán, 3 Hủy
    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_SupplierReturns_Total DEFAULT 0,
    CONSTRAINT UQ_SupplierReturns_Code UNIQUE(SupplierReturnCode),
    CONSTRAINT FK_SupplierReturns_Suppliers FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(SupplierId),
    CONSTRAINT FK_SupplierReturns_Receipts FOREIGN KEY(GoodsReceiptId) REFERENCES dbo.GoodsReceipts(GoodsReceiptId),
    CONSTRAINT FK_SupplierReturns_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_SupplierReturns_Total CHECK(TotalAmount >= 0),
    CONSTRAINT CK_SupplierReturns_Status CHECK(Status IN (0,1,2,3))
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
/* [G-13] SupplierPaymentCode = số phiếu chi (PaymentType 1, 2.2.5) / phiếu thu (PaymentType 2, 2.3.5) */
CREATE TABLE dbo.SupplierPayments (
    SupplierPaymentId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierPayments PRIMARY KEY,
    SupplierId int NOT NULL,
    GoodsReceiptId int NULL,
    SupplierReturnId int NULL,
    EmployeeId int NOT NULL,
    PaymentType tinyint NOT NULL,                          -- 1 Chi trả NCC, 2 Nhận tiền hoàn từ NCC
    PaymentMethod tinyint NOT NULL,                        -- 1 Tiền mặt, 2 Chuyển khoản
    Amount decimal(18,2) NOT NULL,
    TransactionReference varchar(100) NULL,
    PaidAt datetime2(0) NOT NULL CONSTRAINT DF_SupplierPayments_Date DEFAULT SYSDATETIME(),
    Status tinyint NOT NULL CONSTRAINT DF_SupplierPayments_Status DEFAULT 1,  -- 1 Thành công, 2 Đã hủy
    Notes nvarchar(500) NULL,
    SupplierPaymentCode varchar(30) NOT NULL,
    CONSTRAINT UQ_SupplierPayments_Code UNIQUE(SupplierPaymentCode),
    CONSTRAINT FK_SupplierPayments_Suppliers FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(SupplierId),
    CONSTRAINT FK_SupplierPayments_Receipts FOREIGN KEY(GoodsReceiptId) REFERENCES dbo.GoodsReceipts(GoodsReceiptId),
    CONSTRAINT FK_SupplierPayments_Returns FOREIGN KEY(SupplierReturnId) REFERENCES dbo.SupplierReturns(SupplierReturnId),
    CONSTRAINT FK_SupplierPayments_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_SupplierPayments_Amount CHECK(Amount > 0),
    CONSTRAINT CK_SupplierPayments_Type CHECK(PaymentType IN (1,2)),
    CONSTRAINT CK_SupplierPayments_Method CHECK(PaymentMethod IN (1,2)),
    CONSTRAINT CK_SupplierPayments_Status CHECK(Status IN (1,2))
);
GO

/* ============================================================================
   14. QUY ĐỊNH NHÀ THUỐC, GIÁ, BẢO QUẢN (D1, D2, D4 — 1.5, 2.5.3)
   ============================================================================ */
/* 1.5.2 (Manager). [G-02] ExpiryWarningDays NULL = dùng PharmacySettings.ExpiryWarningDays (1.5.3) */
CREATE TABLE dbo.InventoryPolicies (
    InventoryPolicyId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryPolicies PRIMARY KEY,
    MedicineId int NOT NULL,
    WarehouseId int NOT NULL,
    MinimumQuantity decimal(18,3) NOT NULL CONSTRAINT DF_InventoryPolicies_Min DEFAULT 0,  -- đơn vị cơ sở
    ExpiryWarningDays int NULL,
    UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_InventoryPolicies_Updated DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_InventoryPolicies UNIQUE(MedicineId, WarehouseId),
    CONSTRAINT FK_InventoryPolicies_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_InventoryPolicies_Warehouses FOREIGN KEY(WarehouseId) REFERENCES dbo.Warehouses(WarehouseId),
    CONSTRAINT CK_InventoryPolicies_Values CHECK(MinimumQuantity >= 0 AND (ExpiryWarningDays IS NULL OR ExpiryWarningDays >= 0))
);
GO
/* 1.5.4 (Owner): mức giảm giá tối đa, quy định đổi trả */
CREATE TABLE dbo.PricePolicies (
    PricePolicyId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PricePolicies PRIMARY KEY,
    PolicyName nvarchar(150) NOT NULL,
    MaxDiscountPercent decimal(5,2) NOT NULL CONSTRAINT DF_PricePolicies_Discount DEFAULT 0,
    ReturnWithinDays int NOT NULL CONSTRAINT DF_PricePolicies_ReturnDays DEFAULT 0,
    IsActive bit NOT NULL CONSTRAINT DF_PricePolicies_Active DEFAULT 1,
    EffectiveFrom date NULL,
    EffectiveTo date NULL,
    ReturnConditions nvarchar(1000) NULL,                  -- [G-05] điều kiện đổi trả
    CONSTRAINT CK_PricePolicies_Discount CHECK(MaxDiscountPercent BETWEEN 0 AND 100),
    CONSTRAINT CK_PricePolicies_ReturnDays CHECK(ReturnWithinDays >= 0),
    CONSTRAINT CK_PricePolicies_Dates CHECK(EffectiveFrom IS NULL OR EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
GO
/* [Q-14] Lịch sử giá: dòng đầu (khai báo, 1.1.4) OldSellingPrice = NULL;
   mỗi lần Owner đổi giá (1.5.4) phải có OldSellingPrice và ChangeReason. */
CREATE TABLE dbo.MedicinePriceHistories (
    PriceHistoryId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MedicinePriceHistories PRIMARY KEY,
    MedicineId int NOT NULL,
    UnitId int NOT NULL,
    PurchasePrice decimal(18,2) NULL,
    SellingPrice decimal(18,2) NOT NULL,
    EffectiveFrom datetime2(0) NOT NULL,
    EffectiveTo datetime2(0) NULL,
    ChangedByEmployeeId int NULL,
    OldSellingPrice decimal(18,2) NULL,
    ChangeReason nvarchar(500) NULL,
    CONSTRAINT FK_MedicinePriceHistories_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_MedicinePriceHistories_Units FOREIGN KEY(UnitId) REFERENCES dbo.Units(UnitId),
    CONSTRAINT FK_MedicinePriceHistories_Employees FOREIGN KEY(ChangedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_MedicinePriceHistories_Prices CHECK(SellingPrice >= 0 AND (PurchasePrice IS NULL OR PurchasePrice >= 0)
        AND (OldSellingPrice IS NULL OR OldSellingPrice >= 0)),
    CONSTRAINT CK_MedicinePriceHistories_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
    CONSTRAINT CK_MedicinePriceHistories_Reason CHECK(OldSellingPrice IS NULL OR ChangeReason IS NOT NULL)
);
GO
/* [G-02] Cấu hình chung của nhà thuốc thuộc D4 (1.5.3 ngưỡng cận hạn) */
CREATE TABLE dbo.PharmacySettings (
    SettingKey varchar(80) NOT NULL CONSTRAINT PK_PharmacySettings PRIMARY KEY,
    SettingValue nvarchar(500) NOT NULL,
    Description nvarchar(300) NULL,
    UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_PharmacySettings_Updated DEFAULT SYSDATETIME(),
    UpdatedByEmployeeId int NULL,
    CONSTRAINT FK_PharmacySettings_Employees FOREIGN KEY(UpdatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId)
);
GO
/* 2.5.3 Sổ nhiệt độ, độ ẩm */
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

/* ============================================================================
   15. THU HỒI THUỐC THEO LÔ (D2 — 2.3.3)
   ============================================================================ */
/* 2.3.3.1 tiếp nhận công văn → khóa lô (MedicineBatches.Status = 3) → 2.3.3.2 tra cứu → 2.3.3.3 phiếu thu hồi.
   Thu hồi chỉ trừ tồn kho (giữ ràng buộc QuantityRecovered <= QuantityOnHandAtRecall). */
CREATE TABLE dbo.ProductRecalls (
    RecallId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductRecalls PRIMARY KEY,
    RecallCode varchar(40) NOT NULL,
    MedicineId int NOT NULL,
    NoticeNumber varchar(80) NULL,
    NoticeDate date NULL,
    Reason nvarchar(1000) NOT NULL,
    Status tinyint NOT NULL CONSTRAINT DF_ProductRecalls_Status DEFAULT 0,  -- 0 Mới tiếp nhận, 1 Đang thu hồi, 2 Hoàn tất
    CreatedByEmployeeId int NOT NULL,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_ProductRecalls_Created DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_ProductRecalls_Code UNIQUE(RecallCode),
    CONSTRAINT FK_ProductRecalls_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_ProductRecalls_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_ProductRecalls_Status CHECK(Status IN (0,1,2))
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

/* ============================================================================
   16. NHẮC LỊCH, LIÊN THÔNG, NHẬT KÝ
   ============================================================================ */
/* 3.5.2.1 Pharmacist lập lịch → 3.5.2.2 Hệ thống tự gửi (mô phỏng, ghi IntegrationLogs) */
CREATE TABLE dbo.CustomerReminders (
    ReminderId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerReminders PRIMARY KEY,
    CustomerId int NOT NULL,
    MedicineId int NULL,
    ReminderDate datetime2(0) NOT NULL,
    Message nvarchar(500) NOT NULL,
    Channel tinyint NOT NULL CONSTRAINT DF_CustomerReminders_Channel DEFAULT 0,  -- 0 SMS, 1 Email
    Status tinyint NOT NULL CONSTRAINT DF_CustomerReminders_Status DEFAULT 0,    -- 0 Chờ gửi, 1 Đã gửi, 2 Lỗi, 3 Hủy
    SentAt datetime2(0) NULL,
    CreatedByEmployeeId int NULL,
    DaysSupply int NULL,                                   -- [P3] số ngày dùng (3.5.2.1)
    CONSTRAINT FK_CustomerReminders_Customers FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(CustomerId),
    CONSTRAINT FK_CustomerReminders_Medicines FOREIGN KEY(MedicineId) REFERENCES dbo.Medicines(MedicineId),
    CONSTRAINT FK_CustomerReminders_Employees FOREIGN KEY(CreatedByEmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT CK_CustomerReminders_Channel CHECK(Channel IN (0,1)),
    CONSTRAINT CK_CustomerReminders_Status CHECK(Status IN (0,1,2,3)),
    CONSTRAINT CK_CustomerReminders_Days CHECK(DaysSupply IS NULL OR DaysSupply > 0)
);
GO
/* 3.3.3, 3.5.2.2, 4.4.x — mô phỏng; SYS-11: mất Internet thì xếp hàng, có mạng gửi lại */
CREATE TABLE dbo.IntegrationLogs (
    IntegrationLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationLogs PRIMARY KEY,
    IntegrationType varchar(50) NOT NULL,
    ReferenceType varchar(30) NOT NULL,
    ReferenceId bigint NOT NULL,
    RequestPayload nvarchar(max) NULL,
    ResponsePayload nvarchar(max) NULL,
    Status tinyint NOT NULL CONSTRAINT DF_IntegrationLogs_Status DEFAULT 0,  -- 0 Chờ gửi, 1 Thành công, 2 Lỗi
    RetryCount int NOT NULL CONSTRAINT DF_IntegrationLogs_Retry DEFAULT 0,
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_IntegrationLogs_Created DEFAULT SYSDATETIME(),
    LastAttemptAt datetime2(0) NULL,
    ErrorMessage nvarchar(2000) NULL,
    CONSTRAINT CK_IntegrationLogs_Retry CHECK(RetryCount >= 0),
    CONSTRAINT CK_IntegrationLogs_Status CHECK(Status IN (0,1,2))
);
GO
/* SYS-09: ghi nhật ký thao tác nhạy cảm (giảm giá vượt mức, hủy hóa đơn, đổi giá, khóa tài khoản…) */
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
    CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_AuditLogs_Created DEFAULT SYSDATETIME(),  -- [DB-06]
    CONSTRAINT FK_AuditLogs_Users FOREIGN KEY(UserId) REFERENCES dbo.AspNetUsers(Id)
);
GO
CREATE INDEX IX_AuditLogs_Entity ON dbo.AuditLogs(EntityName, EntityId, CreatedAt);
GO

/* ============================================================================
   17. INDEX [DB-14] — mọi khóa ngoại có index (cột đầu), cộng index truy vấn nghiệp vụ
   ============================================================================ */
/* -- Truy vấn nghiệp vụ (giữ từ v1) */
CREATE INDEX IX_Medicines_Name ON dbo.Medicines(MedicineName);
CREATE INDEX IX_MedicineBatches_Expiry ON dbo.MedicineBatches(ExpiryDate);
CREATE INDEX IX_PurchaseOrders_Date ON dbo.PurchaseOrders(OrderDate);
CREATE INDEX IX_GoodsReceipts_Date ON dbo.GoodsReceipts(ReceiptDate);
CREATE INDEX IX_Prescriptions_Customer ON dbo.Prescriptions(CustomerId, PrescriptionDate);
CREATE INDEX IX_Payments_Invoice ON dbo.Payments(InvoiceId, PaidAt);
CREATE INDEX IX_Returns_Invoice ON dbo.Returns(InvoiceId, ReturnDate);
CREATE INDEX IX_SupplierReturns_Date ON dbo.SupplierReturns(ReturnDate);
CREATE INDEX IX_SupplierPayments_SupplierDate ON dbo.SupplierPayments(SupplierId, PaidAt);
GO
/* -- Truy vấn nghiệp vụ (mới) */
CREATE INDEX IX_SalesInvoices_Shift ON dbo.SalesInvoices(ShiftId, Status);
CREATE INDEX IX_SalesInvoices_CustomerDate ON dbo.SalesInvoices(CustomerId, InvoiceDate);
CREATE INDEX IX_SalesInvoices_EmployeeDate ON dbo.SalesInvoices(EmployeeId, InvoiceDate);
CREATE INDEX IX_SalesInvoices_Status ON dbo.SalesInvoices(Status, InvoiceDate);
CREATE INDEX IX_InventoryTransactions_BatchDate ON dbo.InventoryTransactions(BatchId, TransactionDate);
CREATE INDEX IX_InventoryTransactions_Reference ON dbo.InventoryTransactions(ReferenceType, ReferenceId);
CREATE INDEX IX_CustomerReminders_Due ON dbo.CustomerReminders(Status, ReminderDate);
CREATE INDEX IX_IntegrationLogs_Status ON dbo.IntegrationLogs(Status, CreatedAt);
CREATE INDEX IX_Promotions_Effective ON dbo.Promotions(EffectiveFrom, EffectiveTo);
CREATE INDEX IX_MedicinePriceHistories_Medicine ON dbo.MedicinePriceHistories(MedicineId, UnitId, EffectiveFrom);
CREATE INDEX IX_Returns_Shift ON dbo.Returns(ShiftId, RefundMethod);
GO
/* -- Index cho khóa ngoại chưa có index */
CREATE INDEX IX_AspNetRoleClaims_RoleId ON dbo.AspNetRoleClaims(RoleId);
CREATE INDEX IX_AspNetUserClaims_UserId ON dbo.AspNetUserClaims(UserId);
CREATE INDEX IX_AspNetUserLogins_UserId ON dbo.AspNetUserLogins(UserId);
CREATE INDEX IX_AspNetUserRoles_RoleId ON dbo.AspNetUserRoles(RoleId);
CREATE INDEX IX_RolePermissions_PermissionId ON dbo.RolePermissions(PermissionId);
CREATE INDEX IX_Medicines_CategoryId ON dbo.Medicines(CategoryId);
CREATE INDEX IX_Medicines_ManufacturerId ON dbo.Medicines(ManufacturerId);
CREATE INDEX IX_Medicines_BaseUnitId ON dbo.Medicines(BaseUnitId);
CREATE INDEX IX_MedicineIngredients_IngredientId ON dbo.MedicineIngredients(IngredientId);
CREATE INDEX IX_MedicineUnitConversions_UnitId ON dbo.MedicineUnitConversions(UnitId);
CREATE INDEX IX_IngredientInteractions_Ingredient2 ON dbo.IngredientInteractions(IngredientId2);
CREATE INDEX IX_Promotions_MedicineId ON dbo.Promotions(MedicineId);
CREATE INDEX IX_Promotions_CategoryId ON dbo.Promotions(CategoryId);
CREATE INDEX IX_Promotions_CreatedBy ON dbo.Promotions(CreatedByEmployeeId);
CREATE INDEX IX_CustomerAllergies_IngredientId ON dbo.CustomerAllergies(IngredientId);
CREATE INDEX IX_StorageLocations_Parent ON dbo.StorageLocations(ParentLocationId);
CREATE INDEX IX_PurchaseOrders_SupplierId ON dbo.PurchaseOrders(SupplierId);
CREATE INDEX IX_PurchaseOrders_CreatedBy ON dbo.PurchaseOrders(CreatedByEmployeeId);
CREATE INDEX IX_PurchaseOrderDetails_OrderId ON dbo.PurchaseOrderDetails(PurchaseOrderId);
CREATE INDEX IX_PurchaseOrderDetails_MedicineId ON dbo.PurchaseOrderDetails(MedicineId);
CREATE INDEX IX_PurchaseOrderDetails_UnitId ON dbo.PurchaseOrderDetails(UnitId);
CREATE INDEX IX_GoodsReceipts_OrderId ON dbo.GoodsReceipts(PurchaseOrderId);
CREATE INDEX IX_GoodsReceipts_SupplierId ON dbo.GoodsReceipts(SupplierId);
CREATE INDEX IX_GoodsReceipts_ReceivedBy ON dbo.GoodsReceipts(ReceivedByEmployeeId);
CREATE INDEX IX_GoodsReceiptDetails_ReceiptId ON dbo.GoodsReceiptDetails(GoodsReceiptId);
CREATE INDEX IX_GoodsReceiptDetails_OrderDetailId ON dbo.GoodsReceiptDetails(PurchaseOrderDetailId);
CREATE INDEX IX_GoodsReceiptDetails_MedicineId ON dbo.GoodsReceiptDetails(MedicineId);
CREATE INDEX IX_GoodsReceiptDetails_UnitId ON dbo.GoodsReceiptDetails(UnitId);
CREATE INDEX IX_GoodsReceiptDetails_LocationId ON dbo.GoodsReceiptDetails(LocationId);
CREATE INDEX IX_MedicineBatches_ReceiptId ON dbo.MedicineBatches(GoodsReceiptId);
CREATE INDEX IX_MedicineBatches_WarehouseId ON dbo.MedicineBatches(WarehouseId);
CREATE INDEX IX_MedicineBatches_LocationId ON dbo.MedicineBatches(LocationId);
CREATE INDEX IX_InventoryTransactions_WarehouseId ON dbo.InventoryTransactions(WarehouseId);
CREATE INDEX IX_InventoryTransactions_CreatedBy ON dbo.InventoryTransactions(CreatedByEmployeeId);
CREATE INDEX IX_Stocktakes_WarehouseId ON dbo.Stocktakes(WarehouseId);
CREATE INDEX IX_Stocktakes_CreatedBy ON dbo.Stocktakes(CreatedByEmployeeId);
CREATE INDEX IX_Stocktakes_ApprovedBy ON dbo.Stocktakes(ApprovedByEmployeeId);
CREATE INDEX IX_Stocktakes_AdjustedBy ON dbo.Stocktakes(AdjustedByEmployeeId);
CREATE INDEX IX_Stocktakes_LocationId ON dbo.Stocktakes(LocationId);
CREATE INDEX IX_Stocktakes_CategoryId ON dbo.Stocktakes(CategoryId);
CREATE INDEX IX_StocktakeDetails_BatchId ON dbo.StocktakeDetails(BatchId);
CREATE INDEX IX_StockDisposals_WarehouseId ON dbo.StockDisposals(WarehouseId);
CREATE INDEX IX_StockDisposals_CreatedBy ON dbo.StockDisposals(CreatedByEmployeeId);
CREATE INDEX IX_StockDisposalDetails_BatchId ON dbo.StockDisposalDetails(BatchId);
CREATE INDEX IX_ShiftClosings_ClosedBy ON dbo.ShiftClosings(ClosedByEmployeeId);
CREATE INDEX IX_Prescriptions_CheckedBy ON dbo.Prescriptions(CheckedByEmployeeId);
CREATE INDEX IX_PrescriptionDetails_PrescriptionId ON dbo.PrescriptionDetails(PrescriptionId);
CREATE INDEX IX_PrescriptionDetails_MedicineId ON dbo.PrescriptionDetails(MedicineId);
CREATE INDEX IX_PrescriptionDetails_UnitId ON dbo.PrescriptionDetails(UnitId);
CREATE INDEX IX_SalesInvoices_PrescriptionId ON dbo.SalesInvoices(PrescriptionId);
CREATE INDEX IX_SalesInvoices_CancelledBy ON dbo.SalesInvoices(CancelledByEmployeeId);
CREATE INDEX IX_SalesInvoiceDetails_InvoiceId ON dbo.SalesInvoiceDetails(InvoiceId);
CREATE INDEX IX_SalesInvoiceDetails_MedicineId ON dbo.SalesInvoiceDetails(MedicineId);
CREATE INDEX IX_SalesInvoiceDetails_UnitId ON dbo.SalesInvoiceDetails(UnitId);
CREATE INDEX IX_SalesInvoiceDetails_PromotionId ON dbo.SalesInvoiceDetails(PromotionId);
CREATE INDEX IX_SalesIssueAllocations_DetailId ON dbo.SalesIssueAllocations(InvoiceDetailId);
CREATE INDEX IX_SalesIssueAllocations_BatchId ON dbo.SalesIssueAllocations(BatchId);
CREATE INDEX IX_Returns_ProcessedBy ON dbo.Returns(ProcessedByEmployeeId);
CREATE INDEX IX_ReturnDetails_ReturnId ON dbo.ReturnDetails(ReturnId);
CREATE INDEX IX_ReturnDetails_InvoiceDetailId ON dbo.ReturnDetails(InvoiceDetailId);
CREATE INDEX IX_ReturnDetails_BatchId ON dbo.ReturnDetails(BatchId);
CREATE INDEX IX_SupplierReturns_SupplierId ON dbo.SupplierReturns(SupplierId);
CREATE INDEX IX_SupplierReturns_ReceiptId ON dbo.SupplierReturns(GoodsReceiptId);
CREATE INDEX IX_SupplierReturns_CreatedBy ON dbo.SupplierReturns(CreatedByEmployeeId);
CREATE INDEX IX_SupplierReturnDetails_ReturnId ON dbo.SupplierReturnDetails(SupplierReturnId);
CREATE INDEX IX_SupplierReturnDetails_BatchId ON dbo.SupplierReturnDetails(BatchId);
CREATE INDEX IX_SupplierPayments_ReceiptId ON dbo.SupplierPayments(GoodsReceiptId);
CREATE INDEX IX_SupplierPayments_ReturnId ON dbo.SupplierPayments(SupplierReturnId);
CREATE INDEX IX_SupplierPayments_EmployeeId ON dbo.SupplierPayments(EmployeeId);
CREATE INDEX IX_InventoryPolicies_WarehouseId ON dbo.InventoryPolicies(WarehouseId);
CREATE INDEX IX_MedicinePriceHistories_UnitId ON dbo.MedicinePriceHistories(UnitId);
CREATE INDEX IX_MedicinePriceHistories_ChangedBy ON dbo.MedicinePriceHistories(ChangedByEmployeeId);
CREATE INDEX IX_PharmacySettings_UpdatedBy ON dbo.PharmacySettings(UpdatedByEmployeeId);
CREATE INDEX IX_StorageEnvironmentLogs_WarehouseDate ON dbo.StorageEnvironmentLogs(WarehouseId, RecordedAt);
CREATE INDEX IX_StorageEnvironmentLogs_LocationId ON dbo.StorageEnvironmentLogs(LocationId);
CREATE INDEX IX_StorageEnvironmentLogs_RecordedBy ON dbo.StorageEnvironmentLogs(RecordedByEmployeeId);
CREATE INDEX IX_ProductRecalls_MedicineId ON dbo.ProductRecalls(MedicineId);
CREATE INDEX IX_ProductRecalls_CreatedBy ON dbo.ProductRecalls(CreatedByEmployeeId);
CREATE INDEX IX_RecallBatches_BatchId ON dbo.RecallBatches(BatchId);
CREATE INDEX IX_CustomerReminders_CustomerId ON dbo.CustomerReminders(CustomerId);
CREATE INDEX IX_CustomerReminders_MedicineId ON dbo.CustomerReminders(MedicineId);
CREATE INDEX IX_CustomerReminders_CreatedBy ON dbo.CustomerReminders(CreatedByEmployeeId);
CREATE INDEX IX_AuditLogs_UserId ON dbo.AuditLogs(UserId, CreatedAt);
GO

/* ============================================================================
   18. DỮ LIỆU KHỞI TẠO (chạy lại không lỗi — IF NOT EXISTS)
   Không seed tài khoản người dùng.
   ============================================================================ */

/* 18.1 Ba vai trò cố định (1.3.2) — Id cố định để code tham chiếu */
INSERT INTO dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
SELECT v.Id, v.Name, v.NormalizedName, v.Stamp
FROM (VALUES
    (N'7c1e5b0a-0001-4a6f-9b1e-000000000001', N'Owner',      N'OWNER',      N'0b6f3a1e-5c2d-4e8f-9a7b-100000000001'),
    (N'7c1e5b0a-0002-4a6f-9b1e-000000000002', N'Manager',    N'MANAGER',    N'0b6f3a1e-5c2d-4e8f-9a7b-100000000002'),
    (N'7c1e5b0a-0003-4a6f-9b1e-000000000003', N'Pharmacist', N'PHARMACIST', N'0b6f3a1e-5c2d-4e8f-9a7b-100000000003')
) AS v(Id, Name, NormalizedName, Stamp)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles r WHERE r.Id = v.Id);
GO

/* 18.2 Quyền = chức năng lá BFD có vai trò thực hiện (62) + quyền bổ sung (5).
   Chức năng chỉ do Hệ thống thực hiện (16) không cần quyền.
   Kind: A = thao tác (xem/thêm/sửa), V = chỉ xem (báo cáo, tra cứu).
   SYS-13: các quyền nhóm 2 và F3.4.4 làm thay đổi tồn kho → KHÔNG BAO GIỜ cấp cho Pharmacist
   (ứng dụng chặn ở server khi Owner chỉnh quyền 1.3.3). */
DECLARE @Perm TABLE (Code varchar(80), Name nvarchar(150), Module nvarchar(80), Kind char(1), Sensitive bit,
                     ToManager bit, ToPharmacist bit);
INSERT INTO @Perm VALUES
 ('F1.1.1', N'Thêm thuốc mới',                       N'1. Quản trị hệ thống', 'A', 0, 1, 0),
 ('F1.1.2', N'Sửa thông tin thuốc',                  N'1. Quản trị hệ thống', 'A', 0, 1, 0),
 ('F1.1.3', N'Phân loại thuốc',                      N'1. Quản trị hệ thống', 'A', 0, 1, 0),
 ('F1.1.4', N'Khai báo quy đổi Hộp – Vỉ – Viên',     N'1. Quản trị hệ thống', 'A', 0, 1, 0),
 ('F1.2.1', N'Cập nhật nhà cung cấp',                N'1. Quản trị hệ thống', 'A', 0, 1, 0),
 ('F1.2.2', N'Cập nhật khách hàng',                  N'1. Quản trị hệ thống', 'A', 0, 1, 1),
 ('F1.3.1', N'Tạo tài khoản người dùng',             N'1. Quản trị hệ thống', 'A', 1, 0, 0),
 ('F1.3.2', N'Tạo vai trò (danh sách vai trò)',      N'1. Quản trị hệ thống', 'V', 1, 0, 0),
 ('F1.3.3', N'Phân quyền cho vai trò',               N'1. Quản trị hệ thống', 'A', 1, 0, 0),
 ('F1.3.4', N'Gán vai trò cho người dùng',           N'1. Quản trị hệ thống', 'A', 1, 0, 0),
 ('F1.3.5', N'Khóa tài khoản',                       N'1. Quản trị hệ thống', 'A', 1, 0, 0),
 ('F1.4.1', N'Mở ca',                                N'1. Quản trị hệ thống', 'A', 0, 1, 1),
 ('F1.4.2', N'Bàn giao ca',                          N'1. Quản trị hệ thống', 'A', 0, 1, 1),
 ('F1.4.3', N'Chốt tiền cuối ca',                    N'1. Quản trị hệ thống', 'A', 0, 1, 1),
 ('F1.5.1', N'Khai báo tủ, kệ lưu trữ',              N'1. Quản trị hệ thống', 'A', 0, 1, 0),
 ('F1.5.2', N'Thiết lập định mức tồn',               N'1. Quản trị hệ thống', 'A', 0, 1, 0),
 ('F1.5.3', N'Thiết lập ngưỡng cận hạn',             N'1. Quản trị hệ thống', 'A', 0, 1, 0),
 ('F1.5.4', N'Thiết lập chính sách giá',             N'1. Quản trị hệ thống', 'A', 1, 0, 0),
 ('F2.1.1', N'Lập đơn đặt hàng',                     N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.1.2', N'Gửi đơn đặt hàng',                     N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.1.3', N'Theo dõi đơn đặt hàng',                N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.2.1.1', N'Đối chiếu đơn đặt hàng',             N'2. Quản lý kho hàng',  'V', 0, 1, 0),
 ('F2.2.1.2', N'Kiểm tra hạn dùng, chất lượng',      N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.2.1.3', N'Ghi nhận hàng thiếu, hỏng',          N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.2.2', N'Lập phiếu nhập kho',                   N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.2.3', N'Ghi nhận số lô, hạn dùng',             N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.2.4', N'Xếp thuốc vào vị trí',                 N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.2.5', N'Thanh toán nhà cung cấp',              N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.3.1', N'Lập phiếu trả nhà cung cấp',           N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.3.2', N'Lập biên bản hủy thuốc',               N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.3.3.1', N'Tiếp nhận thông báo thu hồi',        N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.3.3.2', N'Tra cứu lô đang tồn/đã bán',         N'2. Quản lý kho hàng',  'V', 0, 1, 0),
 ('F2.3.3.3', N'Lập phiếu thu hồi',                  N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.3.5', N'Thu tiền hoàn từ nhà cung cấp',        N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.4.1.1', N'In phiếu kiểm kê',                   N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.4.1.2', N'Nhập số lượng thực tế',              N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.4.2', N'Lập biên bản kiểm kê',                 N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.4.3', N'Điều chỉnh tồn kho',                   N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F2.5.3', N'Ghi sổ nhiệt độ, độ ẩm',               N'2. Quản lý kho hàng',  'A', 0, 1, 0),
 ('F3.1.1', N'Tra cứu thuốc',                        N'3. Quản lý bán hàng',  'V', 0, 1, 1),
 ('F3.1.4', N'Lập hóa đơn bán lẻ',                   N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.2.1', N'Tiếp nhận đơn thuốc',                  N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.2.2.1', N'Kiểm tra tính hợp lệ của đơn',       N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.2.2.2', N'Kiểm tra liều dùng',                 N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.2.3', N'Lưu thông tin đơn thuốc',              N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.2.4', N'Lập hóa đơn bán theo đơn',             N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.3.2.1', N'Thu tiền mặt',                       N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.3.2.2', N'Thu chuyển khoản, thẻ',              N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.3.2.3', N'Ghi nhận thanh toán kết hợp',        N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F3.4.1', N'Tiếp nhận yêu cầu đổi trả',            N'3. Quản lý bán hàng',  'V', 0, 1, 1),
 ('F3.4.2', N'Kiểm tra điều kiện đổi trả',           N'3. Quản lý bán hàng',  'A', 0, 1, 0),
 ('F3.4.3', N'Hoàn tiền cho khách',                  N'3. Quản lý bán hàng',  'A', 0, 1, 0),
 ('F3.4.4', N'Nhập lại thuốc trả',                   N'3. Quản lý bán hàng',  'A', 0, 1, 0),
 ('F3.5.2.1', N'Lập lịch nhắc',                      N'3. Quản lý bán hàng',  'A', 0, 1, 1),
 ('F4.1.1', N'Lập báo cáo doanh thu',                N'4. Lập báo cáo',       'V', 0, 1, 0),
 ('F4.1.2', N'Lập báo cáo lợi nhuận',                N'4. Lập báo cáo',       'V', 0, 1, 0),
 ('F4.2.1', N'Lập báo cáo nhập – xuất – tồn',        N'4. Lập báo cáo',       'V', 0, 1, 0),
 ('F4.2.2', N'Lập báo cáo thuốc cận hạn',            N'4. Lập báo cáo',       'V', 0, 1, 0),
 ('F4.3.1', N'Kết xuất sổ theo dõi xuất nhập thuốc', N'4. Lập báo cáo',       'V', 0, 1, 0),
 ('F4.3.2', N'Kết xuất sổ thuốc kiểm soát đặc biệt', N'4. Lập báo cáo',       'V', 0, 1, 0),
 ('F4.3.3', N'Lập sổ theo dõi',                      N'4. Lập báo cáo',       'V', 0, 1, 0),
 ('F4.4.2.3', N'Xử lý lỗi gửi dữ liệu',              N'4. Lập báo cáo',       'A', 0, 1, 0),
 /* Quyền bổ sung (không phải chức năng lá riêng) */
 ('F4.1.1-OWN', N'Xem doanh số cá nhân (4.1.1)',     N'4. Lập báo cáo',       'V', 0, 1, 1),
 ('F2.4.3-APPROVE', N'Duyệt điều chỉnh tồn kho (QT5)', N'2. Quản lý kho hàng', 'A', 1, 0, 0),
 ('SYS09.DiscountOverLimit', N'Giảm giá vượt mức tối đa', N'Bảo mật (SYS-09)', 'A', 1, 0, 0),
 ('SYS09.CancelInvoice', N'Hủy hóa đơn chờ thanh toán',   N'Bảo mật (SYS-09)', 'A', 1, 0, 0),
 ('SYS09.ViewAuditLog', N'Xem nhật ký hoạt động',         N'Bảo mật (SYS-09)', 'V', 1, 0, 0);

INSERT INTO dbo.Permissions (PermissionCode, PermissionName, ModuleName, Description, IsSensitive)
SELECT p.Code, p.Name, p.Module,
       CASE p.Kind WHEN 'V' THEN N'Chỉ xem' ELSE N'Thao tác' END, p.Sensitive
FROM @Perm p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions x WHERE x.PermissionCode = p.Code);

/* 18.3 Ma trận quyền mặc định theo Bảng 2.3 bản chốt.
   Owner: toàn quyền. Manager: chức năng Manager + toàn bộ chức năng bán hàng của Pharmacist.
   Không cấp CanDelete cho Manager/Pharmacist (không xóa cứng chứng từ; danh mục dùng IsActive). */
INSERT INTO dbo.RolePermissions (RoleId, PermissionId, CanView, CanCreate, CanUpdate, CanDelete)
SELECT r.RoleId, pm.PermissionId,
       1,
       CASE WHEN r.RoleName = N'Owner' OR p.Kind = 'A' THEN 1 ELSE 0 END,
       CASE WHEN r.RoleName = N'Owner' OR p.Kind = 'A' THEN 1 ELSE 0 END,
       CASE WHEN r.RoleName = N'Owner' THEN 1 ELSE 0 END
FROM @Perm p
JOIN dbo.Permissions pm ON pm.PermissionCode = p.Code
CROSS JOIN (VALUES
    (N'7c1e5b0a-0001-4a6f-9b1e-000000000001', N'Owner'),
    (N'7c1e5b0a-0002-4a6f-9b1e-000000000002', N'Manager'),
    (N'7c1e5b0a-0003-4a6f-9b1e-000000000003', N'Pharmacist')
) AS r(RoleId, RoleName)
WHERE (r.RoleName = N'Owner'
       OR (r.RoleName = N'Manager' AND p.ToManager = 1)
       OR (r.RoleName = N'Pharmacist' AND p.ToPharmacist = 1))
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = r.RoleId AND x.PermissionId = pm.PermissionId);
GO

/* 18.4 Kho mặc định, đơn vị tính cơ bản */
INSERT INTO dbo.Warehouses (WarehouseCode, WarehouseName)
SELECT 'KHO01', N'Kho nhà thuốc'
WHERE NOT EXISTS (SELECT 1 FROM dbo.Warehouses WHERE WarehouseCode = 'KHO01');

INSERT INTO dbo.Units (UnitCode, UnitName)
SELECT v.Code, v.Name
FROM (VALUES ('VIEN', N'Viên'), ('VI', N'Vỉ'), ('HOP', N'Hộp'), ('CHAI', N'Chai'), ('LO', N'Lọ'),
             ('TUYP', N'Tuýp'), ('GOI', N'Gói'), ('ONG', N'Ống'), ('ML', N'ml')) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Units u WHERE u.UnitCode = v.Code);
GO

/* 18.5 Cấu hình chung [G-02] — 1.5.3 (Manager). Giá trị 90 ngày là GIẢ ĐỊNH, chỉnh trong ứng dụng.
   Chỉ chứa quy định thuộc D4 (ngưỡng cận hạn); không đặt quy định của D1 vào bảng này. */
INSERT INTO dbo.PharmacySettings (SettingKey, SettingValue, Description)
SELECT v.K, v.V, v.D
FROM (VALUES
    ('ExpiryWarningDays', N'90', N'1.5.3 Số ngày cảnh báo trước hạn sử dụng (mặc định toàn nhà thuốc)')
) AS v(K, V, D)
WHERE NOT EXISTS (SELECT 1 FROM dbo.PharmacySettings s WHERE s.SettingKey = v.K);

/* 18.6 Chính sách giá mặc định (1.5.4) — giá trị GIẢ ĐỊNH, Owner chỉnh */
INSERT INTO dbo.PricePolicies (PolicyName, MaxDiscountPercent, ReturnWithinDays, IsActive, ReturnConditions)
SELECT N'Chính sách mặc định', 10, 7, 1,
       N'Thuốc còn nguyên bao bì, còn hạn, có hóa đơn; không nhận lại thuốc đã mở hoặc thuốc cần bảo quản lạnh.'
WHERE NOT EXISTS (SELECT 1 FROM dbo.PricePolicies);
GO

/* ============================================================================
   GHI CHÚ CHO TẦNG ỨNG DỤNG (không phải lệnh SQL)
   [DB-07][SYS-14] Trừ tồn trong transaction, bằng UPDATE có điều kiện:
       UPDATE dbo.MedicineBatches
          SET QuantityOnHand = QuantityOnHand - @qty
        WHERE BatchId = @batchId AND Status = 1
          AND ExpiryDate > CAST(SYSDATETIME() AS date)
          AND QuantityOnHand >= @qty;
       -- @@ROWCOUNT = 0 → ROLLBACK, báo không đủ tồn / lô không hợp lệ (SYS-10).
     RowVersion dùng thêm cho cập nhật từ màn hình (optimistic concurrency của EF Core).
   [Q-15] Danh sách quyền làm thay đổi tồn: mọi PermissionCode bắt đầu 'F2.' và 'F3.4.4'
     → không được gán cho role Pharmacist.
   ============================================================================ */
SET NOEXEC OFF;
GO
