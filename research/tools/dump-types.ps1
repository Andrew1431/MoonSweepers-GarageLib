# Dumps the public shape of game types from the BepInEx interop assemblies, in the same format as research/dump_*.txt.
#   .\dump-types.ps1 MoonDepotBuildDefinition MoonDepotSnapshot          one file per type, into research/
#   .\dump-types.ps1 -All                                                research/types.txt (every type, one line each)
#   .\dump-types.ps1 -Match 'Production|Power|Cargo'                     every type whose full name matches the regex
# Game path: MOONSWEEPERS_DIR, else the default Steam folder. Needs BepInEx/interop (launch the game once).
param(
    [Parameter(Position = 0, ValueFromRemainingArguments)][string[]]$Types,
    [string]$Match,
    [switch]$All,
    [string]$OutDir = (Join-Path $PSScriptRoot '..')
)
$ErrorActionPreference = 'Stop'
$game = if ($env:MOONSWEEPERS_DIR) { $env:MOONSWEEPERS_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers' }
$interop = Join-Path $game 'BepInEx\interop'
$core = Join-Path $game 'BepInEx\core'
if (-not (Test-Path (Join-Path $interop 'Assembly-CSharp.dll'))) { throw "No interop assemblies at $interop. Install BepInEx + ModEngine and launch the game once." }

# Interop DLLs reference Il2CppInterop.Runtime and each other; resolve from core and interop.
$dirs = @($core, $interop)
$handler = [ResolveEventHandler]{
    param($sender, $e)
    $name = ($e.Name -split ',')[0]
    foreach ($d in $dirs) { $p = Join-Path $d "$name.dll"; if (Test-Path $p) { return [Reflection.Assembly]::LoadFrom($p) } }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($handler)
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $interop 'Assembly-CSharp.dll'))

function TypeName($t) {
    if ($null -eq $t) { return '?' }
    if ($t.IsGenericType) {
        $base = $t.GetGenericTypeDefinition().FullName
        if (-not $base) { $base = $t.Name }
        return $base + '<' + (($t.GetGenericArguments() | ForEach-Object { TypeName $_ }) -join ',') + '>'
    }
    if ($t.FullName) { return $t.FullName.Replace('+', '/') } else { return $t.Name }
}
function ShortName($t) {
    if ($null -eq $t) { return '?' }
    if ($t.IsGenericType) { return $t.Name.Split('`')[0] + '`' + $t.GetGenericArguments().Count }
    if ($t.IsByRef) { return (ShortName $t.GetElementType()) + '&' }
    return $t.Name
}
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'

function Dump($t) {
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("== $(($t.FullName).Replace('+','/')) : $(TypeName $t.BaseType)")
    if ($t.IsEnum) {
        foreach ($n in [Enum]::GetNames($t)) { $lines.Add("  E $n = $([int][Enum]::Parse($t, $n))") }
        return $lines
    }
    foreach ($f in $t.GetFields($flags) | Sort-Object MetadataToken) {
        if ($f.Name -like 'NativeFieldInfoPtr_*' -or $f.Name -like 'NativeMethodInfoPtr_*') { continue }
        $lines.Add("  F $(if ($f.IsStatic) {'static '})$(TypeName $f.FieldType) $($f.Name)")
    }
    foreach ($p in $t.GetProperties($flags) | Sort-Object MetadataToken) {
        $acc = $p.GetAccessors($true) | Select-Object -First 1
        $lines.Add("  P $(if ($acc -and $acc.IsStatic) {'static '})$(TypeName $p.PropertyType) $($p.Name)")
    }
    foreach ($m in $t.GetMethods($flags) | Sort-Object MetadataToken) {
        if ($m.IsSpecialName -and ($m.Name -like 'get_*' -or $m.Name -like 'set_*')) { continue }
        $ps = ($m.GetParameters() | ForEach-Object { "$(ShortName $_.ParameterType) $($_.Name)" }) -join ', '
        $lines.Add("  M $(if ($m.IsStatic) {'static '})$(ShortName $m.ReturnType) $($m.Name)($ps)")
    }
    foreach ($c in $t.GetConstructors($flags)) {
        $ps = ($c.GetParameters() | ForEach-Object { "$(ShortName $_.ParameterType) $($_.Name)" }) -join ', '
        $lines.Add("  M Void .ctor($ps)")
    }
    return $lines
}

$everyType = $asm.GetTypes()
if ($All) {
    $out = Join-Path $OutDir 'types.txt'
    $everyType | ForEach-Object { "== $(($_.FullName).Replace('+','/')) : $(TypeName $_.BaseType)" } | Set-Content $out -Encoding utf8
    Write-Host "$($everyType.Count) types -> $out"
    return
}
$picked = @()
if ($Match) { $picked += $everyType | Where-Object { $_.FullName -match $Match -and $_.FullName -notmatch 'DisplayClass|_d__|MethodInfoStore' } }
foreach ($name in $Types) {
    $hits = $everyType | Where-Object { $_.Name -eq $name -or $_.FullName -eq $name -or $_.FullName -eq "MoonCleaners.$name" }
    if (-not $hits) { Write-Warning "No type named $name"; continue }
    # Nested types go into the same file.
    $picked += $hits
    $picked += $everyType | Where-Object { $_.DeclaringType -and $hits -contains $_.DeclaringType -and $_.FullName -notmatch 'DisplayClass|_d__|MethodInfoStore' }
}
if (-not $picked) { throw 'Nothing to dump: pass type names, -Match, or -All.' }
$groups = $picked | Sort-Object FullName -Unique | Group-Object { $top = $_; while ($top.DeclaringType) { $top = $top.DeclaringType }; $top.Name }
foreach ($g in $groups) {
    $out = Join-Path $OutDir "dump_$($g.Name).txt"
    $g.Group | ForEach-Object { Dump $_ } | Set-Content $out -Encoding utf8
    Write-Host "$($g.Group.Count) type(s) -> $out"
}
