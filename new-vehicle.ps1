# Scaffolds vehicles/<Name> from templates/Vehicle and adds it to the solution.
#   .\new-vehicle.ps1 -Name RoverX -Author yourname [-Title "Rover X"]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]+$')][string]$Name,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]+$')][string]$Author,
    [string]$Title
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dest = Join-Path $root "vehicles\$Name"
if (Test-Path $dest) { throw "vehicles\$Name already exists" }
if (-not $Title) { $Title = ($Name -creplace '(?<=[a-z0-9])([A-Z])', ' $1') }
$kebab = ($Name -creplace '(?<=[a-z0-9])([A-Z])', '-$1').ToLower()

New-Item -ItemType Directory $dest | Out-Null
Get-ChildItem (Join-Path $root 'templates\Vehicle') -File | ForEach-Object {
    $text = (Get-Content $_.FullName -Raw).
        Replace('__NAME__', $Name).Replace('__TITLE__', $Title).Replace('__AUTHOR__', $Author).
        Replace('__LOWER__', $Name.ToLower()).Replace('__KEBAB__', $kebab)
    $out = Join-Path $dest ($_.Name.Replace('__NAME__', $Name))
    [IO.File]::WriteAllText($out, $text, (New-Object Text.UTF8Encoding $false))
}
dotnet sln (Join-Path $root 'GarageLib.slnx') add (Join-Path $dest "$Name.csproj") | Out-Null
Write-Host "Created vehicles\$Name. Build it with: dotnet build vehicles\$Name -c Release"
