# Scaffolds a vehicle, mod or library from templates/<Kind> and adds it to GarageLib.slnx.
#   .\new-package.ps1 -Kind vehicle|mod|lib -Name RoverX -Author yourname [-Title "Rover X"]
# vehicle -> vehicles/<Name>, mod -> mods/<Name>, lib -> src/<Name>.
param(
    [Parameter(Mandatory)][ValidateSet('vehicle', 'mod', 'lib')][string]$Kind,
    [Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]+$')][string]$Name,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]+$')][string]$Author,
    [string]$Title
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$kinds = @{
    vehicle = @{ Dir = 'vehicles'; Template = 'Vehicle' }
    mod     = @{ Dir = 'mods';     Template = 'Mod' }
    lib     = @{ Dir = 'src';      Template = 'Lib' }
}
$k = $kinds[$Kind]

# Every project installs to BepInEx/plugins/<AssemblyName>, so a name is taken if any root has it.
foreach ($dir in 'src', 'vehicles', 'mods', 'examples') {
    if (Test-Path (Join-Path $root "$dir\$Name")) { throw "$dir\$Name already exists" }
}
if (-not $Title) { $Title = ($Name -creplace '(?<=[a-z0-9])([A-Z])', ' $1') }
$kebab = ($Name -creplace '(?<=[a-z0-9])([A-Z])', '-$1').ToLower()

$dest = Join-Path $root "$($k.Dir)\$Name"
New-Item -ItemType Directory $dest | Out-Null
Get-ChildItem (Join-Path $root "templates\$($k.Template)") -File | ForEach-Object {
    $text = (Get-Content $_.FullName -Raw).
        Replace('__NAME__', $Name).Replace('__TITLE__', $Title).Replace('__AUTHOR__', $Author).
        Replace('__LOWER__', $Name.ToLower()).Replace('__KEBAB__', $kebab)
    $out = Join-Path $dest ($_.Name.Replace('__NAME__', $Name))
    [IO.File]::WriteAllText($out, $text, (New-Object Text.UTF8Encoding $false))
}

# Add the project to GarageLib.slnx by text edit (dotnet sln does not read .slnx on older SDKs).
$slnx = Join-Path $root 'GarageLib.slnx'
$utf8 = New-Object Text.UTF8Encoding $false
$text = [IO.File]::ReadAllText($slnx)
$nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$line = "    <Project Path=`"$($k.Dir)/$Name/$Name.csproj`" />"
$folder = "  <Folder Name=`"/$($k.Dir)/`">"
$at = $text.IndexOf($folder)
if ($at -ge 0) {
    $close = $text.IndexOf('</Folder>', $at)
    $lineStart = $text.LastIndexOf("`n", $close) + 1
    $text = $text.Insert($lineStart, $line + $nl)
} else {
    $block = $folder + $nl + $line + $nl + '  </Folder>' + $nl
    $end = $text.LastIndexOf('</Solution>')
    $text = $text.Insert($end, $block)
}
[IO.File]::WriteAllText($slnx, $text, $utf8)

"Created $($k.Dir)\$Name. Build it with: dotnet build $($k.Dir)\$Name -c Release"
