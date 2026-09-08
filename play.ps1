<#
.SYNOPSIS
    Play angband3d.
.DESCRIPTION
    Launches the Godot client, locating Godot automatically. Run from anywhere.
.PARAMETER Classic
    Skip the client and play plain text Angband in this terminal instead.
.PARAMETER Editor
    Open the client in the Godot editor rather than running it.
.EXAMPLE
    .\play.ps1
.EXAMPLE
    .\play.ps1 -Classic
#>
[CmdletBinding()]
param(
    [switch]$Classic,
    [switch]$Editor
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot

$angband = Join-Path $repo 'engine\build\game\angband.exe'
if (-not (Test-Path $angband)) {
    Write-Host "The engine has not been built yet." -ForegroundColor Yellow
    Write-Host "Run:  .\tools\bootstrap.ps1   (once)" -ForegroundColor Yellow
    Write-Host "Then: .\tools\build.ps1" -ForegroundColor Yellow
    exit 1
}

if ($Classic) {
    Write-Host "Starting Angband in text mode. Quit with Ctrl-X." -ForegroundColor Cyan
    Push-Location (Split-Path $angband)
    try { & $angband -mgcu } finally { Pop-Location }
    exit $LASTEXITCODE
}

function Find-Godot {
    $cmd = Get-Command godot -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # winget cannot create its shim without admin rights, so look in the
    # package directory too.
    $roots = @(
        "$env:LOCALAPPDATA\Microsoft\WinGet\Packages",
        "$env:ProgramFiles\Godot",
        "$env:LOCALAPPDATA\Programs\Godot"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem $root -Recurse -Filter 'Godot*.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike '*console*' } |
            Sort-Object { $_.Name -like '*mono*' } -Descending |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

$godot = Find-Godot
if (-not $godot) {
    Write-Host "Godot 4 (.NET build) was not found." -ForegroundColor Yellow
    Write-Host "Install it with:  winget install --id GodotEngine.GodotEngine.Mono -e" -ForegroundColor Yellow
    Write-Host "Or play in text mode instead:  .\play.ps1 -Classic" -ForegroundColor Yellow
    exit 1
}

$client = Join-Path $repo 'client'
Write-Host "Godot:  $godot" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Controls" -ForegroundColor Cyan
Write-Host "  arrows / hjkl   move            >   descend stairs"
Write-Host "  Tab             terminal view   i   inventory"
Write-Host "  Ctrl-S          save            Ctrl-W  wizard (god) mode"
Write-Host "  Ctrl-X          save and quit   ?   help"
Write-Host ""
Write-Host "Prompts and menus appear in the terminal view (Tab)." -ForegroundColor DarkGray
Write-Host ""

if ($Editor) {
    & $godot --path $client --editor
} else {
    & $godot --path $client
}
