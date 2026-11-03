# Website Quản lý Hiệu thuốc — ASP.NET Core MVC (.NET 10)

Đồ án 3 tín chỉ, nhóm 5 người. Nghiệp vụ theo Chương 2 (BFD 78 chức năng lá) — **đã chốt, không sửa**.
Mọi quyết định, quy ước, tiến độ: xem **`PROJECT_CONTEXT.md`** (luôn đọc file này trước khi code).

## Công nghệ

| Thành phần | Phiên bản / lựa chọn |
|---|---|
| .NET SDK | 10.0.x (`global.json`: 10.0.100, roll-forward `latestFeature`) |
| Web | ASP.NET Core MVC + Razor View + Bootstrap 5 |
| CSDL | SQL Server — **Database-first**: script SQL là nguồn gốc, EF Core 10 chỉ scaffold |
| ORM / công cụ | EF Core 10.0.14, `dotnet-ef` 10.0.14 (ghim trong `.config/dotnet-tools.json`) |
| Đăng nhập | ASP.NET Core Identity (`IdentityUser`, `IdentityRole`), 3 vai trò Owner / Manager / Pharmacist |

## Cấu trúc thư mục

```
BTL_AppQuanLiNhaThuoc/
├─ PharmacyManagementDB.sql              # Schema v2 (58 bảng) — nguồn gốc của database
├─ PharmacyManagementDB_SampleData.sql   # Dữ liệu mẫu (chạy SAU schema)
├─ PharmacyManagementDB_v2.1.sql         # Bổ sung v2.1: 2 chỉ mục (một Owner duy nhất, email không trùng)
├─ PharmacyManagement_DataDictionary.xlsx, CHANGELOG_DB.md, ENUMS.md, SAMPLE_DATA.md, PROJECT_CONTEXT.md
├─ tools/
│  ├─ 01-create-solution.ps1             # Tạo solution + gộp template MVC (chỉ chạy 1 lần, không ghi đè file đã có)
│  ├─ 02-scaffold.ps1                    # Scaffold Entity + DbContext từ database rồi build (chạy lại khi schema đổi)
│  └─ 03-fetch-assets.ps1                # Tải font Be Vietnam Pro + Bootstrap Icons về wwwroot/lib (chạy 1 lần)
├─ PharmacyManagement.sln
└─ PharmacyManagement/
   ├─ Areas/Admin | Sales | Inventory | Catalog | Reports   # Area theo phân hệ (Q-20); quyền kiểm tra theo mã BFD
   ├─ Authorization/                      # PermissionCodes, RoleNames, policy + handler phân quyền
   ├─ BackgroundJobs/                     # Tác vụ "Hệ thống": nhắc lịch 3.5.2.2, liên thông 4.4
   ├─ Configuration/                      # EmailOptions, BootstrapOptions (giá trị bí mật nằm ở User Secrets)
   ├─ Controllers/  Views/  ViewComponents/  wwwroot/
   ├─ Data/PharmacyDbContext.cs           # SINH TỰ ĐỘNG — không sửa tay
   ├─ Models/Entities/                    # SINH TỰ ĐỘNG — không sửa tay
   ├─ Models/Enums/  Models/ViewModels/
   ├─ Identity/  Middleware/  Helpers/
   └─ Services/Interfaces | Implementations
```

## Dựng dự án lần đầu (một người làm, rồi đẩy lên Git)

Chạy trong PowerShell tại thư mục gốc, **đúng thứ tự**:

1. `powershell -ExecutionPolicy Bypass -File .\tools\01-create-solution.ps1` — tạo solution, gói NuGet, User Secrets. Script không build (lúc này chưa có entity).
2. Trong SSMS chạy `PharmacyManagementDB_v2.1.sql` trên database đã có (script chỉ thêm 2 chỉ mục, chạy lại không lỗi; nếu DB đang có 2 Owner hoặc email trùng thì script dừng và báo, không sửa gì).
3. `powershell -ExecutionPolicy Bypass -File .\tools\02-scaffold.ps1` — sinh `Models/Entities`, `Data/PharmacyDbContext.cs`, rồi build cả solution. **Gửi lại cho người phụ trách toàn bộ lỗi/cảnh báo build nếu có.**
4. `powershell -ExecutionPolicy Bypass -File .\tools\03-fetch-assets.ps1` — tải font và icon (cần Internet một lần, sau đó commit thư mục `wwwroot/lib`).

