<#
.SYNOPSIS
    Play angband3d.
.DESCRIPTION
    Launches the Godot client, locating Godot automatically. Run from anywhere.
.PARAMETER Character
    Which save slot to use. Defaults to "angband3d". Each name is a separate
    character; nothing is ever overwritten.
.PARAMETER List
    List existing save slots and exit.
.PARAMETER Random
    Roll a random character without asking. Only applies to a new character.
.PARAMETER Manual
    Go straight to the creation screens without asking.
.PARAMETER Classic
    Skip the client and play plain text Angband in this terminal instead.
.PARAMETER Editor
    Open the client in the Godot editor rather than running it.
.EXAMPLE
    .\play.ps1
.EXAMPLE
    .\play.ps1 -Character thorin
.EXAMPLE
    .\play.ps1 -Character quick -Random
.EXAMPLE
    .\play.ps1 -List
#>
[CmdletBinding()]
param(
    [string]$Character = 'angband3d',
    [switch]$Random,
    [switch]$Manual,
    [switch]$List,
    [switch]$Classic,
    [switch]$Editor
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot

$angband = Join-Path $repo 'engine\build\game\angband.exe'
if (-not (Test-Path $angband)) {
    Write-Host "The engine has not been built yet." -ForegroundColor Yellow
    Write-Host "Run:  .\tools\bootstrap.ps1   (once)" -ForegroundColor Yellow
    Write-Host "Then: .\build.cmd" -ForegroundColor Yellow
    exit 1
}

$saveDir = Join-Path $repo 'engine\build\game\lib\save'

if ($List) {
    Write-Host "Save slots in $saveDir" -ForegroundColor Cyan
    if (Test-Path $saveDir) {
        $saves = Get-ChildItem $saveDir -File -ErrorAction SilentlyContinue
        if ($saves) {
            $saves | ForEach-Object { "  {0,-24} {1}" -f $_.Name, $_.LastWriteTime }
        } else { Write-Host "  (none yet)" }
    } else { Write-Host "  (none yet)" }
    Write-Host ""
    Write-Host "Play one with:  .\play.ps1 -Character <name>"
    exit 0
}

if ($Classic) {
    Write-Host "Starting Angband in text mode. Quit with Ctrl-X." -ForegroundColor Cyan
    Push-Location (Split-Path $angband)
    try { & $angband -mgcu "-u$Character" } finally { Pop-Location }
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

$existing = Join-Path $saveDir $Character
$rollRandom = $false

if (Test-Path $existing) {
    Write-Host "Continuing character '$Character'." -ForegroundColor Cyan
} else {
    Write-Host "New character in slot '$Character'." -ForegroundColor Cyan
    if ($Random) {
        $rollRandom = $true
    } elseif (-not $Manual) {
        Write-Host ""
        Write-Host "  [M] Create it yourself - choose race, class and stats" -ForegroundColor Gray
        Write-Host "  [R] Roll a random character and start straight away" -ForegroundColor Gray
        Write-Host ""
        $answer = Read-Host "Which? [M/r]"
        $rollRandom = $answer -match '^[Rr]'
    }
    Write-Host ($(if ($rollRandom) { "Rolling a random character." }
                  else { "Character creation will open in the classic view." })) -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Controls" -ForegroundColor Cyan
Write-Host "  arrows          turn / walk         >   descend stairs"
Write-Host "  M               classic map         <   ascend stairs"
Write-Host "  Tab             terminal view       i   inventory"
Write-Host "  Ctrl-S  save    Ctrl-X  save+quit   ?   help"
Write-Host "  Ctrl-W          wizard (god) mode    Ctrl-A  debug commands"
Write-Host ""
Write-Host "Menus, targeting and character creation use the classic terminal view." -ForegroundColor DarkGray
Write-Host "Other characters:  .\play.ps1 -Character <name>   (.\play.ps1 -List)" -ForegroundColor DarkGray
Write-Host ""

$client = Join-Path $repo 'client'
$clientArgs = @('--path', $client)
if ($Editor) { $clientArgs += '--editor' }
$clientArgs += @('--', "--save=$Character")
if ($rollRandom) { $clientArgs += '--autobirth' }

& $godot $clientArgs