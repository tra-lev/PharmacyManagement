# CHANGELOG_DB — PharmacyManagementDB

## v2.1 — 02/11/2026 (quy tắc tài khoản, DEC-18)

File: `PharmacyManagementDB_v2.1.sql` — chạy SAU `PharmacyManagementDB.sql` (v2), chạy được trên DB đã có dữ liệu mẫu, chạy lại không lỗi.

| Mã | Thay đổi | Lý do |
|---|---|---|
| A-01 | `CREATE UNIQUE INDEX UX_AspNetUserRoles_SingleOwner ON AspNetUserRoles(RoleId) WHERE RoleId = '7c1e5b0a-0001-…0001'` | Chỉ một tài khoản Owner; CSDL chặn cả khi gọi API hoặc sửa dữ liệu ngoài giao diện |
| A-02 | `CREATE UNIQUE INDEX UX_AspNetUsers_NormalizedEmail ON AspNetUsers(NormalizedEmail) WHERE NormalizedEmail IS NOT NULL` | Mỗi email chỉ thuộc một tài khoản (ứng dụng cũng bật `RequireUniqueEmail`) |

- Không thêm bảng, không thêm/đổi/xóa cột. Giữ nguyên `EmailIndex` (không unique) do Identity khai báo.
- Dữ liệu tài khoản dùng bảng có sẵn: mã xác minh lưu **bản băm** trong `AspNetUserTokens` (LoginProvider `PharmacyVerify`); cờ bắt đổi mật khẩu trong `AspNetUserClaims` (`pm:must_change_password`); trạng thái xác minh email ở `AspNetUsers.EmailConfirmed`; kết quả gửi email ghi `IntegrationLogs` (IntegrationType `EMAIL`, ReferenceType `Employee`, không lưu mã); mốc khởi tạo Owner là dòng `PharmacySettings.OwnerSetupCompletedAt` (ứng dụng tự ghi, không seed).
- Script tự dừng nếu dữ liệu hiện có đã vi phạm (nhiều hơn 1 Owner hoặc email trùng).
- Kiểm thử: mô phỏng ràng buộc trên bản sao dữ liệu mẫu — thêm Owner thứ hai bị chặn, hai tài khoản trùng email bị chặn, nhiều tài khoản chưa có email vẫn hợp lệ. Chưa chạy trên SQL Server thật (nhóm chạy và báo lại).

## v2 — 31/10/2026

Phạm vi được nhóm duyệt: **G-01…G-15, DB-01, DB-03, DB-04, DB-06, DB-07, DB-10, DB-12, DB-13, DB-14, P3, Q-13…Q-16.**
**Không áp dụng DB-11** (thu hồi chỉ trừ tồn kho theo 2.3.3.3 — giữ ràng buộc RecallBatches). **Không sửa Chương 2.**

### 1. Tóm tắt số liệu

| Hạng mục | v1 | v2 | Chênh lệch |
|---|---|---|---|
| Bảng | 52 | 58 | +6 |
| Cột | 415 | 499 | +45 / −3 |
| Khóa ngoại | 94 | 112 | +18 |
| CHECK | 36 | 91 | +55 |
| CREATE INDEX | 18 | 118 | +100 |

> **Đính chính v1:** trước đây báo v1 có 421 cột theo Data Dictionary v1. Thực tế v1 có **415 cột**; file Data Dictionary v1 có 6 dòng lỗi (cột tên “REFERENCES” ở AspNetRoleClaims, AspNetUserClaims, AspNetUserLogins, AspNetUserRoles ×2, AspNetUserTokens).

### 2. Kết quả kiểm thử

**✅ Đã chạy thực tế trên SQL Server — 31/10/2026 (nhóm thực hiện):** SQL Server 2025 Express 17.0.1000 (`.\SQLEXPRESS`), database trống, chạy cả file trong SSMS → **“Query executed successfully”**. Kiểm tra sau khi chạy: **58 bảng, 499 cột, 112 FK, 91 CHECK; RolePermissions Owner 67 / Manager 57 / Pharmacist 17** — khớp 100%. Seed: 3 / 67 / 141 / 1 / 9 / 1 / 1 dòng.

4 cảnh báo (không phải lỗi) về độ dài khóa clustered > 900 byte: `PK_AspNetUserLogins` (1800), `PK_AspNetUserRoles` (1800), `PK_AspNetUserTokens` (2700) — bảng chuẩn Identity, có từ v1; `PK_RolePermissions` (904) — RoleId nvarchar(450) + int. Không ảnh hưởng vì giá trị thực là GUID 36 ký tự (~72 byte) và tên nhà cung cấp đăng nhập ngắn. **Quyết định: giữ nguyên.**

