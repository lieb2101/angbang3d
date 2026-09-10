<#
.SYNOPSIS
    Package Angband3D for standalone distribution.
.DESCRIPTION
    Builds the C engine, compiles the C# Godot client in Release mode, stages
    all assets, gamedata, and runtime binaries into a clean distribution folder,
    and compresses it into a ready-to-distribute ZIP archive.
.PARAMETER OutputDir
    Where to output the packaged distribution. Defaults to 'dist'.
.PARAMETER SkipZip
    If specified, stages the folder but skips creating the ZIP archive.
.EXAMPLE
    .\tools\package.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDir = 'dist',
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'
$repo = (Get-Item $PSScriptRoot).Parent.FullName

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Angband3D Standalone Packaging Tool   " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Check/Build Angband C Engine
$engineExe = Join-Path $repo 'engine\build\game\angband.exe'
if (-not (Test-Path $engineExe)) {
    Write-Host "[1/5] Building Angband C engine..." -ForegroundColor Yellow
    & (Join-Path $repo 'tools\build.ps1')
} else {
    Write-Host "[1/5] Angband C engine already built: $engineExe" -ForegroundColor Green
}

# 2. Build Godot C# Client in Release mode
Write-Host "[2/5] Compiling Godot C# client (Release)..." -ForegroundColor Yellow
$clientProj = Join-Path $repo 'client\angband3d.csproj'
& dotnet build $clientProj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build client project."
}

# 3. Prepare Staging Directory
$distRoot = Join-Path $repo $OutputDir
$pkgName = "Angband3D-Windows-x64"
$stageDir = Join-Path $distRoot $pkgName

if (Test-Path $stageDir) {
    Remove-Item $stageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
Write-Host "[3/5] Staging distribution into: $stageDir" -ForegroundColor Yellow

# Copy root docs and licenses
Copy-Item (Join-Path $repo 'LICENSE') $stageDir
Copy-Item (Join-Path $repo 'README.md') $stageDir

# Copy Client files
$destClient = Join-Path $stageDir 'client'
New-Item -ItemType Directory -Path $destClient -Force | Out-Null

Copy-Item (Join-Path $repo 'client\Main.tscn') $destClient
Copy-Item (Join-Path $repo 'client\project.godot') $destClient
Copy-Item (Join-Path $repo 'client\icon.svg') $destClient
if (Test-Path (Join-Path $repo 'client\icon.svg.import')) {
    Copy-Item (Join-Path $repo 'client\icon.svg.import') $destClient
}
if (Test-Path (Join-Path $repo 'client\export_presets.cfg')) {
    Copy-Item (Join-Path $repo 'client\export_presets.cfg') $destClient
}

# Copy Assets
Copy-Item (Join-Path $repo 'client\assets') $destClient -Recurse

# Copy Scripts & .godot cache
Copy-Item (Join-Path $repo 'client\scripts') $destClient -Recurse
if (Test-Path (Join-Path $repo 'client\.godot')) {
    Copy-Item (Join-Path $repo 'client\.godot') $destClient -Recurse
}

# Copy Engine binaries and gamedata
$destEngine = Join-Path $stageDir 'engine\build\game'
New-Item -ItemType Directory -Path $destEngine -Force | Out-Null
Copy-Item $engineExe $destEngine

$engineLib = Join-Path $repo 'engine\build\game\lib'
if (Test-Path $engineLib) {
    Copy-Item $engineLib $destEngine -Recurse
    # Clean any local save files from distribution package
    $pkgSaveDir = Join-Path $destEngine 'lib\save'
    if (Test-Path $pkgSaveDir) {
        Get-ChildItem $pkgSaveDir -File | Remove-Item -Force -ErrorAction SilentlyContinue
    } else {
        New-Item -ItemType Directory -Path $pkgSaveDir -Force | Out-Null
    }
}

# Copy Launchers
Copy-Item (Join-Path $repo 'play.cmd') $stageDir
Copy-Item (Join-Path $repo 'play.ps1') $stageDir

# Create root quick-launcher Play-Angband3D.cmd
$quickLauncherContent = @"
@echo off
setlocal
cd /d "%~dp0"
call play.cmd %*
"@
Set-Content -Path (Join-Path $stageDir 'Play-Angband3D.cmd') -Value $quickLauncherContent

Write-Host "[4/5] Staged distribution files successfully." -ForegroundColor Green

# 4. Compress to ZIP Archive
if (-not $SkipZip) {
    $zipPath = Join-Path $distRoot "$pkgName.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    Write-Host "[5/5] Compressing to $zipPath..." -ForegroundColor Yellow
    Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Package created: $zipPath" -ForegroundColor Green
} else {
    Write-Host "[5/5] Skipping ZIP compression as requested." -ForegroundColor Gray
}

Write-Host ""
Write-Host "Build & Packaging Complete!" -ForegroundColor Cyan
Write-Host "Output: $stageDir" -ForegroundColor Cyan
if (-not $SkipZip) {
    Write-Host "Archive: $zipPath" -ForegroundColor Cyan
}
