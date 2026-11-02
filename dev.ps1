# The dev loop: close the game, build the projects (each installs itself into the game), launch, optionally tail the log.
#   .\dev.ps1 [-Only GarageLib[,MsChoppa]] [-NoLaunch] [-Tail] [-Release] [-NoBuild] [-TailTimeoutSeconds 60] [-WhatIf]
# Builds per project because the .slnx needs .NET SDK 9.0.200 or newer. Game folder, first match wins: MOONSWEEPERS_DIR
# (authoritative; an error if set but missing), then GameDir from Directory.Build.local.props, then the default Steam
# folder (only if it holds MoonSweepers.exe). -Release builds without the [Dev] helpers.
# -NoBuild skips the build step. -NoBuild -NoLaunch -Tail follows the running game's log and leaves the game running.
# The game is closed only when a build or launch needs it closed. -WhatIf prints the plan and does nothing.
# The tail keeps lines whose BepInEx log source is a project's plugin name (from its Plugin.cs) or folder name, or BepInEx.
[CmdletBinding()]
param(
    [string[]]$Only,
    [switch]$NoLaunch,
    [switch]$Tail,
    [switch]$Release,
    [switch]$NoBuild,
    [switch]$WhatIf,
    [int]$TailTimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$hint = 'Set MOONSWEEPERS_DIR to your Moon Sweepers folder'

function Resolve-GameDir {
    if ($env:MOONSWEEPERS_DIR) {
        # An explicit setting is authoritative; do not fall back to a different install.
        if (-not (Test-Path -LiteralPath $env:MOONSWEEPERS_DIR -PathType Container)) {
            throw "MOONSWEEPERS_DIR is set to $env:MOONSWEEPERS_DIR, which does not exist. $hint."
        }
        return (Resolve-Path -LiteralPath $env:MOONSWEEPERS_DIR).Path
    }
    $local = Join-Path $root 'Directory.Build.local.props'
    if (Test-Path -LiteralPath $local) {
        $m = [regex]::Match((Get-Content -LiteralPath $local -Raw), '<GameDir>\s*([^<]+?)\s*</GameDir>')
        if ($m.Success) {
            if (-not (Test-Path -LiteralPath $m.Groups[1].Value -PathType Container)) {
                throw "GameDir in $local is $($m.Groups[1].Value), which does not exist. Fix that file or $hint."
            }
            return $m.Groups[1].Value
        }
    }
    $steam = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers'
    if (Test-Path -LiteralPath (Join-Path $steam 'MoonSweepers.exe')) { return $steam }
    throw "Moon Sweepers folder not found. $hint."
}

# Project files as repo-relative Windows paths, src first (the others reference GarageLib's output).
function Get-Projects {
    foreach ($dir in 'src', 'vehicles', 'mods', 'examples') {
        $base = Join-Path $root $dir
        if (-not (Test-Path $base)) { continue }
        foreach ($d in Get-ChildItem $base -Directory | Sort-Object Name) {
            $proj = Join-Path $d.FullName "$($d.Name).csproj"
            if (Test-Path $proj) {
                # BepInEx log sources are the plugin names, not the folder names.
                $plugin = $d.Name
                $pc = Join-Path $d.FullName 'Plugin.cs'
                if (Test-Path -LiteralPath $pc) {
                    $m = [regex]::Match((Get-Content -LiteralPath $pc -Raw), '\[BepInPlugin\([^,]+,\s*"([^"]+)"')
                    if ($m.Success) { $plugin = $m.Groups[1].Value }
                }
                [pscustomobject]@{ Name = $d.Name; Plugin = $plugin; Path = "$dir\$($d.Name)\$($d.Name).csproj" }
            }
        }
    }
}

function Select-Projects($all) {
    if (-not $Only) { return @($all) }
    $picked = foreach ($n in $Only) {
        $hit = @($all | Where-Object { $_.Name -ieq $n })
        if (-not $hit) { throw "No project named '$n'. Known: $(($all.Name) -join ', ')." }
        $hit
    }
    return @($picked)
}

function Invoke-Build($project) {
    $buildArgs = @('build', $project.Path, '-c', 'Release', "-p:GameDir=$gameDir")
    if ($Release) { $buildArgs += '-p:DevBuild=false' }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $($project.Name) (dotnet exit code $LASTEXITCODE)." }
}

# Matches a log line whose source ([Info   :   Ms Choppa]) is a project's plugin name or folder name, or BepInEx.
function Get-SourceRegex($projects) {
    $names = @($projects | ForEach-Object { $_.Plugin; $_.Name }) + 'BepInEx' | Select-Object -Unique
    $sources = ($names | ForEach-Object { [regex]::Escape($_) }) -join '|'
    return "^\[\w+\s*:\s*($sources)\s*\]"
}

function Follow-Log($gameDir, $mine) {
    $log = Join-Path $gameDir 'BepInEx\LogOutput.log'
    $deadline = (Get-Date).AddSeconds($TailTimeoutSeconds)
    while (-not (Test-Path -LiteralPath $log)) {
        if ((Get-Date) -gt $deadline) { "Gave up waiting for $log after $TailTimeoutSeconds s (is BepInEx installed?)."; return }
        Start-Sleep -Milliseconds 500
    }
    "Tailing $log (Ctrl+C to stop)"
    # Lines not starting with '[' are continuations (stack traces) of the line above; keep them with it.
    Get-Content -LiteralPath $log -Wait -Tail 0 | Where-Object { $_ -match $mine -or $_ -notmatch '^\[' }
}

# No trailing backslash: it would escape the closing quote of -p:GameDir="..." in native argument parsing.
$gameDir = (Resolve-GameDir).TrimEnd('\')
$projects = Select-Projects @(Get-Projects)
$running = [bool](Get-Process -Name MoonSweepers -ErrorAction SilentlyContinue)
# Only close the game when a build or launch needs it closed; tail-only mode follows the running game.
$needsClose = (-not $NoBuild) -or (-not $NoLaunch)

if ($WhatIf) {
    "Game folder: $gameDir"
    if (-not $running) { 'MoonSweepers.exe is not running; nothing to close' }
    elseif (-not $needsClose) { 'MoonSweepers.exe is running; it would be left running (tail-only mode)' }
    else { 'Would close MoonSweepers.exe (taskkill /IM MoonSweepers.exe /F)' }
    if ($NoBuild) { 'Would skip the build (-NoBuild)' }
    else {
        foreach ($p in $projects) { "Would run: dotnet build $($p.Path) -c Release -p:GameDir=$gameDir$(if ($Release) { ' -p:DevBuild=false' })" }
    }
    if ($NoLaunch) { 'Would not launch the game (-NoLaunch)' } else { "Would launch $(Join-Path $gameDir 'MoonSweepers.exe')" }
    if ($Tail) {
        "Would tail $(Join-Path $gameDir 'BepInEx\LogOutput.log') for: $((@($projects | ForEach-Object { $_.Plugin; $_.Name }) + 'BepInEx' | Select-Object -Unique) -join ', ')"
        "Source filter: $(Get-SourceRegex $projects)"
    } else { 'Would not tail the log' }
    return
}

if ($running -and $needsClose) {
    "Closing MoonSweepers.exe"
    taskkill /IM MoonSweepers.exe /F | Out-Null
    Start-Sleep -Seconds 1
}

if (-not $NoBuild) {
    # GameDir is passed explicitly so MSBuild installs into the same folder this script closes, launches and tails.
    foreach ($p in $projects) { "Building $($p.Name)"; Invoke-Build $p }
}

if (-not $NoLaunch) {
    $exe = Join-Path $gameDir 'MoonSweepers.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "MoonSweepers.exe not found in $gameDir. $hint." }
    "Launching $exe"
    Start-Process -FilePath $exe -WorkingDirectory $gameDir
}

if ($Tail) { Follow-Log $gameDir (Get-SourceRegex $projects) }
