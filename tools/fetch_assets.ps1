<#
.SYNOPSIS
    Downloads and installs CC0 3D models for Angband3D automatically with zero manual steps.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$assetRoot = Join-Path $PSScriptRoot "..\client\assets\models"
$dungeonDir = Join-Path $assetRoot "dungeon"
$charDir = Join-Path $assetRoot "characters"
$mixamoDir = Join-Path $assetRoot "mixamo"
$townDir = Join-Path $assetRoot "town"
$propsDir = Join-Path $assetRoot "props"

foreach ($dir in @($dungeonDir, $charDir, $mixamoDir, $townDir, $propsDir)) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

$packs = @(
    @{
        Name = "KayKit Dungeon Remastered 1.0"
        Url = "https://github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0/archive/refs/heads/main.zip"
        TargetDir = $dungeonDir
        Filter = "*.glb"
    },
    @{
        Name = "KayKit Character Pack Skeletons 1.0"
        Url = "https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0/archive/refs/heads/main.zip"
        TargetDir = $charDir
        Filter = "*.glb"
    },
    @{
        Name = "KayKit Character Pack Adventures 1.0"
        Url = "https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0/archive/refs/heads/main.zip"
        TargetDir = $charDir
        Filter = "*.glb"
    },
    @{
        Name = "KayKit Halloween Bits 1.0"
        Url = "https://github.com/KayKit-Game-Assets/KayKit-Halloween-Bits-1.0/archive/refs/heads/main.zip"
        TargetDir = $propsDir
        Filter = "*.*"
    },
    @{
        Name = "KayKit City Builder Bits 1.0"
        Url = "https://github.com/KayKit-Game-Assets/KayKit-City-Builder-Bits-1.0/archive/refs/heads/main.zip"
        TargetDir = $townDir
        Filter = "*.*"
    }
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$tempZip = Join-Path $env:TEMP "angband3d_pack_temp.zip"

foreach ($pack in $packs) {
    $existing = Get-ChildItem -Path $pack.TargetDir -Filter "*.glb" -Recurse -ErrorAction SilentlyContinue
    if (-not $Force -and $existing.Count -gt 3 -and ($pack.Name -like "*Dungeon*" -or $pack.Name -like "*Skeleton*" -or $pack.Name -like "*Adventure*")) {
        Write-Host "[$($pack.Name)] Already installed in $($pack.TargetDir), skipping." -ForegroundColor Green
        continue
    }

    Write-Host "Downloading CC0 pack: $($pack.Name)..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $pack.Url -OutFile $tempZip -UseBasicParsing

    Write-Host "Extracting assets to $($pack.TargetDir)..." -ForegroundColor Cyan
    $zip = [System.IO.Compression.ZipFile]::OpenRead($tempZip)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.Length -eq 0) { continue }
            $name = [System.IO.Path]::GetFileName($entry.FullName)
            if ([string]::IsNullOrWhiteSpace($name)) { continue }

            $ext = [System.IO.Path]::GetExtension($name).ToLowerInvariant()
            if ($ext -in @(".glb", ".gltf", ".bin", ".png")) {
                $destPath = Join-Path $pack.TargetDir $name
                # Avoid overwriting if same size
                if (-not (Test-Path $destPath) -or (Get-Item $destPath).Length -ne $entry.Length) {
                    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destPath, $true)
                }
            }
        }
    }
    finally {
        $zip.Dispose()
        if (Test-Path $tempZip) {
            Remove-Item $tempZip -Force
        }
    }
}

Write-Host "All CC0 3D assets installed successfully!" -ForegroundColor Green
