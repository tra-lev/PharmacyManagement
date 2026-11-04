# PROJECT_CONTEXT — Website Quản lý Hiệu thuốc (ASP.NET Core MVC)

> **Cách dùng:** Khi mở cuộc trò chuyện mới, gửi lại file này kèm `PharmacyManagementDB.sql`. Claude đọc file này trước, coi mục **0** và **6** là bắt buộc, và cập nhật file sau mỗi mốc (quyết định mới, chức năng xong, đổi DB).
>
> Phiên bản: **v1.4.3** — 04/11/2026 — Giai đoạn 1 hoàn tất. Giai đoạn 2: khung + module tài khoản + giao diện nền (build 0 lỗi C#, 4 cảnh báo NuGet — Q-27). Giai đoạn 3: **Danh mục thuốc (DEC-24) nhóm đã thử xong**; **Nhà cung cấp + Khách hàng (DEC-25), tìm kiếm trực tiếp không dấu (DEC-26), Ca làm việc (DEC-27), ô nhập tiền 200.000 đ (DEC-28), dòng đếm "Tìm thấy X / Y" (DEC-29), Nhập kho (DEC-30), Ca toàn nhà thuốc (DEC-31) đã code, chờ nhóm build và thử**. Nhóm đã chạy được Ca làm việc (mở/bàn giao/chốt) 04/11. Chưa commit phần Ca, ô tiền, đếm, Nhập kho: build 0 lỗi + thử xong thì commit lên `develop`. Repo GitHub (Private) `tra-lev/PharmacyManagement`: đã đẩy `main` 03/11; nhánh `develop` tạo trên máy.

---

## 0. NGUYÊN TẮC TỐI THƯỢNG

**BFD (Hình 2.3) và Chương 2 bản chốt (Hình 2.2 "Sơ đồ tổ chức và phân quyền", Bảng 2.2, QT1–QT8, Bảng 2.3 – 78 chức năng lá, Bảng 2.4 SYS-01…SYS-14) KHÔNG SỬA.**
Mọi khác biệt → sửa **hệ thống** (phân quyền, DB, code) cho khớp. Vai trò thực hiện, dữ liệu vào/ra và kho D1–D4 trong Bảng 2.3 là đặc tả bắt buộc.
(Bản Chương 2 cũ với "Thủ kho / Nhân viên bán hàng / Dược sĩ phụ trách chuyên môn" đã **bị thay thế**, không dùng nữa.)

## 1. Thông tin dự án
| Mục | Nội dung |
|---|---|
| Đề tài | Website quản lý hiệu thuốc — đồ án 3 tín chỉ, nhóm 5 người, định hướng nâng cao |
| Nền tảng | **.NET 10 LTS**, ASP.NET Core MVC, C#, **EF Core 10**, ASP.NET Core Identity |
| CSDL | SQL Server — **Database-first**: script SQL là nguồn gốc; EF Core chỉ scaffold Entity + DbContext. Không Code-first, không dùng Migrations quản lý schema |
| Frontend | Razor View, Bootstrap, JS thuần |
| Thư mục dự án | `E:\LapTrinhWeb\BTL_AppQuanLiNhaThuoc` (máy "omee"); solution tạo **sau khi chốt DB** |
| Tài liệu gốc | Chương 2 bản chốt; `PharmacyManagementDB.sql`; `PharmacyManagement_DataDictionary.xlsx`; master prompt (trái Chương 2 thì Chương 2 thắng) |

## 2. Vai trò (theo Hình 2.2 + Bảng 2.3 bản chốt)
**3 vai trò cố định, khởi tạo một lần (1.3.2); mỗi tài khoản đúng 1 vai trò (1.3.4); Owner điều chỉnh quyền của từng vai trò (1.3.3, gồm quyền thao tác nhạy cảm).**

| Vai trò | Chức năng lá trực tiếp thực hiện |
|---|---|
| **Owner** (Chủ nhà thuốc) | Toàn quyền. Trực tiếp: 1.3.1–1.3.5, **1.5.4 chính sách giá**, **duyệt điều chỉnh tồn (2.4.3)**; xem toàn bộ báo cáo |
| **Manager** (Quản lý kiêm thủ kho) | 1.1.x, 1.2.1, 1.2.2, 1.5.1–1.5.3, toàn bộ nhóm 2 (2.4.3 sau khi Owner duyệt), **3.4.2, 3.4.3, 3.4.4**, 4.1.1, 4.1.2, 4.2.x, 4.3.1–4.3.3, 4.4.2.3; **được bán hàng như Pharmacist** |
| **Pharmacist** (Dược sĩ bán hàng) | 1.2.2, 1.4.1–1.4.3, 3.1.x, 3.2.x, 3.3.x, 3.4.1, 3.5.2.1, 4.1.1 (**doanh số cá nhân**); chỉ xem tồn kho |
| **Hệ thống** | 2.3.4.x (FEFO), 2.5.1, 2.5.2, 3.1.2, 3.1.3.x, 3.2.2.2 (cùng Pharmacist), 3.2.2.3, 3.3.1, 3.3.3, 3.5.1, 3.5.2.2, 4.4.1, 4.4.2.1, 4.4.2.2 |

- SYS-13: Owner và Manager dùng được cả bán hàng và kho; Pharmacist không nhập, xuất thủ công, kiểm kê hay điều chỉnh tồn; quyền kiểm tra ở server.
- Nguyên tắc 3: chỉ Manager, Owner hoặc Hệ thống (xuất bán FEFO) thay đổi tồn kho.

## 3. Nghiệp vụ (BFD — Hình 2.3, không đổi)
4 mức 0 · 19 mức 1 · 63 mức 2 · 24 mức 3 · **78 chức năng lá**.
- QT1 Bán OTC · QT2 Bán theo đơn · QT3 Đặt hàng–nhập kho · QT4 Xuất ngoài bán (trả NCC, hủy, thu hồi)
- **QT5 Kiểm kê: in phiếu → đếm, nhập thực tế → biên bản → Owner duyệt → Manager điều chỉnh**
- **QT6 Đổi trả: Pharmacist tiếp nhận → Manager kiểm tra điều kiện → Manager hoàn tiền (hàng "chờ nhập") → Manager nhập lại**
- QT7 Ca · QT8 Báo cáo–sổ sách–liên thông
- Phạm vi: HĐĐT (3.3.3) và liên thông (4.4) mô phỏng; kiểm tra an toàn (3.1.3, 3.2.2) chỉ hỗ trợ, Pharmacist quyết định.

### Kho dữ liệu D1–D4 ↔ bảng
| Kho | Nội dung | Bảng |
|---|---|---|
| D1 Danh mục | Thuốc (hoạt chất, liều dùng, chống chỉ định, tương tác, quy đổi, giá), NCC, KH, tủ kệ, chính sách giá & đổi trả | Categories, Units, Manufacturers, Medicines, ActiveIngredients, MedicineIngredients, MedicineUnitConversions, Suppliers, Customers, Warehouses, StorageLocations, PricePolicies, MedicinePriceHistories (+ đề xuất IngredientInteractions, MedicineContraindications, Promotions) |
| D2 Kho thuốc | Đơn đặt, phiếu nhập, tồn theo lô & vị trí, phiếu xuất (trả/hủy/thu hồi/bán), kiểm kê, **lịch sử biến động tồn**, sổ nhiệt độ–độ ẩm, công nợ NCC | PurchaseOrders(+D), GoodsReceipts(+D), MedicineBatches, **InventoryTransactions**, SalesIssueAllocations, SupplierReturns(+D), SupplierPayments, ProductRecalls, RecallBatches, Stocktakes(+D), StorageEnvironmentLogs (+ đề xuất StockDisposals(+D)) |
| D3 Giao dịch bán | Đơn thuốc, hóa đơn, thanh toán, đổi trả, lịch sử mua, dị ứng, lịch nhắc | Prescriptions(+D), SalesInvoices(+D), Payments, Returns(+D), CustomerAllergies, CustomerReminders |
| D4 Quản trị | Tài khoản, vai trò, quyền; ca & chốt tiền; định mức tồn, ngưỡng cận hạn | AspNet* (7), Employees, Permissions, RolePermissions, Shifts, ShiftClosings, InventoryPolicies (+ đề xuất PharmacySettings) |
| Kỹ thuật | Ngoài BFD | AuditLogs (SYS-09), IntegrationLogs (3.3.3, 3.5.2.2, 4.4) |

Dẫn xuất, không cần bảng: lịch sử mua (3.5.1), cảnh báo (2.5.1–2.5.2), sổ GPP (4.3), phiếu xuất bán (= SalesIssueAllocations), yêu cầu đổi trả 3.4.1 (chỉ đọc D3).

## 4. Database hiện tại — v2
- File: `PharmacyManagementDB.sql` **v2** (UTF-8 BOM, CRLF) — **58 bảng, 499 cột, 112 FK, 91 CHECK, 118 index**. Chỉ chạy trên DB trống (script tự dừng nếu DB đã có bảng).
- v1 đã sao lưu: `_backup_v1/PharmacyManagementDB_v1.sql`, `_backup_v1/PharmacyManagement_DataDictionary_v1.xlsx`. **Đính chính:** v1 có 52 bảng, **415 cột** (Data Dictionary v1 ghi 421 do 6 dòng lỗi “REFERENCES”).
- Tài liệu đi kèm: `CHANGELOG_DB.md` (thay đổi, kiểm thử, đối chiếu, giả định), `ENUMS.md` (giá trị trạng thái ↔ enum C# `: byte`), `PharmacyManagement_DataDictionary.xlsx` v2 (9 sheet, có cột D1–D4, chức năng BFD, đối chiếu BFD/vai trò/SYS).
- 6 bảng mới: IngredientInteractions, MedicineContraindications, Promotions, StockDisposals, StockDisposalDetails, PharmacySettings. Bỏ: StorageLocations.CabinetName/ShelfName, GoodsReceiptDetails.BatchId.
- Cột tính toán PERSISTED (EF chỉ đọc): GoodsReceiptDetails.SubTotal (= (QuantityReceived − DamagedQuantity) × UnitCost), StocktakeDetails.Difference, ShiftClosings.CashDifference, SupplierReturnDetails.LineTotal. Cột `rowversion`: MedicineBatches, SalesInvoices, Stocktakes.RowVersion.
- Seed: 3 role Id cố định (Owner `7c1e5b0a-0001-4a6f-9b1e-000000000001`, Manager `…0002…0002`, Pharmacist `…0003…0003`); 67 Permissions (62 lá BFD `F<mã>` + `F4.1.1-OWN`, `F2.4.3-APPROVE`, `SYS09.DiscountOverLimit`, `SYS09.CancelInvoice`, `SYS09.ViewAuditLog`); RolePermissions Owner 67 / Manager 57 / Pharmacist 17; kho KHO01; 9 đơn vị; PharmacySettings.ExpiryWarningDays = 90; 1 PricePolicies (10%, 7 ngày). **Không seed tài khoản.**
- Kiểm thử: kiểm tra tĩnh 0 lỗi; mô phỏng SQLite 63/63 kịch bản đạt; đối chiếu 78/78 lá BFD đúng kho D1–D4, 78/78 vai trò khớp, SYS-01…14 đã ánh xạ. **Đã chạy thực tế** trên SQL Server 2025 Express 17.0.1000 (máy nhóm, 31/10/2026): thành công, đếm lại 58 bảng / 499 cột / 112 FK / 91 CHECK / quyền 67-57-17 khớp. 4 cảnh báo độ dài khóa clustered (3 bảng Identity + PK_RolePermissions 904 byte) — vô hại, giữ nguyên.

### 4.1 Dữ liệu mẫu
- File `PharmacyManagementDB_SampleData.sql` (chạy SAU script v2, trên DB vừa tạo) + hướng dẫn `SAMPLE_DATA.md`.
- 9.055 dòng / 44 bảng, sinh bằng mô phỏng nghiệp vụ 01/05/2026 → 31/10/2026 21:15: 101 thuốc, 10 NCC, 80 khách (18 mạn tính, 8 dị ứng), 6 nhân viên (UserId NULL), 644 hóa đơn, 206 lô, 1.319 dòng sổ kho, 368 ca.
- Quy ước tính tiền dùng khi sinh dữ liệu (ứng dụng phải làm giống): LineTotal = Quantity × UnitPrice − DiscountAmount; SubTotal = Σ Quantity × UnitPrice; DiscountAmount (hóa đơn) = Σ chiết khấu dòng; TotalAmount = SubTotal − DiscountAmount + TaxAmount (TaxAmount = 0, giá đã gồm VAT); chiết khấu làm tròn XUỐNG 100đ, không vượt PricePolicies.MaxDiscountPercent; Promotions.DiscountAmount = số tiền giảm trên mỗi đơn vị cơ sở; tiền hoàn khách làm tròn xuống đến đồng.
- Kiểm tra nhất quán trên mô phỏng ràng buộc: 28/28 đạt (tồn lô = sổ kho, FEFO, tiền ca, Q-14 giá theo thời điểm, Q-16, OTC/Rx, dị ứng, liều…). **Đã chạy thành công trên SQL Server** (nhóm xác nhận 02/11/2026).

## 5. Quy tắc nghiệp vụ cốt lõi
1. Tồn theo **lô**, đơn vị cơ sở; không gộp lô.
2. Quy đổi khi bán (3.1.2); lưu ConversionToBase và UnitPrice tại thời điểm giao dịch.
3. Hóa đơn lưu ở trạng thái **"chờ thanh toán"** (3.1.4/3.2.4) → thanh toán 3.3 (một hoặc nhiều hình thức) → **khi đủ tiền**, trong 1 transaction (SYS-14): FEFO (2.3.4.1) → trừ tồn theo lô + ghi InventoryTransactions (2.3.4.2) → lịch sử mua (3.5.1).
4. SYS-10: không xuất lô hết hạn, đang thu hồi, bị khóa; không đủ tồn thì từ chối.
5. Xuất kho chỉ gồm: bán (Hệ thống), trả NCC (2.3.1), hủy (2.3.2), thu hồi (2.3.3).
6. Mọi biến động tồn → InventoryTransactions + cập nhật lô trong cùng transaction; chống bán trùng lô giữa nhiều quầy.
7. Người bán (Pharmacist/Manager/Owner) phải có ca đang mở; hóa đơn gắn ca. Bán OTC (3.1) chặn thuốc kê đơn.
8. Đơn thuốc: 3.2.1 không ghi DB → 3.2.2 kiểm tra → 3.2.3 mới lưu.
9. Đổi trả: 3.4.1 Pharmacist chỉ tra hóa đơn gốc → 3.4.2/3.4.3 Manager kiểm tra, hoàn tiền (hàng "chờ nhập") → 3.4.4 Manager nhập số lô, kiểm tra, nhập lại tồn hoặc từ chối.
10. Thu hồi: tiếp nhận công văn → khóa lô ngay → tra cứu tồn/đã bán → phiếu thu hồi trừ tồn.
11. Kiểm kê: Manager lập → **Owner duyệt** → Manager điều chỉnh theo Difference.
12. Kiểm tra an toàn chỉ cảnh báo; Pharmacist quyết định.
13. Không xóa cứng chứng từ đã ghi sổ (SYS-06); thao tác nhạy cảm ghi AuditLogs (SYS-09).
14. Tác vụ "Hệ thống" chạy ngầm (3.5.2.2, 4.4.x): BackgroundService + IntegrationLogs, gửi lại khi lỗi.
15. SYS-11: chạy trên máy chủ LAN; mất Internet chỉ tạm dừng liên thông.

## 6. Quyết định đã chốt
| Mã | Quyết định | Ngày |
|---|---|---|
| DEC-01 | ASP.NET Core MVC + Razor + SQL Server + EF Core + Identity | 31/10/2026 |
| DEC-02 | Không đổi tên/bỏ bảng, cột khi chưa được nhóm đồng ý; thay đổi DB phải có giải thích | 31/10/2026 |
| DEC-03 | HĐĐT, liên thông Dược QG, SMS nhắc lịch: mô phỏng, ghi IntegrationLogs | 31/10/2026 |
| DEC-04 | Code sinh vào `E:\LapTrinhWeb\BTL_AppQuanLiNhaThuoc`; chưa tạo project tới khi chốt DB | 31/10/2026 |
| DEC-05 | **Chương 2 bản chốt + BFD không sửa; hệ thống phải khớp** | 31/10/2026 |
| DEC-06 | **.NET 10 LTS, EF Core 10, Database-first** (scaffold từ DB; schema đổi bằng script SQL) | 31/10/2026 |
| DEC-07 | 3 role cố định Owner/Manager/Pharmacist, mỗi tài khoản 1 role; Manager được bán hàng; Owner duyệt kiểm kê; Pharmacist xem doanh số cá nhân (theo Chương 2 bản chốt) | 31/10/2026 |
| DEC-08 | Q-13: chống chỉ định so từ khóa (bảng MedicineContraindications) với Diagnosis + ChronicConditions; chuẩn hóa hoa/thường và dấu; khớp theo nguyên từ/cụm từ (không khớp chuỗi con); chỉ cảnh báo, Pharmacist quyết định | 31/10/2026 |
| DEC-09 | Q-14: Manager nhập giá ban đầu (1.1.4, gồm giá cho đơn vị quy đổi mới thêm); sau đó chỉ Owner đổi giá (1.5.4); mỗi lần đổi ghi MedicinePriceHistories (giá cũ, giá mới, người, thời điểm, lý do); hóa đơn giữ UnitPrice lúc bán | 31/10/2026 |
| DEC-10 | Q-15: khóa cứng ở server — Pharmacist không thể được cấp quyền nhập, xuất thủ công, kiểm kê, điều chỉnh tồn (SYS-13) | 31/10/2026 |
| DEC-11 | Q-16: hoàn tiền mặt gắn ca đang mở của chính người hoàn tiền (không có ca thì không được hoàn tiền mặt); chuyển khoản/thẻ ghi đúng phương thức, không tính vào tiền mặt của ca | 31/10/2026 |
| DEC-13 | **DB v2 chốt** (58 bảng, 499 cột) — là nguồn schema cho scaffold; giữ nguyên 4 cảnh báo độ dài khóa (không đổi PK_RolePermissions) | 31/10/2026 |
| DEC-14 | Dữ liệu mẫu: phạm vi danh mục + giao dịch, quy mô lớn (~100 thuốc, 80 khách, 6 tháng); file riêng, không trộn vào script schema; không seed tài khoản | 31/10/2026 |
| DEC-12 | **Không sửa Chương 2 dưới bất kỳ hình thức nào**; quy tắc chi tiết của hệ thống ghi trong tài liệu thiết kế hệ thống (PROJECT_CONTEXT, đặc tả use case, chương thiết kế), không ghi đè Chương 2 | 31/10/2026 |
| DEC-15 | Bước 2.1 được duyệt ("ok"): `tools/01-create-solution.ps1` (global.json 10.0.100, sln, MVC net10.0, EF Core/Identity 10.0.14, tool manifest dotnet-ef 10.0.14, user-secrets, appsettings ConnectionStrings:PharmacyDb), `tools/02-scaffold.ps1` (51 bảng, DbContext kế thừa IdentityDbContext), 25 enum `: byte`, `PermissionCodes` (67 mã), `RoleNames`, README. File UTF-8 BOM + CRLF, đã kiểm tra hash trên máy | 02/11/2026 |
| DEC-16 | Quy tắc làm việc: Claude chỉ code khi nhóm nói "ok"; không tự cài package hay đổi cấu hình quan trọng khi chưa hỏi; nhóm tự đưa mã lên GitHub khi thấy ổn | 02/11/2026 |
| DEC-17 | Giao diện dùng UI/UX Pro Max Skill để dựng design system; vì skill không có stack Razor/Bootstrap nên áp nguyên tắc chung + hướng dẫn Laravel, chuyển sang Bootstrap 5 + CSS custom properties; mọi màu kiểm tra tương phản WCAG AA; font/icon tự host (SYS-11) | 02/11/2026 |
| DEC-31 | Ca toàn nhà thuốc, nhóm "ok" 04/11 (bổ sung DEC-27 sau khi nhóm báo Owner không thấy ca của dược sĩ). Người có **F4.1.1** (Owner, Manager) xem `/Sales/Shifts/All`: mọi ca, mọi nhân viên, lọc nhân viên/trạng thái, tìm mã ca/họ tên (không dấu, tìm khi gõ), tiền theo sổ/thực đếm/chênh lệch từ ShiftClosings; chi tiết ca của người khác **chỉ xem** (không mở, bàn giao, chốt hộ). Không có F4.1.1 thì chỉ xem ca của mình. Không thêm mã quyền | 04/11/2026 |
| DEC-30 | Nhập kho 2.2.1.1–2.2.4, nhóm "ok" 04/11 — Area **Inventory**, `/Inventory/Receipts`. (1) Quyền: xem cần F2.2.1.1 hoặc F2.2.2; lập/sửa/hủy F2.2.2; **Nhập kho cần đủ F2.2.2 + F2.2.3 + F2.2.4** (Manager, Owner; Pharmacist khóa SYS-13). (2) Mã `PN` + yyMMdd + 3 số/ngày. Phiếu có thể theo đơn đặt hàng (chỉ đơn Chờ giao/Giao thiếu; chọn đơn → điền sẵn dòng còn thiếu, đơn giá dự kiến; NCC theo đơn; thuốc + đơn vị của dòng đơn khóa) hoặc **ngoài đơn** (PurchaseOrderId NULL); đơn của phiếu không đổi sau khi lập. (3) Dòng: thuốc đang kinh doanh, đơn vị nhập = đơn vị cơ sở hoặc đơn vị quy đổi (hệ số lấy từ MedicineUnitConversions, lưu ConversionToBase), số nhận/hỏng (≤ 3 số lẻ), đơn giá [Money], số lô (chữ không dấu, số, . / - _ ≤ 60), NSX ≤ hôm nay, HSD > hôm nay và > NSX, kệ (LocationType = Kệ), ghi chú chất lượng **bắt buộc khi có hàng hỏng**. Dòng theo đơn: Σ hàng tốt (nhận − hỏng) ≤ số còn thiếu (**chặn**; giao dư → dòng ngoài đơn). (4) **Đang kiểm nhận** (Status 0): lưu/sửa nhiều lần (thay toàn bộ dòng), số lô/HSD/kệ để trống được; **Hủy** (Status 2) cần lý do, ghi vào Notes, AuditLogs GOODS_RECEIPT_CANCEL; không xóa. (5) **Nhập kho** trong 1 transaction: `ExecuteUpdate` Status 0→1 có điều kiện (chống bấm 2 lần); kiểm tra đủ lô/HSD/kệ cho dòng có hàng tốt; mỗi dòng hàng tốt > 0 sinh 1 lô: kho đang hoạt động đầu tiên (KHO01), QuantityOnHand = (nhận − hỏng) × hệ số (làm tròn 3), UnitCost/đơn vị cơ sở = đơn giá ÷ hệ số (làm tròn 2 — Q-18), ReceivedDate = hôm nay, Status Khả dụng; InventoryTransactions loại 1 `GoodsReceiptDetail`, Notes "Nhập kho — PN…"; PurchaseOrderDetails.ReceivedQuantity += hàng tốt bằng UPDATE có điều kiện ≤ OrderedQuantity (vượt → rollback); đơn → Đã giao nếu đủ, Giao thiếu nếu còn thiếu; TotalAmount = Σ (nhận − hỏng) × đơn giá; AuditLogs GOODS_RECEIPT_POST. Đã nhập kho thì không sửa/hủy (trả NCC ở 2.3.1). (6) Hạn dùng ≤ PharmacySettings.ExpiryWarningDays (90): **cảnh báo, vẫn nhập**; hết hạn: chặn. (7) Ngoài phạm vi lần này: lập đơn đặt hàng 2.1.x, thanh toán NCC 2.2.5. (8) Bổ sung 04/11 (nhóm "ok"): phiếu theo đơn gửi kèm SupplierId ẩn (server vẫn dùng NCC của đơn); **ô chọn thuốc gõ chữ để tìm** — `wwwroot/js/combobox.js` dùng chung cho mọi `<select data-pm-combobox>` (tìm không dấu theo tên, mã, từ khóa `data-search` = tên hoạt chất; tên bắt đầu bằng chữ gõ lên trước; tối đa 50 gợi ý; ↑ ↓ Enter Esc; select gốc ẩn vẫn gửi giá trị; dòng thêm động tự áp dụng; không JS vẫn là danh sách thả xuống). Màn hình Bán hàng tìm thuốc dùng lại cơ chế này | 04/11/2026 |
| DEC-29 | Dòng đếm trên trang danh sách (Thuốc, NCC, Khách hàng; áp dụng cả Nhập kho), nhóm "ok" 04/11: số đếm thật từ CSDL. Không lọc → "Y khách hàng" (Y = tổng bản ghi mọi trạng thái); mặc định chỉ hiện đang hoạt động mà có bản ghi đã ngừng → "X đang hoạt động / Y khách hàng"; đang tìm/lọc → "Tìm thấy X / Y khách hàng". `GrandTotal` trong list view model. Không có xóa khách hàng/thuốc/NCC (SYS-06, DEC-24/25) — "Ngừng" làm giảm số đang hoạt động | 04/11/2026 |
| DEC-28 | Ô nhập tiền, nhóm "ok" 03/11. (1) Mọi ô nhập **số tiền** là ô chữ có `data-pm-money`: `wwwroot/js/money-input.js` (nạp toàn cục ở `_Scripts.cshtml`) tự thêm dấu chấm hàng nghìn khi gõ (200000 → 200.000), giữ vị trí con trỏ, chữ **đ** đặt bên phải ô (input-group); dán 200,000 / 200 000 / 200.000đ đều nhận. (2) **Tiền nguyên đồng** (gồm cả giá thuốc): thuộc tính `[Money]` (`Infrastructure/MoneyModelBinder.cs`) = binder riêng bỏ dấu chấm, dấu phẩy, khoảng trắng, chữ đ (đặt trước InvariantDecimalModelBinder) + kiểm tra 0–1.000.000.000 đ, không số lẻ; trình duyệt dùng quy tắc `pmmoney` (validation.js). Ô số không phải tiền (hệ số quy đổi, liều dùng) vẫn theo DEC-24 (7). Điền sẵn giá trị bằng `Format.MoneyInput`. (3) Đã áp dụng: Ca làm việc (tiền đầu ca, thực đếm), Thêm/sửa thuốc (giá nhập, giá bán, giá đơn vị quy đổi). **Module sau (nhập kho, bán hàng, thanh toán, hoàn tiền) dùng cùng cách.** (4) Hiển thị tiền giữ dạng `Format.Money` = "200.000 đ" (có dấu cách) | 03/11/2026 |
| DEC-27 | Ca làm việc 1.4.1–1.4.3, nhóm "ok" 03/11 — Area **Sales**, `/Sales/Shifts` (Ca của tôi + Chi tiết). (1) Mỗi người chỉ thao tác ca của chính mình (Employees.UserId); quyền F1.4.1 mở, F1.4.2 bàn giao, F1.4.3 chốt (Pharmacist, Manager, Owner theo seed), kiểm tra ở server; tài khoản chưa gắn hồ sơ nhân viên đang làm việc không mở được ca. (2) Mã ca `CA` + yyMMdd + `S` (mở trước 12:00) / `C`; hai quầy cùng buổi thêm `-2`, `-3`. Tiền đầu ca nguyên đồng, 0–1 tỷ; gợi ý = thực đếm lần chốt gần nhất. (3) Thứ tự bắt buộc Mở → Bàn giao → Chốt; **bàn giao bị chặn khi ca còn hóa đơn chờ thanh toán**; sau bàn giao không lập hóa đơn mới; ghi chú bàn giao bắt buộc (điền sẵn tóm tắt). (4) Chốt: TotalCashSales = Payments tiền mặt Thành công của hóa đơn thuộc ca (trừ hóa đơn đã hủy); TotalNonCash = chuyển khoản + thẻ; TotalCashRefunds = Returns tiền mặt gắn ca (trừ phiếu hủy); ExpectedCash = đầu ca + bán − hoàn; chênh lệch ≠ 0 bắt buộc giải trình; ClosedByEmployeeId = người đứng ca; ghi ShiftClosings + Shifts trong 1 transaction. AuditLogs SHIFT_OPEN / SHIFT_HANDOVER / SHIFT_CLOSE. (5) `IShiftService.GetOpenShiftIdAsync(userId)`: ca **Đang mở** (chưa bàn giao) — Bán hàng (3.1.4/3.2.4) và hoàn tiền mặt (3.4.3, DEC-11) phải dùng, null thì từ chối. (6) Sửa lỗi scaffold: EF hiểu nhầm Nhân viên–Ca là 1–1 do chỉ mục lọc UX_Shifts_OpenPerEmployee → `Data/PharmacyDbContext.Custom.cs` bỏ `Employee.Shift`, khai báo lại 1–nhiều (không đổi CSDL); **không dùng `Employee.Shift`**, truy vấn ca qua `DbSet Shifts`. Cảnh báo EF 10627 "Employee.Shift was first mapped explicitly and then ignored" do việc này là chủ đích → tắt riêng `CoreEventId.MappedNavigationIgnoredWarning` ở `AddDbContext` (Program.cs, nhóm "ok" 04/11). Để sau: Owner/Manager xem ca toàn nhà thuốc (Báo cáo), chốt hộ ca bị bỏ quên, in biên bản, đếm theo mệnh giá | 03/11/2026 |
| DEC-26 | Tìm kiếm trên các trang danh sách (Thuốc, Nhà cung cấp, Khách hàng), nhóm "ok + không dấu" 03/11. (1) **Gõ đến đâu lọc đến đó**: `wwwroot/js/live-search.js` dùng chung cho form `data-pm-live-search`; ngừng gõ 300 ms hoặc đổi ô chọn → gọi lại chính trang danh sách (GET, server lọc + kiểm tra quyền như cũ), thay nội dung các vùng `data-pm-live` (count, clear, results), cập nhật địa chỉ bằng `history.replaceState`; hủy yêu cầu cũ khi gõ tiếp; trả về không đúng dạng (hết phiên) hoặc lỗi → chuyển trang bình thường. Nút Lọc ẩn khi có JS (không JS vẫn dùng được). (2) **Không phân biệt dấu**: `Helpers/SearchText.cs` — cột tiếng Việt (tên thuốc, hoạt chất, tên NCC, người liên hệ, họ tên khách) so bằng `EF.Functions.Collate(REPLACE(cột, đ→d), "Latin1_General_100_CI_AI")` với từ khóa đã NFC + đổi đ→d; mã, mã vạch, MST, SĐT so bình thường. Không đổi collation của CSDL. (3) Khi đang tìm: tên **bắt đầu bằng** từ khóa xếp trước, sau đó theo tên. Mọi trang danh sách mới về sau dùng cùng cơ chế | 03/11/2026 |
| DEC-25 | Nhà cung cấp (1.2.1) + Khách hàng (1.2.2), nhóm "ok" 03/11 — Area **Catalog**, `/Catalog/Suppliers`, `/Catalog/Customers` (menu Khách hàng vẫn ở nhóm Bán hàng). (1) Mã tự sinh: `NCC` + số nối tiếp tối thiểu 2 chữ số (tiếp NCC10 → NCC11), `KH` + 4 số. (2) Kiểm tra chung (`ContactRules` trong SupplierViewModels.cs): SĐT lưu chỉ chữ số, 10–11 số, bắt đầu 0; email chữ không dấu (cột varchar), lưu chữ thường; MST 10 số hoặc 10 số-3 số. (3) NCC: chặn trùng tên (theo collation CSDL, không phân biệt hoa/thường) và trùng MST; chi tiết hiện số đơn đặt hàng (đang mở = Nháp/Chờ giao/Giao thiếu), số phiếu nhập đã nhập kho, ngày nhập gần nhất; công nợ để 2.2.5. (4) KH: **chặn trùng SĐT giữa các khách đang hoạt động** (khách đã ngừng không xét; mở lại bị chặn nếu số đã có khách khác dùng); ngày sinh không sau hôm nay, không quá 120 năm; bệnh nền tách theo `;`/xuống dòng, lưu nối `; ` (≤ 500) để so từ khóa DEC-08; dị ứng **chỉ chọn hoạt chất có sẵn** (thêm hoạt chất thuộc 1.1.x), tối đa 30 dòng, sửa giữ RecordedAt dòng cũ; chi tiết hiện lịch sử mua 3.5.1 = 20 hóa đơn Đã thanh toán gần nhất (người bán = Employees.FullName) + tổng hợp (chưa trừ tiền hoàn). (5) Không xóa — Ngừng/Mở lại (IsActive); Đặt hàng/Nhập kho chỉ chọn NCC IsActive = 1, Bán hàng chỉ chọn khách IsActive = 1. AuditLogs SUPPLIER_* / CUSTOMER_* (CREATE, UPDATE, ACTIVATE, DEACTIVATE). (6) Quyền ở server: toàn bộ SuppliersController cần F1.2.1 (Manager, Owner), CustomersController cần F1.2.2 (Manager, Pharmacist, Owner). (7) `catalog.js` dùng chung phần thêm/bỏ dòng cho form có `data-pm-repeater-form`. Phân hệ Bán hàng (TV4) dùng lại `ICustomerService` khi thêm khách ở quầy | 03/11/2026 |
| DEC-24 | Danh mục thuốc (1.1.1–1.1.4), nhóm "ok" 03/11 — Area **Catalog**, `/Catalog/Medicines`. (1) Q-23: nhóm thuốc, đơn vị chỉ chọn từ dữ liệu có sẵn; hoạt chất và hãng sản xuất thêm mới ngay trong form (trùng tên thì dùng lại). (2) Mã thuốc tự sinh `MT` + 4 số nối tiếp. (3) Giá bán đơn vị cơ sở bắt buộc khi thêm, ghi MedicinePriceHistories "Khai báo giá ban đầu"; sau đó **khóa** (đổi ở 1.5.4); giá nhập tham khảo sửa được; đơn vị quy đổi mới thêm sau được khai báo giá ban đầu. (4) Thuốc đã có lô/giao dịch/chứng từ (MedicineBatches, InventoryTransactions, PO/GR/Sales/Prescription details): khóa đơn vị cơ sở, không sửa/xóa dòng quy đổi cũ. (5) Không xóa thuốc — Ngừng/Mở lại kinh doanh (IsActive), ghi AuditLogs MEDICINE_*; các phân hệ sau (bán, đặt hàng) phải bỏ qua thuốc IsActive = 0. (6) Quyền: danh sách/chi tiết cần 1.1.1 hoặc 1.1.2; thêm 1.1.1 (+1.1.3); sửa 1.1.2; đổi nhóm 1.1.3; quy đổi 1.1.4 — kiểm tra ở server. (7) Số thập phân từ ô `type=number` đọc theo InvariantCulture (binder riêng) vì văn hóa vi-VN coi dấu chấm là phân cách nghìn; thông báo lỗi ràng buộc dữ liệu bằng tiếng Việt | 03/11/2026 |
| DEC-23 | 03/11: tên hiển thị vai trò Manager trên giao diện là **"Quản lý"** (không phải "Quản lý kho") — vai trò này phụ trách danh mục, kho, đổi trả, báo cáo và bán hàng như Pharmacist (mục 2), rộng hơn kho. Tên kỹ thuật vẫn là `Manager`; chức danh trong hồ sơ nhân viên (JobTitle "Quản lý kiêm thủ kho (Manager)") là dữ liệu, giữ nguyên | 03/11/2026 |
| DEC-22 | 03/11: đổi email Owner bằng lệnh `owner-recovery` (đúng quy trình DEC-18) từ quynhpham.eculis@gmail.com sang **phuongmaianh122006@gmail.com** → tên tài khoản `phuongmaianh122006`; hồ sơ NV01 giữ nguyên. Gmail gửi thư (Email:SenderAddress) cũng là phuongmaianh122006@gmail.com (App Password trong User Secrets); Email:PublicBaseUrl = https://localhost:7047. quynhpham.eculis@gmail.com không tạo được App Password nên không dùng làm Gmail gửi | 03/11/2026 |
| DEC-21 | Nhóm yêu cầu 03/11 — **ba thông tin độc lập**: (1) `AspNetUsers.UserName` = phần trước @ của email (quynhpham.eculis), dùng để đăng nhập (đăng nhập bằng email hoặc tên tài khoản) và hiện trên giao diện; trùng tên với tài khoản khác thì từ chối email đó; tự đổi khi email đổi; tài khoản cũ có UserName = cả email được `UserNameSyncService` đổi lúc khởi động (ghi AuditLogs USERNAME_SYNC). (2) `Email` đầy đủ: đăng nhập, nhận mã, tooltip ở góc trên. (3) `Employees.FullName`: họ tên thật cho hóa đơn, báo cáo, chứng từ kho, nhật ký, sổ GPP — không thay bằng UserName/Email/tên vai trò. Ảnh đại diện = chữ đầu 2 phần đầu của UserName tách theo . _ - (một phần: 2 ký tự đầu). Góc trên + lời chào Tổng quan dùng UserName. Owner khai báo họ tên ở bước đăng nhập lần đầu và sửa ở Tài khoản của tôi → Chỉnh sửa hồ sơ (chỉ Owner, server kiểm tra; không sửa UserName/Email); họ tên bắt buộc, gộp khoảng trắng, ≤150, không trùng UserName/Email/tên vai trò; Owner chưa có hồ sơ thì gắn hồ sơ chưa có tài khoản cùng email, không có mới tạo (không trùng). Khởi tạo Owner: bắt buộc Bootstrap:OwnerFullName nếu không dùng OwnerEmployeeCode; bỏ mặc định "Chủ nhà thuốc" | 03/11/2026 |
| DEC-20 | Nhóm yêu cầu 03/11: (1) tên nhà thuốc **"Nhà thuốc Tận Tâm"**, đặt ở `appsettings.json` → `Pharmacy:Name` (logo, tiêu đề tab, tên người gửi và nội dung email; đổi tên chỉ sửa một chỗ); (2) góc trên, lời chào và trang Tài khoản của tôi hiện **tên tài khoản = phần trước @ của Gmail đăng nhập**; họ tên trong hồ sơ nhân viên vẫn dùng cho hóa đơn, báo cáo, sổ GPP; (3) form báo lỗi **ngay khi gõ** (jQuery Validation, `wwwroot/js/validation.js`); quy tắc mật khẩu dùng chung một nơi `Models/Validation/PasswordPolicyAttribute.cs` cho Identity, server và trình duyệt (chữ thường/hoa tính theo a–z, A–Z như Identity) | 03/11/2026 |
| DEC-19 | Nhóm "ok" bắt đầu code (02/11): Q-20 Area theo phân hệ (Admin, Sales, Inventory, Catalog, Reports); Q-21 tự host font Be Vietnam Pro (OFL) + Bootstrap Icons 1.11.3 (MIT) bằng `tools/03-fetch-assets.ps1`, biểu đồ để sau; Q-25 script `PharmacyManagementDB_v2.1.sql` (2 chỉ mục lọc, chạy lại không lỗi, tự dừng nếu dữ liệu đang vi phạm); Q-19 hướng giao diện "Soft Clinical" (bản thoáng) | 03/11/2026 |
| DEC-18 | Tài khoản (nhóm chốt 02/11): (1) Gửi email thật qua Gmail SMTP bằng System.Net.Mail + App Password; cấu hình trong User Secrets/biến môi trường, không vào source/Git; gửi lỗi phải báo rõ và ghi IntegrationLogs. (2) Owner đầu tiên: quynhpham.eculis@gmail.com, mật khẩu tạm "123" CHỈ ở môi trường Development, lấy từ User Secrets (không hardcode SQL/code), Identity băm, bắt đổi ở lần đăng nhập đầu, phải xác minh email trước khi dùng quản trị quan trọng — ngoại lệ có chủ đích cho quy tắc "không seed mật khẩu mặc định". (3) Chỉ một Owner; không mời/tạo/nâng cấp Owner qua giao diện; chỉ Owner mời và quản lý Manager/Pharmacist; không khóa/xóa Owner; kiểm tra ở server. (4) Khởi tạo Owner chỉ một lần (mốc OwnerSetupCompletedAt); khôi phục Owner bằng quên mật khẩu hoặc lệnh owner-recovery trên máy chủ, không tạo Owner thứ hai. (5) Mời nhân viên: mã 6 số, băm trong AspNetUserTokens, 15 phút, sai 5 lần hủy; nhân viên tự đặt mật khẩu (lệch đầu vào 1.3.1 Bảng 2.3 — ghi ở tài liệu thiết kế hệ thống, không sửa Chương 2); mỗi tài khoản 1 email, chỉ Owner đổi email | 02/11/2026 |

## 7. Vấn đề mở

### 7.1 Chờ nhóm
| Mã | Việc | Ghi chú |
|---|---|---|
| T-03 | Còn thiếu: phiên bản Visual Studio. Đã có: SDK 10.0.301/302/401, dotnet-ef 10.0.14; chưa có repo GitHub (nhóm tự tạo sau) | |
| T-04 | ✅ 01, v2.1, 02 xong (03/11): 51 entity, build 0 lỗi. Lần đầu 02 bị Timeout (-2) khi đọc metadata, chạy lại thì được. Còn lại theo README: (1) `tools\01-create-solution.ps1`; (2) chạy `PharmacyManagementDB_v2.1.sql` trong SSMS; (3) `tools\02-scaffold.ps1` (scaffold + build); (4) `tools\03-fetch-assets.ps1`; (5) đặt User Secrets Email:* và Bootstrap:*; chạy thử. Gửi Claude số entity (kỳ vọng 51) và TOÀN BỘ lỗi/cảnh báo build | Code chưa từng được biên dịch |
| T-05 | ✅ Đã biên dịch được (03/11) — các điểm sau không gây lỗi build, còn cần xác nhận lúc chạy: `options.Stores.SchemaVersion = IdentitySchemaVersions.Version1`; tên entity/DbSet scaffold (Employee, PharmacySetting, IntegrationLog, AuditLog, Permission, RolePermission); kiểu `IsActive` (bool); `dotnet ef --output-dir` đường dẫn tuyệt đối; `UseStaticFiles` + `MapStaticAssets` | |
| Q-27 | Cảnh báo NU1902/NU1903: gói gián tiếp System.Security.Cryptography.Pkcs 9.0.11 có 2 lỗ hổng DoS (CVE-2026-84008 cao, CVE-2026-70162 trung bình; vá ở 9.0.21 / 10.0.13). Đề xuất thêm tham chiếu trực tiếp System.Security.Cryptography.Pkcs 10.0.14 vào PharmacyManagement.csproj — chờ "ok" | Ứng dụng không gọi SignedCms/EnvelopedCms nên rủi ro thực tế thấp; 3 cảnh báo "Skipping foreign key" là bình thường (bảng AspNet* do Identity ánh xạ) |
| Q-26 | Biểu đồ báo cáo: SVG/CSS tự vẽ hay thêm Chart.js (phần còn lại của Q-21) | Khi làm module Báo cáo |
| Q-22 | Ngưỡng nhiệt độ/độ ẩm (đề xuất ≤ 30°C, ≤ 75% theo GPP) lưu ở PharmacySettings hay cố định | Hiện DB chỉ có ExpiryWarningDays |
| Q-17 | Xác nhận giá trị seed giả định: cận hạn 90 ngày, giảm giá tối đa 10%, đổi trả 7 ngày, điều kiện đổi trả, danh sách đơn vị | Có thể chỉnh sau trong ứng dụng |
| Q-18 | Đề xuất chưa đưa vào script: `MedicineBatches.UnitCost` decimal(18,2) → (18,4); `ReturnDetails.RestockedByEmployeeId/RestockedAt`; `Payments.ShiftId` | Xem CHANGELOG_DB.md mục 12 |

### 7.2 Phạm vi DB v2 đã áp dụng
G-01…G-15, DB-01, DB-03, DB-04, DB-06, DB-07, DB-10, DB-12, DB-13, DB-14, P3 (Payments.TenderedAmount, CustomerReminders.DaysSupply, Permissions.IsSensitive), Q-13…Q-16 (+ ShiftClosings.OpeningCash/TotalCashSales/TotalCashRefunds, Returns.RefundReference, MedicinePriceHistories.OldSellingPrice/ChangeReason). **Không áp dụng DB-11.** Chi tiết từng cột: CHANGELOG_DB.md mục 4.

### 7.3 Quy tắc tầng ứng dụng bắt buộc (DB không tự đảm bảo)
Xem CHANGELOG_DB.md mục 8: trừ tồn bằng UPDATE có điều kiện trong transaction; khóa giá sau khai báo (Q-14); khóa quyền kho Pharmacist (Q-15); hoàn tiền mặt theo ca của người hoàn (Q-16); chuẩn hóa từ khóa chống chỉ định (Q-13); chặn thuốc kê đơn ở OTC; FEFO khi đủ tiền; 2.4.1.1 chỉ đọc; khóa lô khi tiếp nhận thu hồi; chỉ hủy hóa đơn chờ thanh toán; lô xếp vào Kệ.

## 8. Kiến trúc & quy ước code (đề xuất)
- Database-first: `Database/` gồm script gốc + script thay đổi đánh số; đổi schema → chạy script → `dotnet ef dbcontext scaffold ... --force`. Không sửa tay file sinh; tùy biến bằng partial class.
- Identity + scaffold: EF Core T4 templates để DbContext kế thừa `IdentityDbContext`, hoặc scaffold riêng bảng nghiệp vụ; kiểm tra Identity schema version của .NET 10 khớp bảng AspNet* (quyết định khi dựng khung).
- Phân quyền: Policy theo Permission (mã chức năng BFD), đọc từ RolePermissions; ràng buộc cứng SYS-13 không phụ thuộc cấu hình.
- Thư mục: Controllers / Areas(Admin, Sales, Inventory, Catalog, Reports — theo phân hệ, DEC-19) / Models(Entities — sinh tự động, ViewModels, Enums) / Data / Services / Authorization / Configuration / Identity / ViewComponents / BackgroundJobs / Helpers / Middleware.
- Identity: `IdentityUser`/`IdentityRole`, `SchemaVersion = Version1` (DB không có bảng passkey); mật khẩu ≥ 8 ký tự có số, chữ thường, chữ hoa; khóa 15 phút sau 5 lần sai; SecurityStamp kiểm tra mỗi 1 phút (khóa tài khoản/đổi mật khẩu có hiệu lực ≤ 1 phút); claim riêng `pm:email_verified`, `pm:must_change_password`, `pm:employee_id`, `pm:full_name`.
- Phân quyền đã code: `PermissionPolicyProvider` (tên policy = mã quyền) + `PermissionHandler`: khóa cứng SYS-13 và DEC-18 (1.3.1–1.3.5 chỉ Owner), tài khoản đang bị bắt đổi mật khẩu không có quyền, mã quan trọng cần email đã xác minh, còn lại đọc RolePermissions.CanView (cache 5 phút). FallbackPolicy bắt đăng nhập mọi trang.
- Mã 6 số: băm bằng IPasswordHasher trong AspNetUserTokens (LoginProvider `PharmacyVerify`), 15 phút, sai 5 lần hủy, gửi lại sau 60 giây; mục đích invite / verify-email / reset / change-email. Email gửi bằng System.Net.Mail, mọi lần gửi ghi IntegrationLogs (EMAIL, không ghi mã). Thao tác tài khoản ghi AuditLogs.
- Giao diện: `wwwroot/css/tokens.css` (token Soft Clinical) + `site.css` (thành phần) + `site.js` (không chứa nghiệp vụ); `_Layout` (sidebar theo quyền qua `SidebarViewComponent`, mục chưa làm trỏ trang "Đang xây dựng"), `_AuthLayout` cho đăng nhập/kích hoạt. Không màu cứng, không `style=` trong view, không dữ liệu giả.
- Enum C# `: byte` cho cột tinyint; DateOnly cho `date`. Service `I{Tên}Service`; ViewModel `{Màn hình}ViewModel`; hàm async có hậu tố `Async`.
- Git: `main` ← `develop` ← `feature/<module>-<tên>`; PR cần 1 người review.
- SYS-02: in K58/K80 bằng CSS @media print. SYS-05: sao lưu bằng Task Scheduler + sqlcmd.

## 9. Phân công (theo master prompt)
| TV | Phụ trách |
|---|---|
| 1 | Auth, phân quyền theo permission, nhân viên, role, Dashboard Owner, tích hợp |
| 2 | Danh mục thuốc, hoạt chất, đơn vị, quy đổi, tương tác/chống chỉ định, NCC, khách hàng |
| 3 | Đặt hàng, nhập kho, xuất kho (trả NCC, hủy, thu hồi), lô, kiểm kê, tồn kho |
| 4 | POS, bán OTC, bán theo đơn, hóa đơn, thanh toán + FEFO, đổi trả |
| 5 | Báo cáo, sổ GPP, ca làm việc, bảo quản, cảnh báo, nhắc lịch, liên thông, kiểm thử tích hợp |

## 10. Tiến độ
| Giai đoạn | Trạng thái |
|---|---|
| 0. Rà soát tài liệu, đối chiếu Chương 2 bản chốt | ✅ |
| 1. DB v2 + enum + seed + Data Dictionary v2 | ✅ Hoàn tất 31/10/2026 — chạy thành công trên SQL Server 2025 Express, số liệu khớp |
| 1b. Dữ liệu mẫu | ✅ Hoàn tất 02/11/2026 — nhóm chạy thành công |
| 2. Khung solution .NET 10, scaffold, Identity, phân quyền, layout | 🟡 Đã code: script 01/02/03, DB v2.1, Identity + phân quyền, module tài khoản (mời bằng email + mã, kích hoạt, quên mật khẩu, Owner khởi tạo một lần, xác minh email, đổi email, khóa/mở khóa, khôi phục Owner bằng lệnh), layout + sidebar theo quyền, trang tổng quan (chưa có số liệu). Chờ nhóm build (T-04, T-05) |
| 3+. Các module nghiệp vụ | 🟡 Danh mục thuốc 1.1.1–1.1.4 (DEC-24) ✅ nhóm đã thử 03/11. Nhà cung cấp 1.2.1 + Khách hàng 1.2.2 (DEC-25), tìm kiếm trực tiếp không dấu (DEC-26), Ca làm việc 1.4.1–1.4.3 (DEC-27), ô nhập tiền (DEC-28) đã code 03/11; dòng đếm (DEC-29), Nhập kho 2.2.1.1–2.2.4 (DEC-30), Ca toàn nhà thuốc (DEC-31) đã code 04/11 — chờ nhóm build và thử. Còn lại ⏳ |

## 11. Nhật ký thay đổi file
- v0.1: Tạo file sau khi đọc master prompt, SQL, Data Dictionary, Chương 2.
- v0.2: Chốt .NET 10 + Database-first; đối chiếu với Chương 2 (bản nhầm).
- v1.4.3 (04/11/2026): Sửa lỗi phiếu theo đơn báo "Chọn nhà cung cấp"; ô chọn thuốc gõ chữ (mới: wwwroot/js/combobox.js; sửa site.css, _ReceiptForm.cshtml, Receipts/Create.cshtml, Receipts/Edit.cshtml, GoodsReceiptViewModels.cs — MedicineOption.Keywords, GoodsReceiptService.cs).
- v1.4.2 (04/11/2026): Sửa lỗi build `_ReceiptForm.cshtml` (3 thẻ `<option>` trong hàm Razor không async → `<!option>`); nút Ca toàn nhà thuốc màu `btn-primary`; Program.cs tắt riêng cảnh báo EF 10627 (bổ sung DEC-27).
- v1.4.1 (04/11/2026): DEC-31 Ca toàn nhà thuốc (mới: Areas/Sales/Views/Shifts/All.cshtml; sửa ShiftsController.cs, IShiftService.cs, ShiftService.cs, ShiftViewModels.cs, Shifts/Index.cshtml, Shifts/Details.cshtml).
- v1.4 (04/11/2026): DEC-29 dòng đếm Tìm thấy X / Y (sửa 3 list view model, 3 service, 3 Index view). DEC-30 Nhập kho (mới: Areas/Inventory — ReceiptsController, _ViewImports, _ViewStart, Receipts/Index, Create, Edit, Details, _ReceiptForm; IGoodsReceiptService/GoodsReceiptService; Models/ViewModels/Inventory/GoodsReceiptViewModels.cs; wwwroot/js/receipt.js; sửa NavigationMenu.cs, Program.cs).
- v1.3.1 (03/11/2026): DEC-28 ô nhập tiền (mới: Infrastructure/MoneyModelBinder.cs, wwwroot/js/money-input.js; sửa Program.cs, Format.cs, _Scripts.cshtml, validation.js, catalog.js, MedicineViewModels.cs, ShiftViewModels.cs, Shifts/Index.cshtml, _MedicineForm.cshtml).
- v1.3 (03/11/2026): DEC-27 Ca làm việc (mới: Areas/Sales — ShiftsController, _ViewImports, _ViewStart, Shifts/Index, Shifts/Details; IShiftService/ShiftService; ShiftViewModels; Data/PharmacyDbContext.Custom.cs; sửa NavigationMenu.cs, Program.cs). Ghi nhận repo GitHub.
- v1.2.1 (03/11/2026): DEC-26 tìm kiếm trực tiếp + không dấu (mới: SearchText.cs, live-search.js; sửa 3 Index view, 3 service, site.css). Thêm `.gitattributes`, bổ sung `.gitignore` (.claude/, ~$*, ux_*.html, file bí mật) để lên GitHub.
- v1.2 (03/11/2026): Nhóm xác nhận Danh mục thuốc đã thử xong. Code Nhà cung cấp + Khách hàng (DEC-25): 18 file mới, sửa NavigationMenu.cs, Program.cs, catalog.js. Chờ nhóm build và thử.
- v1.1 (03/11/2026): Code Danh mục thuốc (DEC-24), đóng Q-23. Chờ nhóm build và thử.
- v1.0.5 (03/11/2026): DEC-23 (vai trò Manager hiển thị "Quản lý").
- v1.0.4 (03/11/2026): DEC-22 (email Owner mới phuongmaianh122006@gmail.com, cấu hình Gmail gửi thư).
- v1.0.3 (03/11/2026): DEC-21 (UserName/Email/họ tên tách bạch, avatar, Owner sửa họ tên, bỏ mặc định "Chủ nhà thuốc").
- v1.0.2 (03/11/2026): DEC-20 (tên Nhà thuốc Tận Tâm, tên tài khoản theo Gmail, báo lỗi form khi gõ; che mã 6 số trong tiêu đề email trước khi ghi IntegrationLogs).
- v1.0.1 (03/11/2026): Nhóm build thành công (51 entity, 0 lỗi); T-04/T-05 cập nhật; thêm Q-27 (gói Pkcs).
- v1.0 (03/11/2026): Code module tài khoản + giao diện nền; DEC-19 (Q-19, Q-20, Q-21, Q-25 đã giải quyết); thêm T-05, Q-26; cập nhật mục 8, 10.
- v0.9 (02/11/2026): Nhóm chốt quy tắc tài khoản → DEC-18 (thay Q-24); mở Q-25 (2 chỉ mục DB v2.1). Thiết kế: canvas mục 5–6.
- v0.8.1 (02/11/2026): Thêm Q-24 (tạo tài khoản bằng email + mã xác minh).
- v0.8 (02/11/2026): Ghi bước 2.1 vào thư mục dự án (DEC-15); DEC-16, DEC-17; bản đề xuất giao diện v0.2 "Soft Clinical" (làm lại theo góp ý "quá dày"); mở T-04, Q-19…Q-23.
- v0.7 (02/11/2026): Nhóm chạy dữ liệu mẫu thành công; Giai đoạn 1 hoàn tất; mở T-03.
- v0.6 (31/10/2026): Thêm dữ liệu mẫu (PharmacyManagementDB_SampleData.sql, SAMPLE_DATA.md), DEC-14, quy ước tính tiền hóa đơn.
- v0.5 (31/10/2026): Nhóm chạy DB v2 thành công trên SQL Server 2025 Express; ghi DEC-13; Giai đoạn 1 hoàn tất.
- v0.4 (31/10/2026): Bàn giao DB v2 (58 bảng, 499 cột) + CHANGELOG_DB.md + ENUMS.md + Data Dictionary v2; sao lưu v1; đính chính v1 = 415 cột.
- v0.3.1: Ghi DEC-08…DEC-12 sau khi nhóm xác nhận Q-13…Q-16; loại yêu cầu sửa Chương 2.
- v0.3: Đối chiếu lại với **Chương 2 bản chốt cuối** (3 role cố định, Manager bán hàng, Owner duyệt kiểm kê và đặt chính sách giá, Manager xử lý đổi trả, SYS-14). Bỏ Q-10…Q-12 (Chương 2 đã trả lời); thêm Q-14…Q-16, G-14, G-15.
