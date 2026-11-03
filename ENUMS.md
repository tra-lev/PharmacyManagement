# ENUMS — Giá trị trạng thái / loại (PharmacyManagementDB v2)

> Sinh từ `PharmacyManagementDB.sql` v2 — 31/10/2026. Mỗi enum đã được đối chiếu tự động với CHECK constraint tương ứng (khớp 100%).
> Cột `tinyint` trong SQL Server ↔ enum C# kiểu `byte`. Ví dụ: `public enum InvoiceStatus : byte { PendingPayment = 0, Paid = 1, Cancelled = 2 }`.
> Hai cột `tinyint` không phải enum (là số): `Medicines.MinPatientAgeYears`, `PrescriptionDetails.TimesPerDay`.

## Danh sách cột dùng enum

| Bảng.Cột | Enum C# | DEFAULT |
|---|---|---|
| Employees.Gender | `Gender` | — |
| Customers.Gender | `Gender` | — |
| StorageLocations.LocationType | `LocationType` | 2 |
| IngredientInteractions.Severity | `InteractionSeverity` | — |
| PurchaseOrders.Status | `PurchaseOrderStatus` | 0 |
| GoodsReceipts.Status | `GoodsReceiptStatus` | 0 |
| MedicineBatches.Status | `BatchStatus` | 1 |
| InventoryTransactions.TransactionType | `InventoryTransactionType` | — |
| Stocktakes.Status | `StocktakeStatus` | 0 |
| StockDisposals.Status | `DisposalStatus` | 0 |
| StockDisposalDetails.ReasonType | `DisposalReason` | — |
| Shifts.Status | `ShiftStatus` | 0 |
| Prescriptions.Status | `PrescriptionStatus` | 0 |
| SalesInvoices.Status | `InvoiceStatus` | 0 |
| SalesInvoices.EInvoiceStatus | `EInvoiceStatus` | 0 |
| Payments.PaymentMethod | `PaymentMethod` | — |
| Payments.Status | `PaymentStatus` | 1 |
| Returns.Status | `ReturnStatus` | 0 |
| Returns.RefundMethod | `PaymentMethod` | 1 |
| ReturnDetails.RestockStatus | `RestockStatus` | 0 |
| SupplierReturns.Status | `SupplierReturnStatus` | 0 |
| SupplierPayments.PaymentType | `SupplierPaymentType` | — |
| SupplierPayments.PaymentMethod | `SupplierPaymentMethod` | — |
| SupplierPayments.Status | `PaymentStatus` | 1 |
| ProductRecalls.Status | `RecallStatus` | 0 |
| CustomerReminders.Channel | `ReminderChannel` | 0 |
| CustomerReminders.Status | `ReminderStatus` | 0 |
| IntegrationLogs.Status | `IntegrationStatus` | 0 |

## Giá trị từng enum

### `Gender` — Employees.Gender, Customers.Gender

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `Male` | Nam |
| 2 | `Female` | Nữ |
| 3 | `Other` | Khác |

### `LocationType` — StorageLocations.LocationType

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `Cabinet` | Tủ |
| 2 | `Shelf` | Kệ |

### `InteractionSeverity` — IngredientInteractions.Severity

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `Minor` | Nhẹ |
| 2 | `Moderate` | Trung bình |
| 3 | `Severe` | Nghiêm trọng |

### `PurchaseOrderStatus` — PurchaseOrders.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Draft` | Nháp |
| 1 | `AwaitingDelivery` | Chờ giao (đã gửi NCC) |
| 2 | `PartiallyDelivered` | Giao thiếu |
| 3 | `Delivered` | Đã giao |
| 4 | `Cancelled` | Hủy |

### `GoodsReceiptStatus` — GoodsReceipts.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Inspecting` | Đang kiểm nhận — biên bản nhận hàng (2.2.1.3) |
| 1 | `Posted` | Đã nhập kho, đã cộng tồn (2.2.2–2.2.4) |
| 2 | `Cancelled` | Hủy |

