# The dev loop: close the game, build the projects (each installs itself into the game), launch, optionally tail the log.
#   .\dev.ps1 [-Only GarageLib[,MsChoppa]] [-NoLaunch] [-Tail] [-Release] [-NoBuild] [-WhatIf]
# Builds per project because the .slnx needs .NET SDK 9.0.200 or newer. Game folder: MOONSWEEPERS_DIR, else the default
# Steam path, else GameDir from Directory.Build.local.props. -Release builds without the [Dev] helpers.
# -NoBuild skips the build step (relaunch or tail only). -WhatIf prints the plan and does nothing.
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
    $steam = 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers'
    if (Test-Path -LiteralPath $steam -PathType Container) { return $steam }
    $local = Join-Path $root 'Directory.Build.local.props'
    if (Test-Path -LiteralPath $local) {
        $m = [regex]::Match((Get-Content -LiteralPath $local -Raw), '<GameDir>\s*([^<]+?)\s*</GameDir>')
        if ($m.Success -and (Test-Path -LiteralPath $m.Groups[1].Value -PathType Container)) { return $m.Groups[1].Value }
    }
    throw "Moon Sweepers folder not found. $hint."
}

# Project files as repo-relative Windows paths, src first (the others reference GarageLib's output).
function Get-Projects {
    foreach ($dir in 'src', 'vehicles', 'mods', 'examples') {
        $base = Join-Path $root $dir
        if (-not (Test-Path $base)) { continue }
        foreach ($d in Get-ChildItem $base -Directory | Sort-Object Name) {
            $proj = Join-Path $d.FullName "$($d.Name).csproj"
            if (Test-Path $proj) { [pscustomobject]@{ Name = $d.Name; Path = "$dir\$($d.Name)\$($d.Name).csproj" } }
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
    $buildArgs = @('build', $project.Path, '-c', 'Release')
    if ($Release) { $buildArgs += '-p:DevBuild=false' }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $($project.Name) (dotnet exit code $LASTEXITCODE)." }
}

function Follow-Log($gameDir, $names) {
    $log = Join-Path $gameDir 'BepInEx\LogOutput.log'
    $deadline = (Get-Date).AddSeconds($TailTimeoutSeconds)
    while (-not (Test-Path -LiteralPath $log)) {
        if ((Get-Date) -gt $deadline) { "Gave up waiting for $log after $TailTimeoutSeconds s (is BepInEx installed?)."; return }
        Start-Sleep -Milliseconds 500
    }
    "Tailing $log (Ctrl+C to stop)"
    $sources = (@($names) + 'BepInEx' | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $mine = "^\[\w+\s*:\s*($sources)\]"
    # Lines not starting with '[' are continuations (stack traces) of the line above; keep them with it.
    Get-Content -LiteralPath $log -Wait -Tail 0 | Where-Object { $_ -match $mine -or $_ -notmatch '^\[' }
}

$gameDir = Resolve-GameDir
$projects = Select-Projects @(Get-Projects)
$running = [bool](Get-Process -Name MoonSweepers -ErrorAction SilentlyContinue)

if ($WhatIf) {
    "Game folder: $gameDir"
    if ($running) { 'Would close MoonSweepers.exe (taskkill /IM MoonSweepers.exe /F)' } else { 'MoonSweepers.exe is not running; nothing to close' }
    if ($NoBuild) { 'Would skip the build (-NoBuild)' }
    else {
        foreach ($p in $projects) { "Would run: dotnet build $($p.Path) -c Release$(if ($Release) { ' -p:DevBuild=false' })" }
    }
    if ($NoLaunch) { 'Would not launch the game (-NoLaunch)' } else { "Would launch $(Join-Path $gameDir 'MoonSweepers.exe')" }
    if ($Tail) { "Would tail $(Join-Path $gameDir 'BepInEx\LogOutput.log') for: $((@($projects.Name) + 'BepInEx') -join ', ')" } else { 'Would not tail the log' }
    return
}

if ($running) {
    "Closing MoonSweepers.exe"
    taskkill /IM MoonSweepers.exe /F | Out-Null
    Start-Sleep -Seconds 1
}

if (-not $NoBuild) {
    # The game folder reaches MSBuild through MOONSWEEPERS_DIR / Directory.Build.local.props, same as plain dotnet build.
    foreach ($p in $projects) { "Building $($p.Name)"; Invoke-Build $p }
}

if (-not $NoLaunch) {
    $exe = Join-Path $gameDir 'MoonSweepers.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "MoonSweepers.exe not found in $gameDir. $hint." }
    "Launching $exe"
    Start-Process -FilePath $exe -WorkingDirectory $gameDir
}

if ($Tail) { Follow-Log $gameDir $projects.Name }
