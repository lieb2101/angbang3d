<#
.SYNOPSIS
    One-time toolchain setup for angband3d on Windows.
.DESCRIPTION
    Installs MSYS2 and the MinGW-w64 packages needed to build the Angband fork.
    Safe to re-run; every step is idempotent.
#>
[CmdletBinding()]
param(
    [string]$Msys2Root = 'C:\msys64'
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

Write-Step 'Checking for MSYS2'
if (Test-Path (Join-Path $Msys2Root 'usr\bin\bash.exe')) {
    Write-Host "    already present at $Msys2Root"
} else {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget not found. Install MSYS2 manually from https://www.msys2.org/ then re-run."
    }
    Write-Step 'Installing MSYS2 via winget'
    winget install --id MSYS2.MSYS2 -e --accept-package-agreements --accept-source-agreements --disable-interactivity
    if (-not (Test-Path (Join-Path $Msys2Root 'usr\bin\bash.exe'))) {
        throw "MSYS2 did not install to $Msys2Root. Re-run with -Msys2Root <path>."
    }
}

$bash = Join-Path $Msys2Root 'usr\bin\bash.exe'
$packages = @(
    'make'
    'mingw-w64-x86_64-gcc'
    'mingw-w64-x86_64-cmake'
    'mingw-w64-x86_64-ninja'
    'mingw-w64-x86_64-ncurses'
)

Write-Step 'Installing MinGW-w64 build packages'
$env:MSYSTEM = 'MINGW64'
$env:CHERE_INVOKING = '1'
& $bash -lc "pacman -S --needed --noconfirm $($packages -join ' ')"
if ($LASTEXITCODE -ne 0) { throw 'pacman failed' }

Write-Step 'Verifying toolchain'
& $bash -lc 'gcc --version | head -n1; cmake --version | head -n1; ninja --version'

Write-Host ''
Write-Host 'Toolchain ready. Next: ./tools/build.ps1' -ForegroundColor Green
