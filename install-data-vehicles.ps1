# Copies every data vehicle (vehicles/<Name>/ with *.vehicles.json + model .bin, no csproj) into the game's
# BepInEx/plugins/<Name>/ for dev testing. DLL plugins install themselves on build.
#   .\install-data-vehicles.ps1 [-GameDir <Moon Sweepers folder>]
param([string]$GameDir = $(if ($env:MOONSWEEPERS_DIR) { $env:MOONSWEEPERS_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers' }))
$ErrorActionPreference = 'Stop'
$plugins = Join-Path $GameDir 'BepInEx\plugins'
if (-not (Test-Path $plugins)) { throw "No BepInEx\plugins in $GameDir" }
foreach ($dir in Get-ChildItem (Join-Path $PSScriptRoot 'vehicles') -Directory) {
    if (Test-Path (Join-Path $dir.FullName "$($dir.Name).csproj")) { continue }
    $files = Get-ChildItem $dir.FullName -File | Where-Object { $_.Name -like '*.vehicles.json' -or $_.Extension -eq '.bin' }
    if (-not ($files | Where-Object Name -like '*.vehicles.json')) { continue }
    $dest = Join-Path $plugins $dir.Name
    New-Item -ItemType Directory -Force $dest | Out-Null
    $files | Copy-Item -Destination $dest
    Write-Host "  $($dir.Name): $($files.Count) files"
}
