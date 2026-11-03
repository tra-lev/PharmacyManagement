<#
  Bước 2.1b — Scaffold Entity + DbContext từ database (DATABASE-FIRST) rồi build. Chạy lại được mỗi khi schema đổi.

  Cách chạy (PowerShell, tại thư mục gốc dự án):
      powershell -ExecutionPolicy Bypass -File .\tools\02-scaffold.ps1
  Dùng chuỗi kết nối khác (máy có tên SQL Server khác):
      powershell -ExecutionPolicy Bypass -File .\tools\02-scaffold.ps1 -Connection "Server=localhost;Database=PharmacyManagementDB;Trusted_Connection=True;TrustServerCertificate=True"
  Nếu không truyền -Connection: lấy ConnectionStrings:PharmacyDb trong User Secrets, không có thì lấy trong appsettings.json.

  Vì code ứng dụng dùng các Entity chưa tồn tại lúc đầu, việc scaffold chạy qua một project phụ nhỏ
  (tools\.scaffold-host) — project này không nằm trong solution và không chứa code nghiệp vụ.

  Kết quả:
    - PharmacyManagement\Models\Entities\*.cs   : 51 entity (tên bảng số ít: Medicine, SalesInvoice, ...)
    - PharmacyManagement\Data\PharmacyDbContext.cs : kế thừa IdentityDbContext<IdentityUser, IdentityRole, string>
  7 bảng AspNet* KHÔNG scaffold — Identity tự ánh xạ. Các file sinh ra KHÔNG sửa tay; tùy biến bằng partial class
  trong file khác (ví dụ Data\PharmacyDbContext.Custom.cs). Script ghi đè (--force) mọi file trong Models\Entities.
