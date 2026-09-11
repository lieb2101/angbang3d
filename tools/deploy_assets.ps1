$resDir = "C:\Dev\angband3d\resources"
$clientModels = "C:\Dev\angband3d\client\assets\models"

Write-Host "=== Angband3D Comprehensive Asset Deployment ===" -ForegroundColor Cyan

# 1. Bestiary (Imp, Puglin)
Write-Host "1. Deploying Bestiary monsters..."
$bestiaryDest = Join-Path $clientModels "monsters\bestiary"
New-Item -ItemType Directory -Path $bestiaryDest -Force | Out-Null
Copy-Item -LiteralPath "$resDir\Bestiary - Dungeon Monsters Kit[Standard]\Exports\GLB (Godot-Unreal)\Imp.glb" -Destination $bestiaryDest -Force
Copy-Item -LiteralPath "$resDir\Bestiary - Dungeon Monsters Kit[Standard]\Exports\GLB (Godot-Unreal)\Puglin.glb" -Destination $bestiaryDest -Force
Get-ChildItem -LiteralPath "$resDir\Bestiary - Dungeon Monsters Kit[Standard]\Textures" -Filter "*.png" | Copy-Item -Destination $bestiaryDest -Force

# 2. Easy Animated Enemies (Rat, Spider, Snake, Snake_angry, Frog, Wasp)
Write-Host "2. Deploying Easy Animated Enemies..."
$enemiesDest = Join-Path $clientModels "monsters\enemies"
New-Item -ItemType Directory -Path $enemiesDest -Force | Out-Null
Get-ChildItem -LiteralPath "$resDir\Easy Animated Enemy Pack - Jan 2019\FBX" -Filter "*.fbx" | Copy-Item -Destination $enemiesDest -Force
Get-ChildItem -LiteralPath "$resDir\Easy Animated Enemy Pack - Jan 2019\OBJ" -Filter "*.*" | Copy-Item -Destination $enemiesDest -Force

# 3. Medieval Weapons
Write-Host "3. Deploying Medieval Weapons Pack..."
$weaponsDest = Join-Path $clientModels "weapons"
New-Item -ItemType Directory -Path $weaponsDest -Force | Out-Null
Get-ChildItem -LiteralPath "$resDir\Medieval Weapons Pack by @Quaternius\FBX" -Filter "*.fbx" | Copy-Item -Destination $weaponsDest -Force
Get-ChildItem -LiteralPath "$resDir\Medieval Weapons Pack by @Quaternius\OBJ" -Filter "*.*" | Copy-Item -Destination $weaponsDest -Force

# 4. Ultimate RPG Items Pack
Write-Host "4. Deploying Ultimate RPG Items Pack..."
$itemsDest = Join-Path $clientModels "items"
New-Item -ItemType Directory -Path $itemsDest -Force | Out-Null
Get-ChildItem -LiteralPath "$resDir\Ultimate RPG Items Pack - Aug 2019\FBX" -Filter "*.fbx" | Copy-Item -Destination $itemsDest -Force
Get-ChildItem -LiteralPath "$resDir\Ultimate RPG Items Pack - Aug 2019\OBJ" -Filter "*.*" | Copy-Item -Destination $itemsDest -Force

# 5. Characters (Modular Outfits, Men, Women, Base Characters)
Write-Host "5. Deploying Modular Characters, Outfits & Base Rigs..."
$charsDest = Join-Path $clientModels "characters"
New-Item -ItemType Directory -Path $charsDest -Force | Out-Null
Get-ChildItem -LiteralPath "$resDir\Modular Character Outfits - Fantasy[Standard]" -Recurse -File | Where-Object { $_.Extension -in '.gltf', '.bin', '.png', '.fbx' } | Copy-Item -Destination $charsDest -Force
Get-ChildItem -LiteralPath "$resDir\Ultimate Modular Men- Feb 2022\Individual Characters\glTF" -Filter "*.*" | Copy-Item -Destination $charsDest -Force
Get-ChildItem -LiteralPath "$resDir\Ultimate Modular Women - April 2022\Individual Characters\glTF" -Filter "*.*" | Copy-Item -Destination $charsDest -Force
Get-ChildItem -LiteralPath "$resDir\Universal Base Characters[Standard]" -Recurse -File | Where-Object { $_.Extension -in '.gltf', '.bin', '.png', '.fbx' } | Copy-Item -Destination $charsDest -Force

# 6. Fantasy Props MegaKit
Write-Host "6. Extracting Fantasy Props (glTF & FBX & Textures)..."
$propsDest = Join-Path $clientModels "props"
New-Item -ItemType Directory -Path $propsDest -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead("$resDir\Fantasy Props MegaKit[Standard].zip")
try {
    foreach ($entry in $zip.Entries) {
        if ($entry.Length -gt 0 -and ($entry.FullName.EndsWith(".gltf") -or $entry.FullName.EndsWith(".bin") -or $entry.FullName.EndsWith(".fbx") -or $entry.FullName.EndsWith(".png") -or $entry.FullName.EndsWith(".jpg"))) {
            $fileName = [System.IO.Path]::GetFileName($entry.FullName)
            $target = Join-Path $propsDest $fileName
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
} finally {
    $zip.Dispose()
}

# 7. Medieval Village MegaKit
Write-Host "7. Deploying Medieval Village MegaKit (glTF, FBX, Textures)..."
$townDest = Join-Path $clientModels "town"
New-Item -ItemType Directory -Path $townDest -Force | Out-Null
Get-ChildItem -LiteralPath "$resDir\Medieval Village MegaKit[Standard]" -Recurse -File | Where-Object { $_.Extension -in '.gltf', '.bin', '.fbx', '.png', '.jpg', '.mtl', '.obj' } | Copy-Item -Destination $townDest -Force

# 8. Universal Animation Library
Write-Host "8. Deploying Universal Animation Library..."
$animDest = Join-Path $clientModels "animations"
New-Item -ItemType Directory -Path $animDest -Force | Out-Null
if (Test-Path -LiteralPath "$resDir\Universal Animation Library[Standard]\Unreal-Godot") {
    Get-ChildItem -LiteralPath "$resDir\Universal Animation Library[Standard]\Unreal-Godot" -Filter "*.glb" | Copy-Item -Destination $animDest -Force
}

Write-Host "=== All assets successfully deployed and organized! ===" -ForegroundColor Green
