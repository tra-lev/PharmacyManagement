<#
  Bước 2.1a — Tạo solution PharmacyManagement (.NET 10, ASP.NET Core MVC). CHỈ CHẠY MỘT LẦN.

  Cách chạy (PowerShell, tại thư mục gốc dự án E:\LapTrinhWeb\BTL_AppQuanLiNhaThuoc):
      powershell -ExecutionPolicy Bypass -File .\tools\01-create-solution.ps1

  Script sẽ:
    1. Tạo global.json (SDK 10.0.x) và solution PharmacyManagement.sln.
    2. Sinh project MVC từ template vào thư mục tạm rồi CHÉP SANG PharmacyManagement\ những file CHƯA CÓ
       (file do nhóm/Claude đã viết sẵn như Program.cs, _Layout.cshtml, site.css… được GIỮ NGUYÊN).
    3. Cài gói EF Core SQL Server, EF Core Design, ASP.NET Core Identity EF Core (cùng phiên bản 10.0.14).
    4. Tạo tool manifest (.config/dotnet-tools.json) ghim dotnet-ef 10.0.14 cho cả nhóm.
    5. Bật User Secrets, tạo .gitignore, các thư mục chuẩn (Area theo module).
  KHÔNG build ở bước này: code ứng dụng cần Entity/DbContext do bước 02 sinh ra.
  Không chạm vào database. Không xóa file nào của dự án.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$projDir = Join-Path $root 'PharmacyManagement'
$proj = Join-Path $projDir 'PharmacyManagement.csproj'
$efVersion = '10.0.14'

if (Test-Path $proj) { throw "Đã có $proj — script này chỉ chạy một lần." }
if (Test-Path (Join-Path $root 'PharmacyManagement.sln')) { throw 'Đã có PharmacyManagement.sln — script này chỉ chạy một lần.' }

function Invoke-Dotnet([string[]]$DotnetArgs) {
    Write-Host ">> dotnet $($DotnetArgs -join ' ')" -ForegroundColor Cyan
    & dotnet @DotnetArgs
    if ($LASTEXITCODE -ne 0) { throw "Lệnh thất bại: dotnet $($DotnetArgs -join ' ')" }
}

# 1. global.json + solution
if (-not (Test-Path (Join-Path $root 'global.json'))) {
    Invoke-Dotnet @('new', 'globaljson', '--sdk-version', '10.0.100', '--roll-forward', 'latestFeature')
}
Invoke-Dotnet @('new', 'sln', '-n', 'PharmacyManagement', '--format', 'sln')

# 2. Template MVC -> thư mục tạm -> chép những file chưa có (không ghi đè file đã viết sẵn)
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('pm-template-' + [guid]::NewGuid().ToString('N'))
Invoke-Dotnet @('new', 'mvc', '-n', 'PharmacyManagement', '-o', $tmp, '--framework', 'net10.0', '--no-restore')
New-Item -ItemType Directory -Force -Path $projDir | Out-Null
$copied = 0; $kept = 0
Get-ChildItem -Path $tmp -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($tmp.Length).TrimStart('\', '/')
    if ($rel -match '^(bin|obj)[\\/]') { return }
    $dest = Join-Path $projDir $rel
    if (Test-Path $dest) { $kept++; Write-Host "   giữ file đã có: $rel" -ForegroundColor DarkGray }
    else {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item $_.FullName $dest
        $copied++
    }
}
Remove-Item -Recurse -Force $tmp
Write-Host "Template: chép $copied file mới, giữ nguyên $kept file đã viết sẵn." -ForegroundColor Yellow
Invoke-Dotnet @('sln', 'PharmacyManagement.sln', 'add', 'PharmacyManagement\PharmacyManagement.csproj')

# 3. NuGet packages (cùng phiên bản với dotnet-ef)
foreach ($pkg in @('Microsoft.EntityFrameworkCore.SqlServer', 'Microsoft.EntityFrameworkCore.Design',
                   'Microsoft.AspNetCore.Identity.EntityFrameworkCore')) {
    Invoke-Dotnet @('add', 'PharmacyManagement\PharmacyManagement.csproj', 'package', $pkg, '--version', $efVersion)
}

# 4. Tool manifest: cả nhóm dùng chung dotnet-ef 10.0.14 (thành viên khác chỉ cần: dotnet tool restore)
if (-not (Test-Path (Join-Path $root '.config\dotnet-tools.json'))) { Invoke-Dotnet @('new', 'tool-manifest') }
Invoke-Dotnet @('tool', 'install', 'dotnet-ef', '--version', $efVersion)

# 5. User Secrets, .gitignore, thư mục chuẩn
Invoke-Dotnet @('user-secrets', 'init', '--project', 'PharmacyManagement\PharmacyManagement.csproj')
if (-not (Test-Path (Join-Path $root '.gitignore'))) { Invoke-Dotnet @('new', 'gitignore') }

# Area theo module (Q-20): một trang dùng chung cho nhiều vai trò, quyền quyết định nội dung
$dirs = @('Models\Entities', 'Data', 'BackgroundJobs',
          'Areas\Sales', 'Areas\Inventory', 'Areas\Catalog', 'Areas\Reports')
foreach ($d in $dirs) {
    $p = Join-Path $projDir $d
    New-Item -ItemType Directory -Force -Path $p | Out-Null
    if (-not (Get-ChildItem -Path $p -Force | Select-Object -First 1)) { New-Item -ItemType File -Path (Join-Path $p '.gitkeep') | Out-Null }
}

Write-Host ''
Write-Host 'HOÀN TẤT bước 2.1a. Tiếp theo:' -ForegroundColor Green
Write-Host '   1) Trong SSMS chạy PharmacyManagementDB_v2.1.sql' -ForegroundColor Green
Write-Host '   2) powershell -ExecutionPolicy Bypass -File .\tools\02-scaffold.ps1' -ForegroundColor Green
Write-Host '   3) powershell -ExecutionPolicy Bypass -File .\tools\03-fetch-assets.ps1' -ForegroundColor Green
