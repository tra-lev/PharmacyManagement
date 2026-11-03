# SAMPLE_DATA — Dữ liệu mẫu PharmacyManagementDB (v2)

> File: `PharmacyManagementDB_SampleData.sql` — sinh ngày 31/10/2026 bằng mô phỏng nghiệp vụ theo dòng thời gian **01/05/2026 → 31/10/2026 21:15** (seed cố định, chạy lại cho kết quả giống hệt).
> Chỉ dùng cho phát triển, kiểm thử, demo. **Không** dùng cho môi trường thật.

## 1. Cách chạy

1. Tạo database trống bằng `PharmacyManagementDB.sql` (v2).
2. Mở `PharmacyManagementDB_SampleData.sql` trong SSMS → Execute (F5). Thời gian chạy khoảng vài giây.
3. Kết quả cuối: bảng nhỏ đếm số dòng — kỳ vọng SalesInvoices 644, InventoryTransactions 1319, MedicineBatches 206, Medicines 101.

- Script **tự dừng** nếu database đã có dữ liệu thuốc, hoặc seed của script v2 (đơn vị tính, kho KHO01) không đúng.
- Toàn bộ chạy trong **một giao dịch**: lỗi ở bất kỳ đâu → rollback hết, database giữ nguyên trạng thái trước khi chạy.
- Muốn làm lại từ đầu: xóa database → chạy lại script v2 → chạy lại file dữ liệu mẫu.

## 2. Nguyên tắc dữ liệu

- **Nhất quán như dữ liệu thật**: mọi phiếu được sinh bằng cách đi qua đúng quy trình Chương 2 theo thứ tự thời gian — đặt hàng → nhập kho (tạo lô) → bán (FEFO khi thanh toán đủ) → đổi trả → kiểm kê → hủy → thu hồi → trả NCC. Tồn từng lô luôn bằng tổng sổ biến động kho.
- **Hư cấu**: số đăng ký `VD-MAU-…/VN-MAU-…`, mã vạch nội bộ `200…`, tên nhà phân phối, bác sĩ, phòng khám, khách hàng, nhân viên, số điện thoại `0900…`, email `@example.com`, công văn thu hồi `CV-MAU-…`. Tên thuốc và nhà sản xuất là tên phổ biến trên thị trường.
- **Thông tin chuyên môn chỉ để demo cảnh báo** (liều tối đa, tương tác, chống chỉ định, dị ứng) — không dùng làm tài liệu tham khảo lâm sàng.
- **Không có tài khoản đăng nhập** (SYS-08) và **không có AuditLogs** (nhật ký do ứng dụng ghi khi có người dùng thật).

## 3. Số dòng theo bảng

| Bảng | Số dòng | Bảng | Số dòng |
|---|---|---|---|
| Categories | 13 | Prescriptions | 195 |
| Manufacturers | 22 | PrescriptionDetails | 291 |
| ActiveIngredients | 101 | SalesInvoices | 644 |
| Medicines | 101 | SalesInvoiceDetails | 1065 |
| MedicineIngredients | 117 | SalesIssueAllocations | 1083 |
| MedicineUnitConversions | 152 | Payments | 673 |
| IngredientInteractions | 14 | Returns | 18 |
| MedicineContraindications | 40 | ReturnDetails | 18 |
| Employees | 6 | Stocktakes | 7 |
| Promotions | 5 | StocktakeDetails | 180 |
| MedicinePriceHistories | 282 | StockDisposals | 3 |
| InventoryPolicies | 101 | StockDisposalDetails | 5 |
| Suppliers | 10 | ProductRecalls | 2 |
| Customers | 80 | RecallBatches | 2 |
| CustomerAllergies | 8 | SupplierReturns | 2 |
| StorageLocations | 18 | SupplierReturnDetails | 2 |
| PurchaseOrders | 99 | SupplierPayments | 87 |
| PurchaseOrderDetails | 212 | InventoryTransactions | 1319 |
| GoodsReceipts | 97 | ShiftClosings | 367 |
| GoodsReceiptDetails | 209 | StorageEnvironmentLogs | 368 |
| MedicineBatches | 206 | CustomerReminders | 112 |
| Shifts | 368 | IntegrationLogs | 351 |

Tổng **9.055** dòng dữ liệu mẫu (cộng với seed của script v2: 3 vai trò. 67 quyền. 141 phân quyền. 9 đơn vị. 1 kho. 1 cấu hình. 1 chính sách giá).

## 4. Bức tranh kinh doanh 6 tháng