Các bước kiểm tra trước khi bàn giao (do Claude thực hiện):

Môi trường kiểm thử riêng của Claude không tải được SQL Server (kho packages.microsoft.com, mcr.microsoft.com, Docker Hub đều bị chặn), cũng không cài được thư viện phân tích SQL (PyPI/npm bị chặn). Đã thực hiện:

1. **Kiểm tra tĩnh** bằng bộ phân tích viết riêng cho cú pháp T-SQL dùng trong script: cú pháp từng cột/ràng buộc, cân ngoặc, chuỗi; FK trỏ tới bảng tạo trước, trỏ tới PK/UNIQUE, cùng kiểu dữ liệu; không vòng FK; không multiple cascade path; mọi FK có index với cột FK đứng đầu; không index trùng/thừa tiền tố; mọi cột `tinyint` có CHECK; mọi DEFAULT thời gian là `SYSDATETIME()`; tên constraint không trùng. **Kết quả: 0 lỗi, 0 cảnh báo** (cùng bộ kiểm tra chạy trên v1 báo 101 lỗi — đúng các điểm v2 đã sửa).
2. **Mô phỏng trên SQLite ** (dịch tự động script v2 sang SQLite: FK, CHECK, UNIQUE, filtered index, cột tính toán, seed). Tạo đủ 58 bảng; seed chạy 2 lần không trùng dữ liệu; **63/63 kịch bản nghiệp vụ đạt** (chi tiết mục 9).
3. **Đối chiếu tự động** với Bảng 2.3 (78 chức năng lá), Bảng 2.4 (SYS-01…SYS-14) và 3 vai trò — kết quả ở mục 7 và các sheet `BFD ↔ DB`, `Vai trò ↔ Quyền`, `SYS ↔ DB` trong Data Dictionary.

Những gì mô phỏng SQLite **không** chứng minh được: cú pháp riêng của SQL Server (`rowversion`, `INCLUDE`, `SET NOEXEC`, `PERSISTED`, biến bảng `@Perm`), giới hạn độ dài khóa index, thông báo lỗi multiple cascade path của SQL Server. Đề nghị nhóm chạy thử theo các bước ở mục 10.

### 3. Bảng mới (6)

| Bảng | Mã | Chức năng | Kho | Số cột | Nội dung |
|---|---|---|---|---|---|
| IngredientInteractions | G-06 | 3.1.3.1 | D1 | 6 | Cặp hoạt chất tương tác (IngredientId1 < IngredientId2), mức độ 1–3, mô tả, khuyến cáo. |
| MedicineContraindications | G-08 / Q-13 | 3.2.2.3 | D1 | 4 | Từ khóa chống chỉ định theo thuốc; so khớp với Diagnosis và ChronicConditions sau chuẩn hóa hoa/thường, dấu; chỉ cảnh báo. |
| Promotions | G-05 | 1.5.4, 3.3.1 | D1 | 12 | Khuyến mãi theo thuốc / nhóm / toàn bộ; đúng một trong % hoặc số tiền; thời gian hiệu lực. |
| StockDisposals | G-01 | 2.3.2 | D2 | 8 | Biên bản hủy thuốc (mã, kho, người lập, lý do, trạng thái). |
| StockDisposalDetails | G-01 | 2.3.2 | D2 | 7 | Dòng hủy theo lô: số lượng (đơn vị cơ sở), giá vốn, lý do 1–4. |
| PharmacySettings | G-02 | 1.5.3, 2.2.1.2, 2.5.1 | D4 | 5 | Cấu hình D4 dạng khóa–giá trị; seed `ExpiryWarningDays` = 90. |

### 4. Thay đổi trên bảng có sẵn

