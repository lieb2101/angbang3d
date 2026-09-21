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

# 2. Build Godot C# Client in Release mode & Export Standalone Executable
Write-Host "[2/5] Compiling Godot C# client (Release)..." -ForegroundColor Yellow
$clientProj = Join-Path $repo 'client\angband3d.csproj'
$clientSln = Join-Path $repo 'client\angband3d.sln'
if (-not (Test-Path $clientSln)) {
    Push-Location (Join-Path $repo 'client')
    try {
        & dotnet new sln -n angband3d
        & dotnet sln angband3d.sln add angband3d.csproj
    } finally {
        Pop-Location
    }
}
& dotnet build $clientProj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build client project."
}

# Find Godot executable for export (prefer console binary to ensure synchronous completion)
function Find-GodotExe {
    $cmd = Get-Command godot -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $roots = @(
        "$env:LOCALAPPDATA\Microsoft\WinGet\Packages",
        "$env:ProgramFiles\Godot",
        "$env:LOCALAPPDATA\Programs\Godot"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem $root -Recurse -Filter 'Godot*.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like '*console*' } |
            Sort-Object { $_.Name -like '*mono*' } -Descending |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

$godotExe = Find-GodotExe

# 3. Prepare Staging Directory
$distRoot = Join-Path $repo $OutputDir
$pkgName = "Angband3D-Windows-x64"
$stageDir = Join-Path $distRoot $pkgName

# Clean dist directory of any temporary or obsolete artifacts
if (Test-Path $distRoot) {
    Get-ChildItem $distRoot | ForEach-Object {
        if ($_.Name -ne $pkgName -and $_.Name -ne "$pkgName.zip") {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
if (Test-Path $stageDir) {
    Remove-Item $stageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
Write-Host "[3/5] Staging distribution into: $stageDir" -ForegroundColor Yellow

# Export Standalone Executable if Godot is available
if ($godotExe) {
    Write-Host "Exporting standalone Angband3D.exe using Godot ($godotExe)..." -ForegroundColor Yellow
    $exportTarget = Join-Path $stageDir 'Angband3D.exe'
    & $godotExe --headless --path (Join-Path $repo 'client') --export-release "Windows Desktop" $exportTarget
    if ($LASTEXITCODE -eq 0 -and (Test-Path $exportTarget)) {
        Write-Host "Standalone executable exported successfully: $exportTarget" -ForegroundColor Green
    } else {
        Write-Host "Warning: Standalone export exited with code $LASTEXITCODE; falling back to source distribution staging." -ForegroundColor Yellow
    }
} else {
    Write-Host "Godot executable not found; skipping standalone .exe export step." -ForegroundColor Yellow
}

# Copy root docs and licenses
Copy-Item (Join-Path $repo 'LICENSE') $stageDir
Copy-Item (Join-Path $repo 'README.md') $stageDir

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
if exist "%~dp0Angband3D.exe" (
    "%~dp0Angband3D.exe" %*
) else (
    call play.cmd %*
)
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
    $canonicalZip = Join-Path $distRoot "angband3d-standalone.zip"
    Copy-Item $zipPath $canonicalZip -Force
    Write-Host "Package created: $zipPath" -ForegroundColor Green
    Write-Host "Canonical server download: $canonicalZip" -ForegroundColor Green
} else {
    Write-Host "[5/5] Skipping ZIP compression as requested." -ForegroundColor Gray
}

Write-Host ""
Write-Host "Build & Packaging Complete!" -ForegroundColor Cyan
Write-Host "Output: $stageDir" -ForegroundColor Cyan
if (-not $SkipZip) {
    Write-Host "Archive: $zipPath" -ForegroundColor Cyan
}
