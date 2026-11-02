# Installs or updates the MoonSweepers-GarageLib modpack in a Moon Sweepers game folder.
#   Double-click "Install or update.bat" (or the "Moon Sweepers (modded)" desktop shortcut it makes, which updates and
#   then starts the game). Flags:
#     -Launch            start the game when done
#     -GameDir <path>    game folder, if it isn't found through Steam
#     -Zip <path>        install this modpack zip instead of downloading the latest release (testing)
#     -Uninstall         remove the modpack's plugins, then offer to remove BepInEx too ("Uninstall mods.bat")
#     -Vanilla           with -Uninstall: also remove BepInEx and ModEngine without asking (saves always stay)
# What it does: BepInEx 6 IL2CPP be.791 (downloaded from builds.bepinex.dev if missing), the MoonSweepers.ModEngine
# patcher (only on Nexus, so it opens the page and picks the zip up from Downloads), then the newest modpack release
# from GitHub. It remembers which plugin folders it installed and replaces only those on update.
param([switch]$Launch, [string]$GameDir, [string]$Zip, [switch]$Uninstall, [switch]$Vanilla)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest's progress bar makes downloads 10x slower in PS 5.1
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Repo = 'Andrew1431/MoonSweepers-GarageLib'
$SteamAppId = '5335510'
$BepInExVersion = '6.0.0-be.791'
$BepInExUrl = 'https://builds.bepinex.dev/projects/bepinex_be/791/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791%2Bb782da4.zip'
$ModEngineUrl = 'https://www.nexusmods.com/moonsweepers/mods/9'

function Say($m, $c = 'Gray') { Write-Host $m -ForegroundColor $c }

function Find-Game {
    $roots = @()
    foreach ($k in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam') {
        $p = (Get-ItemProperty $k -ErrorAction SilentlyContinue)
        if ($p.SteamPath) { $roots += $p.SteamPath }
        if ($p.InstallPath) { $roots += $p.InstallPath }
    }
    $libs = @()
    foreach ($r in $roots | Select-Object -Unique) {
        $libs += $r
        $vdf = Join-Path $r 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) { $libs += [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' } }
    }
    foreach ($l in $libs | Select-Object -Unique) {
        $g = Join-Path $l 'steamapps\common\Moon Sweepers'
        if (Test-Path (Join-Path $g 'GameAssembly.dll')) { return (Resolve-Path $g).Path }
    }
    $null
}

function Expand-Into($zip, $dest, [scriptblock]$filter = { $true }) {
    $a = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($e in $a.Entries) {
            if (-not $e.Name -or -not (& $filter $e.FullName)) { continue }
            $out = Join-Path $dest ($e.FullName -replace '/', '\')
            New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $out, $true)
        }
    } finally { $a.Dispose() }
}

function Zip-Has($zip, $pattern) {
    $a = [IO.Compression.ZipFile]::OpenRead($zip)
    try { return [bool]($a.Entries | Where-Object { $_.FullName -like $pattern }) } finally { $a.Dispose() }
}

# ---------------------------------------------------------------- game folder
if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'GameAssembly.dll'))) {
    throw "Couldn't find Moon Sweepers. Run again with -GameDir '<your Moon Sweepers folder>'."
}
Say "Moon Sweepers: $GameDir" 'Cyan'
if (Get-Process MoonSweepers -ErrorAction SilentlyContinue) { throw 'Close Moon Sweepers first (its files are locked while it runs).' }
$plugins = Join-Path $GameDir 'BepInEx\plugins'
$record = Join-Path $plugins '.modpack-installed.json'
$installed = if (Test-Path $record) { Get-Content $record -Raw | ConvertFrom-Json } else { $null }