| Mã | Bảng.Cột | Trước (v1) | Sau (v2) | Lý do (Chương 2) | Ảnh hưởng code |
|---|---|---|---|---|---|
| DB-01 | Employees.UserId | UNIQUE(UserId) — chỉ cho 1 dòng NULL | Filtered unique index `UX_Employees_UserId WHERE UserId IS NOT NULL` | 1.3.1: nhiều nhân viên chưa có tài khoản | Không đổi Entity |
| DB-06 | Employees.CreatedAt, Medicines.CreatedAt, Customers.CreatedAt, CustomerAllergies.RecordedAt, AuditLogs.CreatedAt | DEFAULT SYSUTCDATETIME() | DEFAULT SYSDATETIME() | Báo cáo theo ngày không lệch 7 giờ | Ứng dụng dùng `DateTime.Now` |
| DB-12 | Employees.Gender, Customers.Gender | tinyint, không CHECK | CHECK IN (1,2,3) hoặc NULL | Toàn vẹn dữ liệu | Enum `Gender` |
| P3 | Permissions.IsSensitive | — | bit NOT NULL DEFAULT 0 | 1.3.3 quyền thao tác nhạy cảm, SYS-09 | Màn hình phân quyền nhóm quyền nhạy cảm |
| G-07 | Medicines.MaxSingleDose, MaxDailyDose, MinPatientAgeYears | — | decimal(18,3) NULL ×2, tinyint NULL; CHECK liều > 0, liều 1 lần ≤ liều 1 ngày, tuổi ≤ 120 | 3.2.2.2 kiểm tra liều (Pharmacist + Hệ thống) | Service cảnh báo liều |
| DB-13 | Medicines.DefaultSellingPrice / MedicineUnitConversions | Giá đơn vị cơ sở có thể nằm ở 2 nơi | Quy ước (comment trong script): DefaultSellingPrice = giá đơn vị cơ sở; MedicineUnitConversions chỉ chứa đơn vị khác; SellingPrice NULL = DefaultSellingPrice × ConversionToBase | 1.1.4, 3.1.2 | Service giá kiểm tra không thêm dòng quy đổi cho BaseUnitId |
| G-03 | StorageLocations | CabinetName, ShelfName (chữ tự do) | Bỏ 2 cột; thêm `LocationType` (1 Tủ, 2 Kệ), `ParentLocationId` (FK tự tham chiếu); CHECK Tủ không có cha, Kệ phải có cha | 1.5.1 “mã, tên tủ; mã, tên kệ và tủ trực thuộc” | Màn hình cây tủ–kệ; lô xếp vào Kệ (2.2.4) |
| G-12 | GoodsReceipts.Status | tinyint không định nghĩa | 0 Đang kiểm nhận (biên bản nhận hàng), 1 Đã nhập kho, 2 Hủy + CHECK | 2.2.1.3 → 2.2.2 | Enum `GoodsReceiptStatus` |
| G-12 / DB-03 | GoodsReceiptDetails | Có BatchId (vòng FK với MedicineBatches); SubTotal = QuantityReceived × UnitCost | Bỏ BatchId + FK; thêm ConversionToBase, BatchNumber, ManufactureDate, ExpiryDate, LocationId; SubTotal = (QuantityReceived − DamagedQuantity) × UnitCost; tạo bảng TRƯỚC MedicineBatches | 2.2.1.3 hàng hỏng không nhập; 2.2.3 số lô, NSX, HSD; 2.2.4 vị trí | Lô tìm dòng phiếu nhập qua MedicineBatches.GoodsReceiptDetailId |
| DB-03 / DB-04 / DB-07 / DB-12 | MedicineBatches | UQ(MedicineId, BatchNumber, WarehouseId); FK vòng; Status không định nghĩa | Bỏ UQ đó; FK GoodsReceiptDetailId khai báo trực tiếp + filtered unique (1 dòng phiếu nhập ↔ 1 lô); thêm `RowVersion`; Status 1–4 + CHECK; index FEFO mới (MedicineId, Status, ExpiryDate) INCLUDE(...); index (BatchNumber, MedicineId) | 2.2.3; nhập lại cùng số lô; SYS-10, SYS-14 | Entity có `byte[] RowVersion` → `IsRowVersion()` |
| DB-12 | InventoryTransactions | TransactionType, ReferenceType tự do | CHECK TransactionType 1–7; CHECK cặp (loại, ReferenceType, dấu số lượng) | D2 “lịch sử biến động tồn kho”, SYS-14 | Hằng số ReferenceType trong code (ENUMS.md) |
| G-04 / G-14 / DB-07 | Stocktakes | Không có phạm vi; chỉ ApprovedByEmployeeId | Thêm LocationId, CategoryId, ApprovedAt, ApprovalNote, AdjustedByEmployeeId, AdjustedAt, RowVersion; Status 0–4 + CHECK duyệt/từ chối/điều chỉnh | 2.4.1.1 phạm vi; QT5 Owner duyệt → Manager điều chỉnh | Quyền `F2.4.3-APPROVE` (Owner) và `F2.4.3` (Manager) |
| DB-10 | Shifts | Một nhân viên mở được nhiều ca | Thêm HandedOverAt; Status 0–2 + CHECK vòng đời; `UX_Shifts_OpenPerEmployee (EmployeeId) WHERE ClosedAt IS NULL` | 1.4.1–1.4.3 | Mở ca bắt lỗi trùng → báo “đang có ca mở” |
| Q-16 | ShiftClosings | Chỉ ExpectedCash/ActualCash | Thêm OpeningCash, TotalCashSales, TotalCashRefunds; CHECK ExpectedCash = OpeningCash + TotalCashSales − TotalCashRefunds | 1.4.3 chốt tiền đối chiếu doanh thu ca | Service chốt ca tính 3 giá trị rồi lưu |
| — | Prescriptions | CustomerId, DoctorName, ClinicName, PrescriptionDate, CheckedByEmployeeId cho phép NULL | NOT NULL; Status 0–2 + CHECK | Chỉ lưu đơn hợp lệ (3.2.3 sau 3.2.2); 3.2.2.1 cần bác sĩ, cơ sở, ngày kê; 4.3.3 cần khách hàng | Bán theo đơn phải chọn/tạo khách hàng |
| G-07 | PrescriptionDetails.DosePerTime, TimesPerDay | — | decimal(18,3) NULL, tinyint NULL; CHECK > 0 | 3.2.2.2 | Form đơn thuốc nhập liều dạng số (vẫn giữ Dosage/Frequency dạng chữ) |
| G-09 / G-10 / DB-07 | SalesInvoices | ShiftId NULL; không có HĐĐT, thông tin hủy | ShiftId NOT NULL; thêm EInvoiceNumber (filtered unique), EInvoiceStatus, EInvoiceIssuedAt, BuyerName, BuyerTaxCode, BuyerAddress, CancelledByEmployeeId, CancelledAt, CancelReason, RowVersion; Status 0–2 + CHECK hủy, HĐĐT | 3.1.4 ca hiện tại; 3.3.3 HĐĐT ghi D3; SYS-09 | Hóa đơn phải có ca mở; hủy cần quyền SYS09.CancelInvoice |
| G-05 | SalesInvoiceDetails.PromotionId | — | int NULL FK Promotions | 1.5.4 → 3.3.1 | Lưu khuyến mãi đã áp dụng; UnitPrice vẫn là giá lúc bán (Q-14) |
| P3 / DB-12 | Payments | PaymentMethod, Status không CHECK | Thêm TenderedAmount (chỉ tiền mặt, ≥ Amount); CHECK PaymentMethod 1–3, Status 1–2 | 3.3.2.1 tiền khách đưa → tiền thối | Enum `PaymentMethod`, `PaymentStatus` |
| G-11 / Q-16 | Returns | Không gắn ca, không có phương thức hoàn | Thêm ShiftId, RefundMethod (1–3), RefundReference; CHECK tiền mặt phải có ShiftId; Status 0–2 + CHECK | 3.4.3 Manager hoàn tiền; 1.4.3 chốt tiền | Hoàn tiền mặt lấy ca đang mở của chính người hoàn tiền |
| DB-12 | ReturnDetails.RestockStatus | Không CHECK | CHECK 0–2; CHECK đã nhập lại (1) phải có BatchId | 3.4.4 Manager nhập số lô | Enum `RestockStatus` |
| G-13 / DB-12 | SupplierPayments | Không có số phiếu | Thêm SupplierPaymentCode NOT NULL UNIQUE; CHECK PaymentMethod 1–2, Status 1–2 | 2.2.5 phiếu chi, 2.3.5 phiếu thu | Sinh mã PC…/PT… |
| DB-12 | PurchaseOrders.Status, SupplierReturns.Status, ProductRecalls.Status, CustomerReminders.Channel/Status, IntegrationLogs.Status | Không CHECK | CHECK theo ENUMS.md | 2.1.3, 2.3.1, 2.3.3, 3.5.2, 4.4 | Enum tương ứng |
| G-02 | InventoryPolicies.ExpiryWarningDays | int NOT NULL DEFAULT 90 | int NULL (NULL = dùng PharmacySettings.ExpiryWarningDays) | 1.5.3 chỉ nhập “số ngày cảnh báo” | Cảnh báo cận hạn: COALESCE(policy, setting) |
| G-05 | PricePolicies.ReturnConditions | — | nvarchar(1000) NULL; thêm CHECK ngày hiệu lực | 1.5.4 quy định đổi trả (thời hạn, điều kiện) | 3.4.2 hiển thị điều kiện |
| Q-14 | MedicinePriceHistories | Không có giá cũ, lý do | Thêm OldSellingPrice, ChangeReason; CHECK có giá cũ thì phải có lý do | 1.1.4 giá ban đầu (Manager); 1.5.4 đổi giá (Owner) | Đổi giá = đóng dòng cũ (EffectiveTo) + thêm dòng mới, trong transaction |
| P3 | CustomerReminders.DaysSupply | — | int NULL CHECK > 0 | 3.5.2.1 “số ngày dùng” | ReminderDate = ngày mua + DaysSupply − số ngày nhắc trước |
| G-15 | AspNetUserRoles | Một tài khoản có thể nhiều role | `UX_AspNetUserRoles_UserId` UNIQUE(UserId) | 1.3.4 mỗi tài khoản một vai trò | Đổi vai trò = xóa dòng cũ + thêm dòng mới |
| DB-14 | Toàn bộ | 18 index; nhiều FK không có index | 118 index; mọi FK có index (cột FK đứng đầu); không index trùng | SYS-12 | — |

