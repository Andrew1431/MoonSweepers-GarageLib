# Copies one Moon Sweepers save slot folder to another, for testing against a clone of a real save.
#   .\copy-slot.ps1 -From 1 -To 2            refuses if slot-2 already exists
#   .\copy-slot.ps1 -From 1 -To 2 -Force     overwrites (mirrors) slot-2
# Saves live in %USERPROFILE%\AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions\slot-N. Close the game first.
param(
    [Parameter(Mandatory)][int]$From,
    [Parameter(Mandatory)][int]$To,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
if ($From -eq $To) { throw "From and To are the same slot ($From)." }
if ($To -eq 1) { throw "Refusing to write slot-1: it is the real save. Copy it to another slot instead." }
$root = Join-Path $env:USERPROFILE 'AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions'
$src = Join-Path $root "slot-$From"
$dst = Join-Path $root "slot-$To"
if (-not (Test-Path $src -PathType Container)) { throw "Source $src does not exist." }
if ((Test-Path $dst) -and -not $Force) { throw "Target $dst already exists; pass -Force to overwrite it." }

# /E copies subfolders including empty ones; with -Force, /MIR also removes files the source no longer has.
$mode = if ($Force) { '/MIR' } else { '/E' }
robocopy $src $dst $mode /NFL /NDL /NJH /NP | Out-Host
# robocopy exit codes 0-7 are success (bit flags for copied / extra / mismatched); 8+ is a failure.
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }
$n = (Get-ChildItem $dst -Recurse -File).Count
Write-Host "Copied slot-$From -> slot-$To ($n files) at $dst"
$global:LASTEXITCODE = 0