function Remove-Installed {
    if (-not $installed) { return }
    foreach ($f in $installed.folders) {
        $p = Join-Path $plugins $f
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
}

$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Moon Sweepers (modded).lnk'
# Everything BepInEx + ModEngine add to the game folder; none of it is the game's own.
$LoaderFiles = 'BepInEx', 'dotnet', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt', 'steam_appid.txt'

if ($Uninstall) {
    Remove-Installed
    Remove-Item $record, $lnk -ErrorAction SilentlyContinue
    Say 'Mods removed.' 'Green'
    $others = @(Get-ChildItem $plugins -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
    if (-not $Vanilla) {
        Say ''
        if ($others) { Say "Other mods still in BepInEx\plugins: $($others -join ', ')" 'Yellow' }
        $a = Read-Host "Also remove BepInEx$(if ($others) { ' (and those mods)' }) to get the plain game back? [y/N]"
        $Vanilla = $a -match '^(y|yes)$'
    }
    if ($Vanilla) {
        foreach ($f in $LoaderFiles) { $p = Join-Path $GameDir $f; if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
        $mine = Join-Path $GameDir 'MoonSweepersMods'
        if (Test-Path $mine) { Remove-Item $mine -Recurse -Force }
        Say 'BepInEx removed: the game is back to vanilla.' 'Green'
    } else { Say 'BepInEx is still installed (run this again and answer y to remove it).' }
    Say 'Saves are untouched. Modded vehicles wait in garagelib.json next to the save and come back if you reinstall.'
    return
}

$tmp = Join-Path $env:TEMP 'MoonSweepersMods'
New-Item -ItemType Directory -Force $tmp | Out-Null

# ---------------------------------------------------------------- BepInEx
$core = Join-Path $GameDir 'BepInEx\core\BepInEx.Core.dll'
$have = if (Test-Path $core) { (Get-Item $core).VersionInfo.ProductVersion } else { '' }
$freshLoader = -not $have.StartsWith($BepInExVersion)
if ($freshLoader) {
    Say "Installing BepInEx $BepInExVersion$(if ($have) { " (found $have)" })..."
    $bz = Join-Path $tmp 'bepinex.zip'
    Invoke-WebRequest $BepInExUrl -OutFile $bz -UseBasicParsing
    Expand-Into $bz $GameDir
} else { Say "BepInEx $BepInExVersion already installed" }

# ---------------------------------------------------------------- ModEngine (Nexus only: can't be downloaded for you)
$engineDir = Join-Path $GameDir 'BepInEx\patchers\MoonSweepers.ModEngine'
if (-not (Test-Path (Join-Path $engineDir 'MoonSweepers.ModEngine.dll'))) {
    $downloads = Join-Path $env:USERPROFILE 'Downloads'
    function Find-EngineZip { Get-ChildItem $downloads -Filter *.zip -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Where-Object { Zip-Has $_.FullName '*MoonSweepers.ModEngine.dll' } | Select-Object -First 1 }
    $ez = Find-EngineZip
    if (-not $ez) {
        Say ''
        Say 'One manual step: the MoonSweepers.ModEngine patcher is only on Nexus Mods.' 'Yellow'
        Say "Opening $ModEngineUrl - download its zip (Manual download) into your Downloads folder." 'Yellow'
        Start-Process $ModEngineUrl
        while (-not $ez) {
            Read-Host 'Press Enter once it has downloaded' | Out-Null
            $ez = Find-EngineZip
            if (-not $ez) { Say "No zip with MoonSweepers.ModEngine.dll in $downloads yet." 'Yellow' }
        }
    }
    Say "Installing ModEngine from $($ez.Name)"
    New-Item -ItemType Directory -Force $engineDir | Out-Null
    $a = [IO.Compression.ZipFile]::OpenRead($ez.FullName)
    try {
        # Zips differ in layout; take the patcher DLL wherever it sits.
        foreach ($e in $a.Entries | Where-Object { $_.Name -like 'MoonSweepers.ModEngine*.dll' }) {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $engineDir $e.Name), $true)
        }
    } finally { $a.Dispose() }
} else { Say 'ModEngine already installed' }

# ---------------------------------------------------------------- modpack
if ($Zip) {
    $packZip = (Resolve-Path $Zip).Path
    $version = 'local'
} else {
    $rels = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases?per_page=100" -Headers @{ 'User-Agent' = 'MoonSweepersMods' }
    $rel = $rels | Where-Object { $_.tag_name -like 'modpack-v*' -and -not $_.draft -and -not $_.prerelease } |
        Sort-Object { [version]($_.tag_name -replace '^modpack-v', '') } -Descending | Select-Object -First 1
    if (-not $rel) { throw "No modpack release on github.com/$Repo yet." }
    $version = $rel.tag_name -replace '^modpack-v', ''
    $asset = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
    if ($installed -and $installed.version -eq $version -and -not ($installed.folders | Where-Object { -not (Test-Path (Join-Path $plugins $_)) })) {
        Say "Modpack $version is up to date" 'Green'
        $asset = $null
    } else {
        Say "Downloading modpack $version$(if ($installed) { " (had $($installed.version))" })..."
        $packZip = Join-Path $tmp $asset.name
        Invoke-WebRequest $asset.browser_download_url -OutFile $packZip -UseBasicParsing
    }
}
if ($Zip -or $asset) {
    # Folders this modpack owns: BepInEx/plugins/<Name>/...
    $a = [IO.Compression.ZipFile]::OpenRead($packZip)
    try {
        $folders = @($a.Entries | ForEach-Object { if ($_.FullName -match '^BepInEx/plugins/([^/]+)/') { $Matches[1] } } | Select-Object -Unique)
    } finally { $a.Dispose() }
    if (-not $folders) { throw "$packZip has no BepInEx/plugins folders" }
    Remove-Installed
    foreach ($f in $folders) { $p = Join-Path $plugins $f; if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
    Expand-Into $packZip $GameDir { param($n) $n -like 'BepInEx/*' -or $n -like 'MoonSweepersMods/*' }
    [pscustomobject]@{ version = $version; folders = $folders; installedAt = (Get-Date).ToString('s') } | ConvertTo-Json | Set-Content $record -Encoding UTF8
    Say "Installed modpack $version`: $($folders -join ', ')" 'Green'
}

# ---------------------------------------------------------------- shortcut: update on every launch
$here = Join-Path $GameDir 'MoonSweepersMods'
$bat = Join-Path $here 'Install or update.bat'
if ((Test-Path $bat) -and -not (Test-Path $lnk)) {
    $s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
    $s.TargetPath = $bat
    $s.Arguments = '-Launch'
    $s.WorkingDirectory = $here
    $exe = Join-Path $GameDir 'MoonSweepers.exe'
    if (Test-Path $exe) { $s.IconLocation = $exe }
    $s.Save()
    Say "Made a desktop shortcut 'Moon Sweepers (modded)': it updates the mods, then starts the game." 'Green'
}

if ($freshLoader) { Say 'First launch with mods takes a minute or two while BepInEx prepares the game. Later launches are quick.' 'Yellow' }
if ($Launch) { Start-Process "steam://rungameid/$SteamAppId" }