### 5. Bị bỏ so với v1

| Đối tượng | Lý do |
|---|---|
| Cột StorageLocations.CabinetName, StorageLocations.ShelfName | G-03: thay bằng cây tủ–kệ (LocationType, ParentLocationId) |
| Cột GoodsReceiptDetails.BatchId + FK_GoodsReceiptDetails_Batches | DB-03: bỏ vòng FK; liên kết một chiều qua MedicineBatches.GoodsReceiptDetailId |
| Constraint UQ_Employees_User | DB-01: thay bằng filtered unique index UX_Employees_UserId |
| Constraint UQ_MedicineBatches_MedicineBatch | DB-04: cho phép cùng số lô ở nhiều phiếu nhập |
| ALTER TABLE thêm FK_MedicineBatches_ReceiptDetails | Khai báo trực tiếp trong CREATE TABLE (GoodsReceiptDetails được tạo trước) |
| Index IX_MedicineBatches_FEFO cũ (MedicineId, ExpiryDate, QuantityOnHand) | DB-14: tạo lại với (MedicineId, Status, ExpiryDate) INCLUDE(QuantityOnHand, WarehouseId, LocationId) |

### 6. Dữ liệu khởi tạo (seed)

Chạy lại không lỗi, không trùng (IF NOT EXISTS). **Không seed tài khoản/mật khẩu** — tài khoản Owner đầu tiên tạo qua ASP.NET Core Identity trong ứng dụng.