### `BatchStatus` — MedicineBatches.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `Available` | Khả dụng |
| 2 | `Locked` | Bị khóa |
| 3 | `Recalled` | Đang thu hồi (khóa từ 2.3.3.1) |
| 4 | `Discontinued` | Ngừng sử dụng |

### `InventoryTransactionType` — InventoryTransactions.TransactionType

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `PurchaseReceipt` | Nhập mua — GoodsReceiptDetail, > 0 |
| 2 | `SaleIssue` | Xuất bán FEFO — SalesIssueAllocation, < 0 |
| 3 | `SupplierReturn` | Trả NCC — SupplierReturnDetail, < 0 |
| 4 | `Disposal` | Hủy thuốc — StockDisposalDetail, < 0 |
| 5 | `Recall` | Thu hồi — RecallBatch, < 0 |
| 6 | `StocktakeAdjustment` | Điều chỉnh kiểm kê — StocktakeDetail, ≠ 0 |
| 7 | `CustomerReturnRestock` | Nhập lại hàng khách trả — ReturnDetail, > 0 |

### `StocktakeStatus` — Stocktakes.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Counting` | Đang kiểm đếm |
| 1 | `PendingApproval` | Chờ Owner duyệt |
| 2 | `Approved` | Đã duyệt |
| 3 | `Adjusted` | Manager đã điều chỉnh tồn |
| 4 | `Rejected` | Từ chối |

### `DisposalStatus` — StockDisposals.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Draft` | Nháp |
| 1 | `Posted` | Đã ghi sổ (đã trừ tồn) |
| 2 | `Cancelled` | Hủy |

### `DisposalReason` — StockDisposalDetails.ReasonType

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `Expired` | Hết hạn |
| 2 | `Damaged` | Hỏng |
| 3 | `Dented` | Móp méo |
| 4 | `Other` | Khác |

### `ShiftStatus` — Shifts.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Open` | Đang mở |
| 1 | `HandedOver` | Đã bàn giao |
| 2 | `Closed` | Đã chốt |

### `PrescriptionStatus` — Prescriptions.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Saved` | Đã lưu (hợp lệ) |
| 1 | `Dispensed` | Đã bán |
| 2 | `Cancelled` | Hủy |

### `InvoiceStatus` — SalesInvoices.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `PendingPayment` | Chờ thanh toán |
| 1 | `Paid` | Đã thanh toán |
| 2 | `Cancelled` | Đã hủy |

### `EInvoiceStatus` — SalesInvoices.EInvoiceStatus

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `NotIssued` | Chưa xuất |
| 1 | `Issued` | Đã xuất |
| 2 | `Failed` | Lỗi |

### `PaymentMethod` — Payments.PaymentMethod, Returns.RefundMethod

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `Cash` | Tiền mặt |
| 2 | `BankTransfer` | Chuyển khoản/QR |
| 3 | `Card` | Thẻ |

### `PaymentStatus` — Payments.Status, SupplierPayments.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `Succeeded` | Thành công |
| 2 | `Voided` | Đã hủy |

### `ReturnStatus` — Returns.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `RefundedPendingRestock` | Đã hoàn tiền — chờ nhập kho |
| 1 | `Completed` | Hoàn tất |
| 2 | `Cancelled` | Hủy |

### `RestockStatus` — ReturnDetails.RestockStatus

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Pending` | Chờ nhập |
| 1 | `Restocked` | Đã nhập lại |
| 2 | `Rejected` | Không nhập |

### `SupplierReturnStatus` — SupplierReturns.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Draft` | Nháp |
| 1 | `Issued` | Đã xuất trả (đã trừ tồn) |
| 2 | `Settled` | Đã quyết toán |
| 3 | `Cancelled` | Hủy |

### `SupplierPaymentType` — SupplierPayments.PaymentType

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `PaymentToSupplier` | Chi trả NCC — phiếu chi (2.2.5) |
| 2 | `RefundFromSupplier` | Nhận tiền hoàn từ NCC — phiếu thu (2.3.5) |

