# Copies data vehicles into the game for dev testing, laid out the way package.ps1 ships them:
#   data packs (mods/<Pack>/pack.json) -> BepInEx/plugins/<Pack>/ (stats files) and <Pack>/<Vehicle>/ (json + .bin)
#   a lone data vehicle (vehicles/<Name>/ with *.vehicles.json, no csproj, in no pack) -> BepInEx/plugins/<Name>/
# DLL plugins install themselves on build.
#   .\install-data-vehicles.ps1 [-GameDir <Moon Sweepers folder>]
param([string]$GameDir = $(if ($env:MOONSWEEPERS_DIR) { $env:MOONSWEEPERS_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers' }))
$ErrorActionPreference = 'Stop'
$plugins = Join-Path $GameDir 'BepInEx\plugins'
if (-not (Test-Path $plugins)) { throw "No BepInEx\plugins in $GameDir" }

function Copy-Vehicle($src, $dest) {
    $files = @(Get-ChildItem $src -File | Where-Object { $_.Name -like '*.vehicles.json' -or $_.Extension -eq '.bin' })
    if (-not ($files | Where-Object Name -like '*.vehicles.json')) { return $false }
    New-Item -ItemType Directory -Force $dest | Out-Null
    $files | Copy-Item -Destination $dest
    $true
}

$inPack = @()
foreach ($f in Get-ChildItem (Join-Path $PSScriptRoot 'mods') -Filter pack.json -Recurse -Depth 1) {
    $pack = Get-Content $f.FullName -Raw | ConvertFrom-Json
    $dest = Join-Path $plugins $f.Directory.Name
    New-Item -ItemType Directory -Force $dest | Out-Null
    foreach ($st in $pack.stats) { Copy-Item (Join-Path $f.Directory.FullName $st) $dest }
    foreach ($v in $pack.vehicles) {
        if (-not (Copy-Vehicle (Join-Path $PSScriptRoot "vehicles\$v") (Join-Path $dest $v))) { throw "vehicles\$v has no *.vehicles.json" }
        # Older dev installs put each vehicle in its own plugin folder; two copies would register twice.
        $old = Join-Path $plugins $v
        if (Test-Path $old) { Remove-Item $old -Recurse -Force }
        $inPack += $v
    }
    Write-Host "  $($f.Directory.Name): $($pack.vehicles.Count) vehicles"
}
foreach ($dir in Get-ChildItem (Join-Path $PSScriptRoot 'vehicles') -Directory) {
    if ($inPack -contains $dir.Name -or (Test-Path (Join-Path $dir.FullName "$($dir.Name).csproj"))) { continue }
    if (Copy-Vehicle $dir.FullName (Join-Path $plugins $dir.Name)) { Write-Host "  $($dir.Name)" }
}
# The balance table used to ship inside GarageLib's folder.
Remove-Item (Join-Path $plugins 'GarageLib\lines.vehicles.json') -ErrorAction SilentlyContinue