| Dữ liệu | Nội dung |
|---|---|
| AspNetRoles | 3 vai trò, Id cố định: Owner `7c1e5b0a-0001-…0001`, Manager `…0002-…0002`, Pharmacist `…0003-…0003` |
| Permissions | 67 = 62 chức năng lá BFD có vai trò thực hiện (`F<mã>`) + `F4.1.1-OWN`, `F2.4.3-APPROVE`, `SYS09.DiscountOverLimit`, `SYS09.CancelInvoice`, `SYS09.ViewAuditLog`. 16 chức năng chỉ do Hệ thống thực hiện không cần quyền. |
| RolePermissions | Owner 67 (toàn quyền) · Manager 57 (chức năng Manager + toàn bộ chức năng bán hàng của Pharmacist) · Pharmacist 17. Không cấp CanDelete cho Manager/Pharmacist. Pharmacist không có quyền F2.* hoặc F3.4.4 (SYS-13). |
| Warehouses | KHO01 — Kho nhà thuốc |
| Units | Viên, Vỉ, Hộp, Chai, Lọ, Tuýp, Gói, Ống, ml |
| PharmacySettings | ExpiryWarningDays = 90 *(giả định)* |
| PricePolicies | Chính sách mặc định: giảm tối đa 10%, đổi trả trong 7 ngày, điều kiện mẫu *(giả định)* |

### 7. Đối chiếu Chương 2

- **78 chức năng lá (Bảng 2.3):** 78/78 có bảng phục vụ và mọi bảng nằm đúng kho dữ liệu D1–D4 mà Bảng 2.3 ghi (bảng kỹ thuật AuditLogs, IntegrationLogs được phép). 3.2.1 không ghi CSDL đúng như Bảng 2.3. Chi tiết: sheet `BFD ↔ DB`.
- **3 vai trò:** 78/78 dòng khớp — quyền seed đúng cột “Vai trò thực hiện” + ghi chú “Owner toàn quyền, Manager được bán hàng như Pharmacist”; 2.4.3 Owner duyệt (`F2.4.3-APPROVE`); 4.1.1 Pharmacist chỉ doanh số cá nhân (`F4.1.1-OWN`). Chi tiết: sheet `Vai trò ↔ Quyền`.
- **SYS-01…SYS-14:** sheet `SYS ↔ DB` ghi cách DB v2 đáp ứng; SYS-02, 05, 07 không thuộc CSDL.
- **Mọi bảng** đều phục vụ ít nhất một chức năng lá hoặc yêu cầu SYS (không có bảng thừa).