### `SupplierPaymentMethod` — SupplierPayments.PaymentMethod

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 1 | `Cash` | Tiền mặt |
| 2 | `BankTransfer` | Chuyển khoản |

### `RecallStatus` — ProductRecalls.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Received` | Mới tiếp nhận |
| 1 | `InProgress` | Đang thu hồi |
| 2 | `Completed` | Hoàn tất |

### `ReminderChannel` — CustomerReminders.Channel

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Sms` | SMS |
| 1 | `Email` | Email |

### `ReminderStatus` — CustomerReminders.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Pending` | Chờ gửi |
| 1 | `Sent` | Đã gửi |
| 2 | `Failed` | Lỗi |
| 3 | `Cancelled` | Hủy |

### `IntegrationStatus` — IntegrationLogs.Status

| Giá trị | Tên C# | Ý nghĩa |
|---|---|---|
| 0 | `Pending` | Chờ gửi |
| 1 | `Succeeded` | Thành công |
| 2 | `Failed` | Lỗi |

## Hằng chuỗi `InventoryTransactions.ReferenceType`

| TransactionType | ReferenceType | ReferenceId trỏ tới | Dấu QuantityChange |
|---|---|---|---|
| 1 | `GoodsReceiptDetail` | GoodsReceiptDetails.GoodsReceiptDetailId | > 0 |
| 2 | `SalesIssueAllocation` | SalesIssueAllocations.AllocationId | < 0 |
| 3 | `SupplierReturnDetail` | SupplierReturnDetails.SupplierReturnDetailId | < 0 |
| 4 | `StockDisposalDetail` | StockDisposalDetails.DisposalDetailId | < 0 |
| 5 | `RecallBatch` | RecallBatches.RecallBatchId | < 0 |
| 6 | `StocktakeDetail` | StocktakeDetails.StocktakeDetailId | ≠ 0 |
| 7 | `ReturnDetail` | ReturnDetails.ReturnDetailId | > 0 |

Cặp (TransactionType, ReferenceType, dấu) được CHECK `CK_InventoryTransactions_TypeRef` ràng buộc.

## Vòng đời trạng thái chính

- **Hóa đơn** (`InvoiceStatus`): 0 Chờ thanh toán → 1 Đã thanh toán (khi tổng Payments thành công = TotalAmount; cùng transaction: FEFO + trừ tồn + ledger). 0 → 2 Đã hủy (chỉ hóa đơn chờ thanh toán, cần quyền `SYS09.CancelInvoice`, bắt buộc người/thời điểm/lý do).
- **Phiếu nhập** (`GoodsReceiptStatus`): 0 Đang kiểm nhận (biên bản nhận hàng) → 1 Đã nhập kho (tạo lô, cộng tồn, ledger loại 1) | → 2 Hủy (chỉ khi còn 0).
- **Kiểm kê** (`StocktakeStatus`): 0 Đang kiểm đếm → 1 Chờ duyệt → 2 Đã duyệt (Owner) → 3 Đã điều chỉnh (Manager, ledger loại 6 theo Difference) | 1 → 4 Từ chối (Owner, bắt buộc ApprovalNote).
- **Ca** (`ShiftStatus`): 0 Đang mở → 1 Đã bàn giao (HandedOverAt) → 2 Đã chốt (ClosedAt + ShiftClosings). Mỗi nhân viên tối đa 1 ca chưa chốt.
- **Đổi trả** (`ReturnStatus` / `RestockStatus`): phiếu tạo ở 3.4.3 với Status 0, mọi dòng RestockStatus 0 → Manager xử lý 3.4.4 từng dòng (1 Đã nhập lại + ledger loại 7, hoặc 2 Không nhập) → khi hết dòng 0 thì phiếu sang 1 Hoàn tất.
- **Lô** (`BatchStatus`): chỉ lô 1 Khả dụng, còn hạn, đủ tồn mới được FEFO chọn. 2.3.3.1 tiếp nhận thu hồi → đặt 3 ngay.