| Tháng | Hóa đơn đã thanh toán | Doanh thu (đ) | Giá vốn FEFO (đ) | Tiền hoàn khách (đ) |
|---|---|---|---|---|
| 05/2026 | 108 | 5.601.000 | 3.665.536 | 96.100 |
| 06/2026 | 102 | 6.176.200 | 4.120.522 | 300 |
| 07/2026 | 106 | 6.677.100 | 4.383.398 | 14.600 |
| 08/2026 | 105 | 5.936.600 | 3.838.706 | 15.900 |
| 09/2026 | 105 | 5.312.400 | 3.445.103 | 10.433 |
| 10/2026 | 113 | 6.515.400 | 4.252.584 | 15.262 |
| **Tổng** | **639** | **36.218.700** | **23.705.848** | **152.595** |

- 101 thuốc (47 kê đơn, 2 kiểm soát đặc biệt — Diazepam, Phenobarbital, để ở Tủ 5), 101 hoạt chất, 13 nhóm, 22 nhà sản xuất, 10 nhà cung cấp, 80 khách hàng.
- 639 hóa đơn đã thanh toán: 191 bán theo đơn, 448 bán không kê đơn; 3 hóa đơn đã hủy; 2 hóa đơn đang chờ thanh toán.
- 673 lượt thanh toán (tiền mặt, chuyển khoản/QR, thẻ, kết hợp); 368 ca (2 ca/ngày) và 367 biên bản chốt tiền (33 ca có chênh lệch thừa/thiếu).
- 99 đơn đặt hàng (18 giao thiếu), 97 phiếu nhập, 206 lô thuốc, 87 phiếu chi/thu NCC.
- Giá thay đổi 2 đợt do Owner: 01/08/2026 tăng 7% cho 10 thuốc, 15/09/2026 giảm 10% cho 2 thuốc (29 dòng lịch sử giá có giá cũ + lý do). Hóa đơn trước ngày đổi giá giữ đơn giá cũ.

## 5. Nhân viên và tài khoản

| Mã | Họ tên | Vai trò dự kiến | Ghi chú ca |
|---|---|---|---|
| NV01 | Phạm Thị Quỳnh | Owner | Đứng ca chiều thứ Bảy đầu tháng (Owner được bán hàng) |
| NV02 | Nguyễn Văn Thành | Manager | Toàn bộ nghiệp vụ kho; đứng ca sáng Chủ nhật; xử lý mọi đổi trả |
| NV03 | Trần Thị Mai Anh | Pharmacist | Đang mở ca chiều 31/10 (`CA261031C`) |
| NV04 | Lê Hoàng Nam | Pharmacist | Xoay ca |
| NV05 | Võ Thị Thu Hà | Pharmacist | Xoay ca |
| NV06 | Đỗ Minh Tuấn | Pharmacist | Đã nghỉ việc 31/07/2026 (IsActive = 0), còn lịch sử ca tháng 5–7 |

Sau khi ứng dụng tạo tài khoản Identity (ví dụ `owner`, `manager`, `ds.maianh`…), gắn tài khoản vào nhân viên mẫu:

```sql
UPDATE dbo.Employees SET UserId = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName = N'OWNER')   WHERE EmployeeCode = 'NV01';
UPDATE dbo.Employees SET UserId = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName = N'MANAGER') WHERE EmployeeCode = 'NV02';
-- tương tự NV03–NV05 với vai trò Pharmacist
```

## 6. Tình huống demo có sẵn