### 8. Quy tắc phải cài ở tầng ứng dụng (CSDL không tự đảm bảo)

1. **[DB-07][SYS-14]** Trừ tồn bằng `UPDATE MedicineBatches SET QuantityOnHand = QuantityOnHand - @qty WHERE BatchId=@id AND Status=1 AND ExpiryDate > hôm nay AND QuantityOnHand >= @qty` trong transaction; `@@ROWCOUNT = 0` → rollback. Ghi InventoryTransactions trong cùng transaction.
2. **[Q-14]** Manager chỉ nhập giá khi tạo thuốc / thêm đơn vị quy đổi mới; sửa giá đã có chỉ Owner (`F1.5.4`), ghi MedicinePriceHistories (giá cũ, giá mới, người, thời điểm, lý do).
3. **[Q-15]** Khi Owner chỉnh quyền (1.3.3): từ chối cấp `F2.*` và `F3.4.4` cho Pharmacist; không thu hồi được quyền của Owner.
4. **[Q-16]** Hoàn tiền mặt: lấy ca `ClosedAt IS NULL` của chính người hoàn tiền; không có → từ chối. Chốt ca: TotalCashSales = tiền mặt thành công của hóa đơn thuộc ca; TotalCashRefunds = Returns tiền mặt thuộc ca.
5. **[Q-13]** Chống chỉ định: chuẩn hóa chữ thường + bỏ dấu tiếng Việt cho cả từ khóa và văn bản, khớp theo nguyên từ/cụm từ (không khớp chuỗi con), chỉ hiển thị cảnh báo.
6. Bán OTC (3.1) chặn thuốc `IsPrescription = 1`; mọi hóa đơn gắn ca đang mở của người bán.
7. Thanh toán đủ (tổng Payments Status=1 = TotalAmount) mới chuyển hóa đơn sang 1 và chạy FEFO; thiếu tồn → rollback toàn bộ (SYS-10, SYS-14).
8. 2.4.1.1 In phiếu kiểm kê chỉ đọc D2; bản ghi Stocktakes tạo khi nhập số lượng thực tế (2.4.1.2). Điều chỉnh tồn theo `Difference`.
9. 2.3.3.1 Tiếp nhận thu hồi đặt `MedicineBatches.Status = 3` ngay.
10. Chỉ hủy hóa đơn chờ thanh toán; hóa đơn đã thanh toán xử lý qua đổi trả.
11. Lô được xếp vào vị trí loại Kệ (LocationType = 2).

### 9. Kịch bản đã chạy trên mô phỏng SQLite

