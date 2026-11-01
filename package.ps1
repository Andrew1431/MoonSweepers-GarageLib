# Builds release (non-dev) DLLs and zips them into dist/.
#   .\package.ps1                      every package + the modpack
#   .\package.ps1 -Only MsChoppa       one package
#   .\package.ps1 -Release             also create GitHub releases for versions that don't have one yet
# Each package zip unpacks into the game folder: BepInEx/plugins/<Name>/<Name>.dll.
# Tags: <name>-v<version> per package (garagelib-v0.1.0), modpack-v<version> for the bundle (version in modpack.json).
param([string[]]$Only, [switch]$Release)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Packages {
    Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'vehicles'), (Join-Path $root 'mods') -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $proj = Join-Path $_.FullName "$($_.Name).csproj"
        $plugin = Join-Path $_.FullName 'Plugin.cs'
        if (-not (Test-Path $proj) -or -not (Test-Path $plugin)) { return }
        $m = Select-String -Path $plugin -Pattern 'const string Version = "([^"]+)"'
        if (-not $m) { throw "$plugin has no 'const string Version'" }
        [pscustomobject]@{ Name = $_.Name; Dir = $_.FullName; Project = $proj; Version = $m.Matches[0].Groups[1].Value }
    }
}

function Build-Package($p) {
    Write-Host "Building $($p.Name) $($p.Version)"
    dotnet build $p.Project -c Release -p:DevBuild=false -p:InstallToGame=false -nologo -v q | Out-Host
    if ($LASTEXITCODE) { throw "build failed: $($p.Name)" }
    $dll = Join-Path $p.Dir "bin\Release\$($p.Name).dll"
    # A dev-only type in a release DLL means DEVBUILD leaked.
    $bytes = [IO.File]::ReadAllText($dll)
    if ($bytes.Contains('DevTools') -or $bytes.Contains('DevSelfTest')) { throw "$($p.Name): dev-only code found in the release build" }
    return $dll
}

function Stage($p, $dll, $into) {
    $dir = Join-Path $into "BepInEx\plugins\$($p.Name)"
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item $dll $dir
    $readme = Join-Path $p.Dir 'README.md'
    if (Test-Path $readme) { Copy-Item $readme $dir }
}

function Zip($from, $zip) {
    if (Test-Path $zip) { Remove-Item $zip }
    # Built by hand: Windows PowerShell's CreateFromDirectory writes backslash entry names, which other unzippers mangle.
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        Get-ChildItem $from -Recurse -File | ForEach-Object {
            $entry = $_.FullName.Substring($from.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry) | Out-Null
        }
    } finally { $archive.Dispose() }
    Write-Host "  -> $zip"
}

function Publish($tag, $title, $notes, $zip) {
    # Not `gh release view`: its "not found" goes to stderr, which Windows PowerShell turns into a terminating error.
    $existing = gh release list --limit 1000 --json tagName --jq '.[].tagName'
    if ($existing -contains $tag) { Write-Host "  $tag already released, skipped"; return }
    gh release create $tag $zip --title $title --notes $notes | Out-Host
    if ($LASTEXITCODE) { throw "gh release create $tag failed" }
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
$packages = @(Get-Packages)
if ($Only) { $packages = @($packages | Where-Object { $Only -contains $_.Name }) }
$built = @{}

foreach ($p in $packages) {
    $dll = Build-Package $p
    $built[$p.Name] = @{ P = $p; Dll = $dll }
    $s = Join-Path $stage $p.Name
    Stage $p $dll $s
    $zip = Join-Path $dist "$($p.Name)-$($p.Version).zip"
    Zip $s $zip
    if ($Release) { Publish "$($p.Name.ToLower())-v$($p.Version)" "$($p.Name) $($p.Version)" "Unzip into the Moon Sweepers game folder (needs BepInEx + ModEngine$(if ($p.Name -ne 'GarageLib') { ' and GarageLib' }))." $zip }
}

if (-not $Only) {
    $pack = Get-Content (Join-Path $root 'modpack.json') -Raw | ConvertFrom-Json
    $s = Join-Path $stage 'modpack'
    $lines = @()
    foreach ($n in $pack.packages) {
        if (-not $built.ContainsKey($n)) { throw "modpack.json lists '$n' but no such package was built" }
        Stage $built[$n].P $built[$n].Dll $s
        $lines += "- $n $($built[$n].P.Version)"
    }
    $zip = Join-Path $dist "$($pack.name)-$($pack.version).zip"
    Zip $s $zip
    if ($Release) { Publish "modpack-v$($pack.version)" "Modpack $($pack.version)" ("Unzip into the Moon Sweepers game folder (needs BepInEx + ModEngine).`n`n" + ($lines -join "`n")) $zip }
}
Remove-Item $stage -Recurse -Force