#>
param(
    [string]$Connection = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$projDir = Join-Path $root 'PharmacyManagement'
$projFile = Join-Path $projDir 'PharmacyManagement.csproj'
$efVersion = '10.0.14'
if (-not (Test-Path $projFile)) { throw 'Chưa có project — hãy chạy tools\01-create-solution.ps1 trước.' }

function Invoke-Dotnet([string[]]$DotnetArgs, [string]$Display = '') {
    if (-not $Display) { $Display = $DotnetArgs -join ' ' }
    Write-Host ">> dotnet $Display" -ForegroundColor Cyan
    & dotnet @DotnetArgs
    if ($LASTEXITCODE -ne 0) { throw "Lệnh thất bại: dotnet $Display" }
}

# Chuỗi kết nối: tham số > User Secrets > appsettings.json
if (-not $Connection) {
    $secretLine = & dotnet user-secrets list --project $projFile 2>$null | Where-Object { $_ -like 'ConnectionStrings:PharmacyDb = *' } | Select-Object -First 1
    if ($secretLine) { $Connection = ($secretLine -split ' = ', 2)[1]; Write-Host 'Dùng chuỗi kết nối trong User Secrets.' -ForegroundColor Yellow }
    else {
        $settings = Get-Content (Join-Path $projDir 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $Connection = $settings.ConnectionStrings.PharmacyDb
        Write-Host 'Dùng chuỗi kết nối trong appsettings.json.' -ForegroundColor Yellow
    }
}
if (-not $Connection) { throw 'Không tìm thấy chuỗi kết nối ConnectionStrings:PharmacyDb.' }

# Project phụ chỉ để scaffold
$hostDir = Join-Path $root 'tools\.scaffold-host'
$hostProj = Join-Path $hostDir 'ScaffoldHost.csproj'
if (-not (Test-Path $hostProj)) {
    Invoke-Dotnet @('new', 'console', '-n', 'ScaffoldHost', '-o', $hostDir, '--framework', 'net10.0', '--no-restore')
    foreach ($pkg in @('Microsoft.EntityFrameworkCore.SqlServer', 'Microsoft.EntityFrameworkCore.Design')) {
        Invoke-Dotnet @('add', $hostProj, 'package', $pkg, '--version', $efVersion)
    }
}

# 51 bảng nghiệp vụ (58 bảng trừ 7 bảng AspNet*)
$tables = @(
    'Employees', 'Permissions', 'RolePermissions',
    'Categories', 'Units', 'Manufacturers', 'ActiveIngredients', 'Medicines', 'MedicineIngredients', 'MedicineUnitConversions',
    'IngredientInteractions', 'MedicineContraindications', 'Promotions',
    'Suppliers', 'Customers', 'CustomerAllergies', 'Warehouses', 'StorageLocations',
    'PurchaseOrders', 'PurchaseOrderDetails', 'GoodsReceipts', 'GoodsReceiptDetails', 'MedicineBatches',
    'InventoryTransactions', 'Stocktakes', 'StocktakeDetails', 'StockDisposals', 'StockDisposalDetails',
    'Shifts', 'ShiftClosings', 'Prescriptions', 'PrescriptionDetails',
    'SalesInvoices', 'SalesInvoiceDetails', 'SalesIssueAllocations', 'Payments', 'Returns', 'ReturnDetails',
    'SupplierReturns', 'SupplierReturnDetails', 'SupplierPayments',
    'InventoryPolicies', 'PricePolicies', 'MedicinePriceHistories', 'PharmacySettings', 'StorageEnvironmentLogs',
    'ProductRecalls', 'RecallBatches', 'CustomerReminders', 'IntegrationLogs', 'AuditLogs'
)
if ($tables.Count -ne 51) { throw "Danh sách bảng phải có 51 bảng (hiện có $($tables.Count))." }
$tableArgs = @()
foreach ($t in $tables) { $tableArgs += @('--table', "dbo.$t") }

Invoke-Dotnet @('tool', 'restore')
$entDir = Join-Path $projDir 'Models\Entities'
$ctxDir = Join-Path $projDir 'Data'
New-Item -ItemType Directory -Force -Path $entDir, $ctxDir | Out-Null
$scaffold = @('ef', 'dbcontext', 'scaffold', $Connection, 'Microsoft.EntityFrameworkCore.SqlServer',
              '--project', $hostProj, '--startup-project', $hostProj,
              '--context', 'PharmacyDbContext', '--context-dir', $ctxDir, '--context-namespace', 'PharmacyManagement.Data',
              '--output-dir', $entDir, '--namespace', 'PharmacyManagement.Models.Entities',
              '--no-onconfiguring', '--force') + $tableArgs
Invoke-Dotnet $scaffold 'ef dbcontext scaffold "<chuỗi kết nối>" ... (51 bảng)'

# Hậu xử lý tự động (không sửa tay): DbContext kế thừa IdentityDbContext để Identity + nghiệp vụ dùng chung
# một DbContext, chung transaction (ví dụ 1.3.1 tạo tài khoản + hồ sơ nhân viên).
$ctxPath = Join-Path $ctxDir 'PharmacyDbContext.cs'
$ctx = [System.IO.File]::ReadAllText($ctxPath)
if ($ctx -notmatch 'IdentityDbContext') {
    $before = $ctx
    $ctx = $ctx.Replace('public partial class PharmacyDbContext : DbContext',
                        'public partial class PharmacyDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>')
    $ctx = [regex]::Replace($ctx, '(protected override void OnModelCreating\(ModelBuilder modelBuilder\)\s*\{)',
                            '$1' + "`r`n        base.OnModelCreating(modelBuilder); // Identity: AspNetUsers, AspNetRoles, ...`r`n")
    if ($ctx -eq $before -or $ctx -notmatch 'base\.OnModelCreating') { throw 'Không hậu xử lý được PharmacyDbContext.cs — gửi file này cho Claude.' }
    $ctx = "using Microsoft.AspNetCore.Identity;`r`nusing Microsoft.AspNetCore.Identity.EntityFrameworkCore;`r`n" + $ctx
    [System.IO.File]::WriteAllText($ctxPath, $ctx, (New-Object System.Text.UTF8Encoding($true)))
}

$entityCount = (Get-ChildItem $entDir -Filter *.cs).Count
Write-Host "Đã sinh $entityCount entity (kỳ vọng 51)." -ForegroundColor Yellow
Invoke-Dotnet @('build', 'PharmacyManagement.sln')
Write-Host ''
Write-Host 'HOÀN TẤT bước 2.1b. Gửi cho Claude: số entity ở trên + toàn bộ lỗi/cảnh báo (warning) của lệnh scaffold/build nếu có.' -ForegroundColor Green