| Kết quả | Kịch bản | Phản hồi CSDL |
|---|---|---|
| PASS | DB-01 Hai nhân viên chưa có tài khoản (UserId NULL) | chấp nhận |
| PASS | DB-01 Hai nhân viên cùng một tài khoản | bị chặn: UNIQUE constraint failed |
| PASS | G-15 Gán vai trò thứ hai cho cùng tài khoản | bị chặn: UNIQUE constraint failed |
| PASS | DB-12 Giới tính ngoài danh sách | bị chặn: CHECK constraint failed |
| PASS | G-07 Liều 1 lần lớn hơn liều 1 ngày | bị chặn: CHECK constraint failed |
| PASS | G-06 Cặp tương tác đúng thứ tự | chấp nhận |
| PASS | G-06 Cặp tương tác ngược thứ tự | bị chặn: CHECK constraint failed |
| PASS | G-06 Mức độ ngoài danh sách | bị chặn: CHECK constraint failed |
| PASS | G-08 Trùng từ khóa chống chỉ định | bị chặn: UNIQUE constraint failed |
| PASS | G-05 Khuyến mãi vừa % vừa số tiền | bị chặn: CHECK constraint failed |
| PASS | G-05 Khuyến mãi hợp lệ | chấp nhận |
| PASS | Q-14 Đổi giá không có lý do | bị chặn: CHECK constraint failed |
| PASS | Q-14 Đổi giá có lý do | chấp nhận |
| PASS | G-03 Kệ thuộc Tủ 1 | chấp nhận |
| PASS | G-03 Kệ không có tủ cha | bị chặn: CHECK constraint failed |
| PASS | G-03 Tủ lại có cha | bị chặn: CHECK constraint failed |
| PASS | G-12 SubTotal = (10 − 1 hỏng) × 80.000 | 720000 |
| PASS | G-12 Tỷ lệ quy đổi bằng 0 | bị chặn: CHECK constraint failed |
| PASS | DB-04 Cùng số lô LOT001 ở phiếu nhập thứ hai | chấp nhận |
| PASS | DB-03 Hai lô cùng một dòng phiếu nhập | bị chặn: UNIQUE constraint failed |
| PASS | DB-12 Lô có trạng thái 9 | bị chặn: CHECK constraint failed |
| PASS | Ledger nhập mua đúng loại | chấp nhận |
| PASS | DB-12 Ledger xuất bán mà số lượng dương | bị chặn: CHECK constraint failed |
| PASS | DB-12 Ledger loại 4 (hủy) nhưng tham chiếu phiếu nhập | bị chặn: CHECK constraint failed |
| PASS | DB-07 Trừ 1000 khi lô chỉ còn 900 → 0 dòng bị cập nhật | rowcount=0 |
| PASS | DB-07 Tồn âm bị CHECK chặn | bị chặn: CHECK constraint failed |
| PASS | DB-10 Mở ca thứ hai khi ca 1 chưa chốt | bị chặn: UNIQUE constraint failed |
| PASS | DB-10 Chốt ca mà thiếu ClosedAt | bị chặn: CHECK constraint failed |
| PASS | 1.4.2 Bàn giao ca thiếu thời điểm | bị chặn: CHECK constraint failed |
| PASS | Đơn thuốc thiếu khách hàng | bị chặn: NOT NULL constraint failed |
| PASS | Đơn thuốc hợp lệ | chấp nhận |
| PASS | 3.1.4 Hóa đơn không gắn ca | bị chặn: NOT NULL constraint failed |
| PASS | G-10 Hủy hóa đơn không ghi lý do | bị chặn: CHECK constraint failed |
| PASS | G-09 Đánh dấu đã xuất HĐĐT mà thiếu số | bị chặn: CHECK constraint failed |
| PASS | Q-14 Đổi giá hiện hành không ảnh hưởng hóa đơn cũ | chấp nhận |
| PASS | Q-14 UnitPrice của hóa đơn vẫn 9.500 |  |
| PASS | 3.3.2.3 Thanh toán kết hợp: tiền mặt | chấp nhận |
| PASS | 3.3.2.3 Thanh toán kết hợp: chuyển khoản | chấp nhận |
| PASS | P3 Tiền khách đưa ghi cho thanh toán chuyển khoản | bị chặn: CHECK constraint failed |
| PASS | P3 Tiền khách đưa nhỏ hơn số tiền | bị chặn: CHECK constraint failed |
| PASS | DB-12 Phương thức thanh toán 7 | bị chặn: CHECK constraint failed |
| PASS | Ledger xuất bán FEFO | chấp nhận |
| PASS | Q-16 Hoàn tiền mặt khi người hoàn tiền không có ca | bị chặn: CHECK constraint failed |
| PASS | Q-16 Hoàn tiền mặt gắn ca của Manager | chấp nhận |
| PASS | Q-16 Hoàn chuyển khoản không cần ca | chấp nhận |
| PASS | 3.4.4 Đánh dấu đã nhập lại mà chưa có số lô | bị chặn: CHECK constraint failed |
| PASS | 3.4.4 Nhập lại có số lô | chấp nhận |
| PASS | Q-16 Chốt ca sai công thức tiền mặt | bị chặn: CHECK constraint failed |
| PASS | Q-16 Chốt ca Manager: 0 + 50.000 − 9.500 = 40.500 | chấp nhận |
| PASS | ShiftClosings.CashDifference = −500 |  |
| PASS | G-14 Duyệt kiểm kê thiếu người duyệt | bị chặn: CHECK constraint failed |
| PASS | G-14 Từ chối không ghi lý do | bị chặn: CHECK constraint failed |
| PASS | G-14 Owner duyệt | chấp nhận |
| PASS | G-14 Chuyển 'đã điều chỉnh' thiếu người điều chỉnh | bị chặn: CHECK constraint failed |
| PASS | StocktakeDetails.Difference = −2 |  |
| PASS | Ledger điều chỉnh kiểm kê | chấp nhận |
| PASS | G-01 Biên bản hủy: lý do hợp lệ | chấp nhận |
| PASS | G-01 Lý do hủy ngoài danh sách | bị chặn: CHECK constraint failed |
| PASS | 2.3.3 Thu hồi vượt số tồn lúc thu hồi | bị chặn: CHECK constraint failed |
| PASS | G-13 Phiếu chi có mã | chấp nhận |
| PASS | G-13 Trùng mã phiếu chi | bị chặn: UNIQUE constraint failed |
| PASS | Xóa phiếu nhập đã sinh lô bị chặn | bị chặn: FOREIGN KEY constraint failed |
| PASS | PharmacySettings: ngưỡng cận hạn mặc định | chấp nhận |

