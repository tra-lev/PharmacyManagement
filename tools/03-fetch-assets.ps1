<#
  Bước 2.1c — Tải font Be Vietnam Pro (giấy phép SIL OFL 1.1) và Bootstrap Icons (giấy phép MIT)
  về wwwroot\lib để ứng dụng chạy trong mạng LAN không cần Internet (SYS-11). Chạy một lần, cần Internet.

  Cách chạy (PowerShell, tại thư mục gốc dự án):
      powershell -ExecutionPolicy Bypass -File .\tools\03-fetch-assets.ps1

  Nếu chưa chạy, giao diện vẫn dùng được: chữ hiển thị bằng font hệ thống (Segoe UI), icon không hiện.
  Các file tải về được commit cùng mã nguồn để thành viên khác không phải tải lại.
#>
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path -Parent $PSScriptRoot
$lib = Join-Path $root 'PharmacyManagement\wwwroot\lib'
if (-not (Test-Path (Join-Path $root 'PharmacyManagement\wwwroot'))) { throw 'Chưa có wwwroot — hãy chạy tools\01-create-solution.ps1 trước.' }

function Get-Asset([string]$Url, [string]$Dest) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Dest) | Out-Null
    Write-Host ">> $Url" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing
    if ((Get-Item $Dest).Length -lt 100) { throw "File tải về quá nhỏ, có thể lỗi: $Url" }
}

# Bootstrap Icons 1.11.3
$bi = 'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3'
Get-Asset "$bi/font/bootstrap-icons.min.css" (Join-Path $lib 'bootstrap-icons\font\bootstrap-icons.min.css')
Get-Asset "$bi/font/fonts/bootstrap-icons.woff2" (Join-Path $lib 'bootstrap-icons\font\fonts\bootstrap-icons.woff2')
Get-Asset "$bi/font/fonts/bootstrap-icons.woff" (Join-Path $lib 'bootstrap-icons\font\fonts\bootstrap-icons.woff')
Get-Asset "$bi/LICENSE" (Join-Path $lib 'bootstrap-icons\LICENSE')

# Be Vietnam Pro (bản đóng gói của Fontsource) — 4 độ đậm × 3 bộ ký tự
$fs = 'https://cdn.jsdelivr.net/npm/@fontsource/be-vietnam-pro@5'
foreach ($subset in @('vietnamese', 'latin', 'latin-ext')) {
    foreach ($weight in @(400, 500, 600, 700)) {
        $name = "be-vietnam-pro-$subset-$weight-normal.woff2"
        Get-Asset "$fs/files/$name" (Join-Path $lib "be-vietnam-pro\$name")
    }
}
Get-Asset "$fs/LICENSE" (Join-Path $lib 'be-vietnam-pro\LICENSE')

Write-Host ''
Write-Host 'HOÀN TẤT: font và icon đã nằm trong PharmacyManagement\wwwroot\lib. Build lại để áp dụng.' -ForegroundColor Green