| Chức năng | Dữ liệu sẵn có | Cách thử |
|---|---|---|
| 1.4 Ca làm việc | Ca `CA261031C` của NV03 **đang mở**; 2 hóa đơn **chờ thanh toán** `HD261031005`, `HD261031006` | Thanh toán 2 hóa đơn, bàn giao rồi chốt tiền ca |
| 1.4.3 / Q-16 Hoàn tiền mặt | Ca sáng Chủ nhật của Manager có hoàn tiền mặt: `TH260524001`, `TH260614001` | Mở biên bản chốt ca tương ứng: ExpectedCash = đầu ca + tiền mặt bán − tiền hoàn |
| 1.5.3 / 2.5.1 Cận hạn (90 ngày) | Colchicin 1mg lô CO260442 hạn 30/11/2026 còn 280; Glimepirid 2mg lô GL260424 hạn 31/10/2026 còn 77; Levonorgestrel 1,5mg lô LE260485 hạn 25/10/2026 còn 14; Magnesi B6 lô MA260479 hạn 31/12/2026 còn 232; Simethicon 80mg lô SI260471 hạn 30/11/2026 còn 484 | Dashboard cảnh báo cận hạn; lô hạn ≤ hôm nay phải bị chặn bán |
| 2.5.2 Dưới định mức | Montelukast 10mg: tồn 70 < định mức 90; Calci D: tồn 54 < định mức 60; Nước muối sinh lý 0,9% 500ml: tồn 2 < định mức 3 | Cảnh báo dưới định mức → lập đơn đặt hàng 2.1.1 |
| 2.1 Đặt hàng | Đơn **nháp** `DH261031001`; đơn **chờ giao** `DH261029002`; đơn **đã hủy** `DH260812001`; 18 đơn giao thiếu | Gửi đơn nháp; theo dõi trạng thái |
| 2.2 Nhập kho | Phiếu `PN261031002` **đang kiểm nhận** (đã có số lô, hạn dùng, vị trí; chưa sinh lô) | Lập phiếu nhập kho → hệ thống tạo lô, cộng tồn, ghi sổ kho |
| 2.2.5 Công nợ NCC | 10 phiếu nhập chưa thanh toán, tổng 2.202.200đ | Thanh toán NCC → sinh phiếu chi `PC…` |
| 2.3.1 / 2.3.5 Trả NCC | `TNCC260715001` đã quyết toán (có phiếu thu `PT…`); `TNCC261020001` **chưa nhận tiền hoàn** | Thu tiền hoàn cho phiếu thứ hai |
| 2.3.2 Hủy thuốc | `HUY260820001` (móp méo, hỏng), `HUY261005001` (hết hạn), `HUY261031001` **nháp** | Ghi sổ biên bản nháp |
| 2.3.3 Thu hồi | `THUHOI-2609-01` hoàn tất; `THUHOI-2610-02` **đang thu hồi** — Domperidon 10mg lô DO260486 bị khóa, còn 255 | Tra cứu lô đã bán cho ai (2.3.3.2) → lập phiếu thu hồi (2.3.3.3); thử bán thuốc này → không được xuất lô bị khóa |
| 2.4 Kiểm kê | `KK261030T01` **chờ Owner duyệt**; `KK260831T04` bị **từ chối** (có lý do) rồi kiểm lại `KK260901T04`; 5 phiếu đã điều chỉnh | Owner duyệt → Manager điều chỉnh theo Difference |
| 2.5.3 Nhiệt độ, độ ẩm | 368 lần đo (08:00, 15:00 mỗi ngày); 19 lần vượt ngưỡng có ghi chú | Sổ bảo quản |
| 3.1.3.1 Tương tác | 14 cặp hoạt chất mẫu, ví dụ Ibuprofen + Aspirin, Clarithromycin + Atorvastatin, Omeprazol + Clopidogrel | Cho Ibuprofen 400mg và Aspirin 81mg vào cùng giỏ → cảnh báo |
| 3.1.3.2 Trùng hoạt chất | Paracetamol có trong 4 thuốc (Paracetamol 500mg, Panadol Extra, Efferalgan, Alaxan) | Bán Panadol Extra + Efferalgan → cảnh báo trùng hoạt chất |
| 3.1.3.3 Dị ứng | KH0012 dị ứng Acid acetylsalicylic; KH0028 dị ứng Ibuprofen; KH0033 dị ứng Acid acetylsalicylic; KH0042 dị ứng Amoxicillin; KH0044 dị ứng Amoxicillin; KH0062 dị ứng Amoxicillin; KH0071 dị ứng Amoxicillin; KH0080 dị ứng Ibuprofen | Chọn khách KH… rồi thêm thuốc chứa hoạt chất đó → cảnh báo |
| 3.2.2.3 Chống chỉ định (Q-13) | Khách có bệnh nền: KH0027 Viêm loét dạ dày; KH0030 Viêm loét dạ dày; KH0036 Hen phế quản; KH0040 Suy thận mạn; KH0073 Hen phế quản | Bán Ibuprofen cho khách “Viêm loét dạ dày” → cảnh báo từ khóa “loét dạ dày” |
| 3.2.2.2 Liều dùng | Liều tối đa mẫu trên 40+ thuốc (vd Paracetamol 500mg: 2 viên/lần, 8 viên/ngày) | Kê 3 viên/lần → cảnh báo |
| 3.4 Đổi trả | Phiếu `TH261031001` **đã hoàn tiền, chờ nhập kho**; 11 dòng đã nhập lại, 6 dòng không nhập | Manager nhập số lô và nhập lại (3.4.4) |
| 3.5.2 Nhắc lịch | 18 khách mạn tính mua mỗi ~30 ngày; 17 lịch nhắc **chờ gửi** (tháng 11), 2 lần gửi lỗi | Hệ thống tự gửi; xem lịch sử mua (3.5.1) |
| 3.3.3 HĐĐT | 28 hóa đơn đã xuất (`MAU26-…`); `HD261002002` **xuất lỗi** | Xuất lại |
| 4.4 Liên thông | 227 lần gửi DQG mô phỏng; 1 lỗi chưa xử lý | Manager xử lý lỗi gửi (4.4.2.3) |
| 4.1 Báo cáo kinh doanh | Doanh thu, giá vốn theo lô (FEFO) đủ 6 tháng — bảng mục 4 | Lọc theo ca, nhân viên, nhóm thuốc; Pharmacist xem doanh số cá nhân |
| 4.3.2 Sổ thuốc kiểm soát | Diazepam, Phenobarbital: phiếu nhập + 4 lần bán theo đơn | Kết xuất sổ |
| 1.5.4 Khuyến mãi | `KM2610ORS` đang chạy (−200đ/gói Oresol); `KM2611GD` bắt đầu 01/11; `KM2606VTM`, `KM2609HH` đã kết thúc; `KM2605PAN` bị tắt | Bán Oresol → tự giảm |

