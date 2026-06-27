# publish.ps1 - GalleryCloud 一键发布脚本
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "publish",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "=== GalleryCloud Publish ===" -ForegroundColor Cyan
Write-Host "Runtime: $Runtime | Config: $Configuration" -ForegroundColor Yellow

# 1. Build frontend
Write-Host "`n[1/3] Building frontend..." -ForegroundColor Green
Push-Location "$ScriptDir\src\GalleryCloud.Web"
npm ci
npm run build
if ($LASTEXITCODE -ne 0) { throw "Frontend build failed" }
Pop-Location

# 2. Publish backend (self-contained single file)
Write-Host "`n[2/3] Publishing backend..." -ForegroundColor Green
dotnet publish "$ScriptDir\src\GalleryCloud.Api\GalleryCloud.Api.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=true `
    -o "$ScriptDir\$OutputDir"
if ($LASTEXITCODE -ne 0) { throw "Backend publish failed" }

# 3. Copy frontend assets to wwwroot
Write-Host "`n[3/3] Copying frontend assets..." -ForegroundColor Green
$WwwRoot = "$ScriptDir\$OutputDir\wwwroot"
if (Test-Path $WwwRoot) { Remove-Item -Recurse -Force $WwwRoot }
Copy-Item -Recurse "$ScriptDir\src\GalleryCloud.Web\dist" $WwwRoot

# Create data directory
$DataDir = "$ScriptDir\$OutputDir\data"
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

# Copy appsettings.json if not exists
if (-not (Test-Path "$ScriptDir\$OutputDir\appsettings.json")) {
    Copy-Item "$ScriptDir\src\GalleryCloud.Api\appsettings.json" "$ScriptDir\$OutputDir\appsettings.json"
}

Write-Host "`n=== Done! Output: $ScriptDir\$OutputDir ===" -ForegroundColor Cyan
Write-Host "Run: .\GalleryCloud.Api.exe" -ForegroundColor White

# Optional: package as zip
# Compress-Archive -Path "$ScriptDir\$OutputDir\*" -DestinationPath "$ScriptDir\GalleryCloud-$Runtime.zip" -Force
