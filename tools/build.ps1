<#
.SYNOPSIS
    Build the Angband fork with the bridge front end.
.PARAMETER Clean
    Delete the build directory first.
.PARAMETER Test
    Run Angband's unit tests and the bridge smoke tests afterwards.
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Test,
    [string]$Msys2Root = 'C:\msys64'
)

$ErrorActionPreference = 'Stop'

$bash = Join-Path $Msys2Root 'usr\bin\bash.exe'
if (-not (Test-Path $bash)) { throw "MSYS2 not found at $Msys2Root. Run ./tools/bootstrap.ps1 first." }

$repo = Split-Path -Parent $PSScriptRoot
$env:MSYSTEM = 'MINGW64'
$env:CHERE_INVOKING = '1'

# C:\Dev\angband3d -> /c/Dev/angband3d
$msysRepo = '/' + ($repo -replace ':', '' -replace '\\', '/')

function Invoke-Msys([string]$cmd) {
    & $bash -lc $cmd
    if ($LASTEXITCODE -ne 0) { throw "command failed: $cmd" }
}

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

if ($Clean) {
    Write-Step 'Cleaning'
    Remove-Item (Join-Path $repo 'engine\build') -Recurse -Force -ErrorAction SilentlyContinue
}

# GCU must stay enabled: with no graphical front end selected, CMake falls back
# to the Windows front end, which needs libpng and disables the bridge.
Write-Step 'Configuring'
Invoke-Msys "cd $msysRepo/engine && cmake -G Ninja -DSUPPORT_BRIDGE_FRONTEND=ON -DSUPPORT_GCU_FRONTEND=ON -DSUPPORT_TEST_FRONTEND=ON -DSUPPORT_STATIC_LINKING=ON -B build"

Write-Step 'Building'
# Ninja can report a spurious .ninja_lock permission error after a successful
# link on Windows, so verify the artefact rather than trusting the exit code.
& $bash -lc "cd $msysRepo/engine && cmake --build build"

$exe = Join-Path $repo 'engine\build\game\angband.exe'
if (-not (Test-Path $exe)) { throw 'build failed: angband.exe was not produced' }
Write-Host ("    {0} ({1:N0} bytes)" -f $exe, (Get-Item $exe).Length) -ForegroundColor Green

if ($Test) {
    Write-Step 'Angband unit tests'
    & $bash -lc "cd $msysRepo/engine && cmake --build build -t allunittests" 2>&1 |
        Select-String -Pattern 'Total:' | ForEach-Object { Write-Host "    $_" }

    Write-Step 'Bridge smoke tests'
    Push-Location $repo
    try { python tools/smoke_test.py; if ($LASTEXITCODE -ne 0) { throw 'smoke tests failed' } }
    finally { Pop-Location }
}

Write-Host ''
Write-Host 'Build OK.' -ForegroundColor Green
