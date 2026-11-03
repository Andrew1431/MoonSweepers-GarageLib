# Compatibility wrapper for one release: use new-package.ps1 -Kind vehicle instead.
#   .\new-vehicle.ps1 -Name RoverX -Author yourname [-Title "Rover X"]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]+$')][string]$Name,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]+$')][string]$Author,
    [string]$Title
)
& "$PSScriptRoot\new-package.ps1" -Kind vehicle @PSBoundParameters
