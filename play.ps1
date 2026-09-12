<#
.SYNOPSIS
    Play angband3d.
.DESCRIPTION
    Launches the Godot client, locating Godot automatically. Run from anywhere.
.PARAMETER Character
    Which character / save slot to use. Each name is a separate character;
    nothing is ever overwritten.
.PARAMETER List
    List existing save slots and exit.
.PARAMETER Random
    Skip the title screen and roll a random character.
.PARAMETER Manual
    Skip the title screen and go straight to the creation screens.
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
    [string]$Character,
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

function Get-SaveDescription([string]$path) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($path)
        if ($bytes.Length -ge 36) {
            $magic = [System.Text.Encoding]::ASCII.GetString($bytes, 0, 8)
            if ($magic -eq "SaveVNLA") {
                $block = [System.Text.Encoding]::ASCII.GetString($bytes, 8, 16).TrimEnd("`0")
                if ($block -eq "description") {
                    $sz = [System.BitConverter]::ToUInt32($bytes, 28)
                    if ($sz -gt 0 -and $sz -le 512 -and $bytes.Length -ge (36 + $sz)) {
                        $nullIdx = [System.Array]::IndexOf($bytes, [byte]0, 36, $sz)
                        $len = if ($nullIdx -ge 36) { $nullIdx - 36 } else { $sz }
                        return [System.Text.Encoding]::UTF8.GetString($bytes, 36, $len)
                    }
                }
            }
        }
    } catch {}
    return $null
}

if ($List) {
    Write-Host "Save slots in $saveDir" -ForegroundColor Cyan
    if (Test-Path $saveDir) {
        $saves = Get-ChildItem $saveDir -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending
        if ($saves) {
            $saves | ForEach-Object {
                $desc = Get-SaveDescription $_.FullName
                if ($desc) {
                    "  {0,-16} {1,-38} ({2:yyyy-MM-dd HH:mm})" -f $_.Name, $desc, $_.LastWriteTime
                } else {
                    "  {0,-16} ({1:yyyy-MM-dd HH:mm})" -f $_.Name, $_.LastWriteTime
                }
            }
        } else { Write-Host "  (none yet)" }
    } else { Write-Host "  (none yet)" }
    Write-Host ""
    Write-Host "Play one with:  .\play.ps1 -Character <name>"
    exit 0
}

if ($Classic) {
    Write-Host "Starting Angband in text mode. Quit with Ctrl-X." -ForegroundColor Cyan
    Push-Location (Split-Path $angband)
    $gcuArgs = @('-mgcu')
    if ($Character) { $gcuArgs += "-u$Character" }
    try { & $angband $gcuArgs } finally { Pop-Location }
    exit $LASTEXITCODE
}

$standaloneExe = Join-Path $repo 'Angband3D.exe'
if ((Test-Path $standaloneExe) -and (-not $Editor)) {
    if ($Character) {
        $existing = Join-Path $saveDir $Character
        if (Test-Path $existing) {
            Write-Host "Character '$Character' found." -ForegroundColor Cyan
        } else {
            Write-Host "No character called '$Character' yet." -ForegroundColor Cyan
        }
    }
    Write-Host "Launching standalone Angband3D..." -ForegroundColor Cyan
    $exeArgs = @()
    if ($Character) { $exeArgs += "--save=$Character" }
    if ($Random) { $exeArgs += '--autobirth' }
    if ($Manual) { $exeArgs += '--manual' }
    if ($exeArgs.Count -gt 0) {
        $exeArgs = @('--') + $exeArgs
    }
    & $standaloneExe $exeArgs
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

if ($Character) {
    $existing = Join-Path $saveDir $Character
    if (Test-Path $existing) {
        Write-Host "Character '$Character' found." -ForegroundColor Cyan
    } else {
        Write-Host "No character called '$Character' yet." -ForegroundColor Cyan
    }
}
Write-Host "Choose continue, load, or new character on the title screen." -ForegroundColor DarkGray

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
$userArgs = @()
if ($Character) { $userArgs += "--save=$Character" }
if ($Random) { $userArgs += '--autobirth' }
if ($Manual) { $userArgs += '--manual' }
if ($userArgs.Count -gt 0) {
    $clientArgs += @('--') + $userArgs
}

& $godot $clientArgs