## Cài đặt cho mỗi thành viên

1. Cài .NET SDK 10, Visual Studio bản hỗ trợ .NET 10, SQL Server (Express được) + SSMS.
2. Lấy mã nguồn (clone repo khi nhóm đã có GitHub).
3. Tạo database: trong SSMS chạy `PharmacyManagementDB.sql`, `PharmacyManagementDB_SampleData.sql`, rồi `PharmacyManagementDB_v2.1.sql`.
4. Tại thư mục gốc: `dotnet tool restore` (cài đúng `dotnet-ef` 10.0.14).
5. Nếu SQL Server của bạn **không** phải `.\SQLEXPRESS`, đặt chuỗi kết nối riêng (không sửa `appsettings.json`):
   ```powershell
   dotnet user-secrets set "ConnectionStrings:PharmacyDb" "Server=TEN_MAY;Database=PharmacyManagementDB;Trusted_Connection=True;TrustServerCertificate=True" --project PharmacyManagement
   ```
6. Đặt cấu hình gửi email và Owner đầu tiên (mục dưới).
7. Mở `PharmacyManagement.sln` bằng Visual Studio → Build → Run (hoặc `dotnet run --project PharmacyManagement`).

## Bí mật: chỉ đặt bằng User Secrets hoặc biến môi trường

Không ghi email gửi, App Password hay mật khẩu vào `appsettings.json`, script SQL hay mã nguồn. User Secrets lưu ngoài thư mục dự án nên không bị commit.

```powershell
# Gửi email qua Gmail SMTP — App Password tạo tại Google Account > Bảo mật > Mật khẩu ứng dụng (cần bật xác minh 2 bước)
dotnet user-secrets set "Email:SenderAddress" "tai-khoan-gui@gmail.com" --project PharmacyManagement
dotnet user-secrets set "Email:AppPassword"   "xxxx xxxx xxxx xxxx"     --project PharmacyManagement
# Tùy chọn: địa chỉ ghi trong email khi chạy trong mạng LAN, ví dụ http://192.168.1.10:5000
dotnet user-secrets set "Email:PublicBaseUrl" "http://192.168.1.10:5000" --project PharmacyManagement
```

Biến môi trường tương ứng (máy chủ chạy thật): `Email__SenderAddress`, `Email__AppPassword`, `Email__PublicBaseUrl`.

## Tài khoản

- Mỗi tài khoản có ba thông tin độc lập: **tên tài khoản** (phần trước @ của email — đăng nhập, hiện ở góc trên, ảnh đại diện chữ viết tắt), **email** (đăng nhập, nhận mã), **họ tên** trong hồ sơ nhân viên (hóa đơn, báo cáo, chứng từ, sổ GPP, nhật ký). Không dùng tên tài khoản/email/tên vai trò thay họ tên.
- Hệ thống có **đúng một Owner** (chủ nhà thuốc). Không có trang đăng ký; không có chức năng mời hay nâng cấp ai thành Owner. CSDL chặn thêm bằng chỉ mục `UX_AspNetUserRoles_SingleOwner`.
- Owner mời Quản lý / Dược sĩ ở **Quản trị → Tài khoản**: nhập email → hệ thống gửi mã 6 số (15 phút, sai tối đa 5 lần) → nhân viên vào **/Account/Activate**, nhập mã, tự đặt mật khẩu. Mỗi email chỉ một tài khoản; đổi email phải qua Owner và nhân viên xác nhận bằng mã gửi tới email mới.
- Chức năng quản trị (tài khoản, phân quyền, thiết lập, nhật ký) chỉ mở khi email của Owner đã xác minh. Server kiểm tra quyền, không chỉ ẩn nút.

### Khởi tạo Owner đầu tiên (chỉ môi trường Development, chỉ một lần)

```powershell
dotnet user-secrets set "Bootstrap:OwnerEmail"           "quynhpham.eculis@gmail.com" --project PharmacyManagement
dotnet user-secrets set "Bootstrap:OwnerInitialPassword" "<mật khẩu tạm>"             --project PharmacyManagement
dotnet user-secrets set "Bootstrap:OwnerEmployeeCode"    "NV01"                        --project PharmacyManagement
# Họ và tên thật — bắt buộc nếu không dùng OwnerEmployeeCode; nếu có thì ghi đè họ tên của hồ sơ đó
dotnet user-secrets set "Bootstrap:OwnerFullName"        "<họ và tên thật>"           --project PharmacyManagement
```

