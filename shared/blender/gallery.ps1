# Builds research/concept-gallery/index.html from every vehicles/<Name>/blender/preview_*.png and CONCEPT.md.
# Images are shrunk to JPEG and embedded so the page is one file.
param([int]$Width = 720)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$stats = Get-Content (Join-Path $root 'data\lines.vehicles.json') -Raw | ConvertFrom-Json
$enc = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
$ep = New-Object Drawing.Imaging.EncoderParameters 1
$ep.Param[0] = New-Object Drawing.Imaging.EncoderParameter([Drawing.Imaging.Encoder]::Quality, [long]82)

function Shrink($path) {
    $src = [Drawing.Image]::FromFile($path)
    try {
        $h = [int]($src.Height * $Width / $src.Width)
        $bmp = New-Object Drawing.Bitmap $Width, $h
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.DrawImage($src, 0, 0, $Width, $h)
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, $enc, $ep)
        $g.Dispose(); $bmp.Dispose()
        return 'data:image/jpeg;base64,' + [Convert]::ToBase64String($ms.ToArray())
    } finally { $src.Dispose() }
}

$items = @()
foreach ($dir in Get-ChildItem (Join-Path $root 'vehicles') -Directory) {
    $hero = Join-Path $dir.FullName 'blender\preview_hero.png'
    if (-not (Test-Path $hero)) { continue }
    $concept = Join-Path $dir.FullName 'CONCEPT.md'
    $text = if (Test-Path $concept) { [IO.File]::ReadAllText($concept) } else { '' }
    $id = ($stats.vehicles.PSObject.Properties | Where-Object { ($_.Value.name -replace '[^A-Za-z0-9]', '') -eq $dir.Name } | Select-Object -First 1)
    $items += [pscustomobject]@{
        folder = $dir.Name
        id = if ($id) { $id.Name } else { '' }
        stats = if ($id) { $id.Value } else { $null }
        concept = [string]$text
        hero = [string](Shrink $hero)
        side = if (Test-Path (Join-Path $dir.FullName 'blender\preview_side.png')) { [string](Shrink (Join-Path $dir.FullName 'blender\preview_side.png')) } else { '' }
        rear = if (Test-Path (Join-Path $dir.FullName 'blender\preview_rear.png')) { [string](Shrink (Join-Path $dir.FullName 'blender\preview_rear.png')) } else { '' }
        front = if (Test-Path (Join-Path $dir.FullName 'blender\preview_front.png')) { [string](Shrink (Join-Path $dir.FullName 'blender\preview_front.png')) } else { '' }
    }
    Write-Host "  $($dir.Name)"
}
$json = ConvertTo-Json -InputObject @($items) -Depth 6 -Compress
$template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'gallery.template.html'))
$out = Join-Path $root 'research\concept-gallery\index.html'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
[IO.File]::WriteAllText($out, $template.Replace('/*DATA*/[]', $json), (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB, $($items.Count) vehicles)"