### 10. Hướng dẫn nhóm chạy thử trên SQL Server

1. Trên máy cá nhân (không phải DB dùng chung), mở SSMS, kết nối SQL Server 2019/2022 hoặc Express.
2. Nếu đã có PharmacyManagementDB v1 trên máy đó: bỏ comment khối “(TÙY CHỌN) Xóa DB cũ” ở đầu script. Script tự dừng nếu DB đã có bảng.
3. Mở `PharmacyManagementDB.sql`, nhấn Execute (F5) chạy cả file. Kỳ vọng: “Commands completed successfully”, không có dòng đỏ.
4. Kiểm tra nhanh:

```sql
USE PharmacyManagementDB;
SELECT COUNT(*) AS SoBang FROM sys.tables;                                   -- 58
SELECT COUNT(*) AS SoCot  FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id;  -- 499
SELECT COUNT(*) AS SoFK   FROM sys.foreign_keys;                             -- 112
SELECT COUNT(*) AS SoCheck FROM sys.check_constraints;                        -- 91
SELECT r.Name, COUNT(*) AS SoQuyen FROM RolePermissions rp JOIN AspNetRoles r ON r.Id = rp.RoleId GROUP BY r.Name;  -- Owner 67, Manager 57, Pharmacist 17
```

5. Gửi lại thông báo lỗi (nếu có) kèm số dòng; mình sửa script và cập nhật changelog.

### 11. Giả định và điểm cần nhóm xác nhận

1. ~~Chưa chạy trên SQL Server~~ — đã chạy thành công (mục 2).
2. Giá trị seed là giả định: ngưỡng cận hạn 90 ngày; giảm giá tối đa 10%; đổi trả trong 7 ngày; nội dung điều kiện đổi trả; danh sách đơn vị tính.
3. `Prescriptions.CustomerId` NOT NULL: mọi lần bán theo đơn phải có hồ sơ khách hàng (để lập sổ 4.3.3).
4. 3.4.3 (Bảng 2.3 ghi “Ghi D3”) — theo Q-16 hệ thống đọc thêm ca đang mở (Shifts, D4) để ghi Returns.ShiftId. Đây là chi tiết cài đặt theo quyết định Q-16, Chương 2 không thay đổi.
5. Quyền CRUD mặc định: Manager/Pharmacist có Xem/Thêm/Sửa với chức năng thao tác, chỉ Xem với báo cáo; không ai ngoài Owner có CanDelete. Owner chỉnh được trong 1.3.3.
6. Identity của .NET 10: script giữ 7 bảng AspNet* như v1. Khi dựng khung project sẽ cấu hình Identity đúng schema này (nếu template .NET 10 bật schema có passkey thì phải tắt hoặc bổ sung bảng — sẽ xử lý và báo lại ở bước dựng khung).

### 12. Đề xuất thêm (CHƯA đưa vào script — cần nhóm duyệt)

1. `MedicineBatches.UnitCost decimal(18,2)` là giá vốn trên đơn vị cơ sở; với hộp 100.000đ/30 viên sẽ làm tròn 3.333,33đ → sai số lợi nhuận nhỏ. Có thể đổi sang `decimal(18,4)` (đổi kiểu dữ liệu nên cần duyệt).
2. `ReturnDetails`: thêm `RestockedByEmployeeId`, `RestockedAt` để truy vết người xử lý 3.4.4 khi dòng bị từ chối nhập (trường hợp nhập lại đã có InventoryTransactions).
3. Ràng buộc 1 hóa đơn = tiền của đúng ca lập hóa đơn: nếu hóa đơn lập ở ca này nhưng thanh toán ở ca sau, tiền mặt vẫn tính cho ca lập. Nếu muốn tính theo ca thu tiền, cần thêm `Payments.ShiftId`.

### 13. File liên quan

- `PharmacyManagementDB.sql` (v2, thay thế nội dung v1) · `PharmacyManagement_DataDictionary.xlsx` (v2) · `ENUMS.md`
- Bản sao lưu v1: `_backup_v1/PharmacyManagementDB_v1.sql`, `_backup_v1/PharmacyManagement_DataDictionary_v1.xlsx`

## v1

- Bản thiết kế ban đầu: 52 bảng, 415 cột (Data Dictionary v1 ghi 421 do 6 dòng lỗi).
