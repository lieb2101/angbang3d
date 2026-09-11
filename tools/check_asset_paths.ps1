$csFiles = Get-ChildItem -Path "C:\Dev\angband3d\client\scripts" -Filter "*.cs"
$projectRoot = "C:\Dev\angband3d\client"
$missing = @()
$found = @()

foreach ($cs in $csFiles) {
    $content = Get-Content -Path $cs.FullName -Raw
    $matches = [regex]::Matches($content, 'res://assets/models/[^",;)\s]+')
    foreach ($m in $matches) {
        $relPath = $m.Value.Substring("res://".Length).Replace("/", "\")
        $fullPath = Join-Path $projectRoot $relPath
        if (Test-Path $fullPath) {
            $found += "$($cs.Name) -> $($m.Value)"
        } else {
            $missing += "$($cs.Name) -> $($m.Value) [MISSING: $fullPath]"
        }
    }
}

Write-Host "=== FOUND ASSETS ($($found.Count)) ==="
$found | Select-Object -Unique | ForEach-Object { Write-Host "  OK: $_" -ForegroundColor Green }

Write-Host "`n=== MISSING ASSETS ($($missing.Count)) ==="
$missing | Select-Object -Unique | ForEach-Object { Write-Host "  MISSING: $_" -ForegroundColor Red }