## 7. Kiểm tra tính nhất quán đã chạy

Nạp script v2 + dữ liệu mẫu vào bản mô phỏng ràng buộc (SQLite: FK, CHECK, UNIQUE, filtered index, cột tính toán) → **28/28 kiểm tra đạt**:

- ✅ Không vi phạm khóa ngoại (PRAGMA foreign_key_check)
- ✅ Tồn từng lô = tổng biến động InventoryTransactions
- ✅ Tồn lô không bao giờ âm theo dòng thời gian
- ✅ Hóa đơn: SubTotal, chiết khấu, TotalAmount, LineTotal khớp chi tiết
- ✅ Hóa đơn đã thanh toán: tổng Payments = TotalAmount; hóa đơn chờ/hủy không có thanh toán
- ✅ Phân bổ lô (FEFO) = Quantity × ConversionToBase của từng dòng đã thanh toán
- ✅ Không xuất bán lô hết hạn / chưa nhập / sai thuốc; giá vốn = giá vốn lô
- ✅ Mỗi dòng sổ kho trỏ đúng chứng từ, đúng lô, đúng số lượng
- ✅ Kiểm kê: chỉ phiếu đã điều chỉnh (Owner duyệt) mới có bút toán, đủ mọi dòng chênh lệch
- ✅ Chốt ca: tiền đầu ca, tiền mặt bán, tiền hoàn mặt, phi tiền mặt khớp giao dịch của ca
- ✅ Hóa đơn lập trong ca đang mở, đúng người đứng ca
- ✅ Q-16: hoàn tiền mặt gắn ca đang mở của chính Manager hoàn tiền
- ✅ Đổi trả: số lượng ≤ đã bán, tiền hoàn đúng, nhập lại đúng lô đã xuất
- ✅ Bán không kê đơn (3.1) không chứa thuốc kê đơn
- ✅ Bán theo đơn: thuốc thuộc đơn, đúng khách; đơn 'Đã bán' ↔ có hóa đơn đã thanh toán
- ✅ Liều kê trong đơn không vượt liều tối đa (G-07)
- ✅ Không bán thuốc chứa hoạt chất khách đã khai báo dị ứng
- ✅ Q-14: đơn giá hóa đơn = giá hiệu lực tại thời điểm bán (MedicinePriceHistories)
- ✅ Q-14: đổi giá do Owner kèm lý do; giá hiện hành của thuốc/đơn vị quy đổi = dòng lịch sử đang mở
- ✅ Khuyến mãi đúng phạm vi & thời gian; chiết khấu ≤ 10% (PricePolicies)
- ✅ Nhập kho: tổng phiếu, số lượng đã nhận của đơn, trạng thái giao đủ/thiếu; phiếu đang kiểm nhận chưa sinh lô
- ✅ Thanh toán NCC = giá trị phiếu nhập; tiền hoàn = giá trị phiếu trả đã quyết toán
- ✅ Phiếu trả NCC: tổng tiền = tổng dòng
- ✅ Không bán lô sau khi tiếp nhận công văn thu hồi; lô 'Đang thu hồi' đều có phiếu
- ✅ Lô hết hạn trước đợt hủy 05/10 đã được hủy hết
- ✅ HĐĐT, SMS nhắc lịch, liên thông DQG khớp trạng thái chứng từ nguồn
- ✅ Ca: đúng 1 ca đang mở (31/10 ca chiều); mọi ca đã chốt có biên bản chốt tiền
- ✅ FEFO: không có lô hạn gần hơn còn hàng mà bị bỏ qua khi xuất bán

**Chưa chạy trên SQL Server thật** — cần nhóm chạy thử (mục 1). Cú pháp dùng: `SET IDENTITY_INSERT`, `INSERT … VALUES` (≤ 400 dòng/lệnh), chuỗi `N'…'`, ngày `'YYYY-MM-DD'`, ngày giờ `'YYYY-MM-DDTHH:MM:SS'`.