Tên tài khoản tự sinh từ email (phần trước @, ví dụ `quynhpham.eculis`); đăng nhập bằng email hoặc tên tài khoản. Lần đăng nhập đầu, Owner xác nhận họ tên thật và đổi mật khẩu tạm; sau đó sửa họ tên ở **Tài khoản của tôi → Chỉnh sửa hồ sơ**.

Lần chạy đầu, ứng dụng tạo Owner (mật khẩu được Identity băm), gắn với nhân viên NV01 và ghi mốc `PharmacySettings.OwnerSetupCompletedAt`. Từ đó cơ chế khởi tạo đóng vĩnh viễn, kể cả khi xóa secrets. Đăng nhập lần đầu: bắt buộc đổi mật khẩu tạm, rồi xác minh email bằng mã 6 số. Sau khi khởi tạo xong nên xóa mật khẩu tạm khỏi máy: `dotnet user-secrets remove "Bootstrap:OwnerInitialPassword" --project PharmacyManagement`.

### Khôi phục Owner khi mất quyền truy cập

Quên mật khẩu mà email còn dùng được: dùng **Quên mật khẩu** ở trang đăng nhập. Mất luôn email: người quản trị máy chủ chạy lệnh (không có trên giao diện web):

```powershell
dotnet run --project PharmacyManagement -- owner-recovery --email email-moi@gmail.com
```

Lệnh đổi email của **Owner hiện có** và gửi mã đặt lại mật khẩu tới email mới. Lệnh không tạo Owner thứ hai: khi đã có Owner nó chỉ sửa tài khoản đó; chỉ khi CSDL không còn Owner nào mới gán lại (dùng `--employee-code NVxx` cho nhân viên chưa có tài khoản), và không bao giờ nâng cấp tài khoản Quản lý / Dược sĩ. Mọi lần chạy được ghi nhật ký.

## Quy tắc làm việc với database (Database-first)

- **Không** dùng EF Core Migrations. Muốn đổi schema: sửa `PharmacyManagementDB.sql` (ghi vào `CHANGELOG_DB.md`, cả nhóm duyệt) → chạy trên DB → chạy lại `tools\02-scaffold.ps1`.
- **Không sửa tay** file trong `Models/Entities` và `Data/PharmacyDbContext.cs` — lần scaffold sau sẽ ghi đè. Cần tùy biến thì tạo `partial class` ở file khác, ví dụ `Data/PharmacyDbContext.Custom.cs`.
- Cột trạng thái trong entity là `byte` (tinyint). So sánh bằng enum có ép kiểu:
  ```csharp
  var paid = await db.SalesInvoices.Where(i => i.Status == (byte)InvoiceStatus.Paid).ToListAsync();
  ```
- Biến động kho luôn dùng `InventoryReferenceTypes.For(type)` để lấy đúng `ReferenceType` (CHECK trong DB sẽ chặn nếu sai).
- Quy tắc nghiệp vụ tầng ứng dụng bắt buộc: xem `CHANGELOG_DB.md` mục 8 và `PROJECT_CONTEXT.md` mục 5.

## Quy ước code

- Service: `I{Tên}Service` / `{Tên}Service`; ViewModel: `{Màn hình}ViewModel`; hàm async có hậu tố `Async`.
- Phân quyền theo mã chức năng BFD: `[Authorize(Policy = PermissionCodes.F3_1_4)]`. Không kiểm tra theo tên vai trò rải rác trong code. Menu tự ẩn mục không có quyền, nhưng controller vẫn phải gắn policy.
- Giao diện: màu và bo góc lấy từ `wwwroot/css/tokens.css`, thành phần chung trong `site.css`. Không viết màu cứng hoặc `style="..."` trong view.
- Nghiệp vụ nằm trong Service, Controller chỉ điều phối. Nghiệp vụ làm thay đổi tồn kho, tiền, chứng từ chạy trong transaction.

## Git

`main` (ổn định, chỉ merge qua PR) ← `develop` ← `feature/<module>-<mo-ta>` (ví dụ `feature/kho-nhap-kho`). Mỗi PR cần 1 người review.
