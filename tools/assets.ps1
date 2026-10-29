<#
.SYNOPSIS
    Genera los assets de la app desde los PNG originales de la marca.
.EXAMPLE
    pwsh tools/assets.ps1 -Origen "$env:USERPROFILE\Downloads\Colmillo_assets"
#>
param([Parameter(Mandatory)] [string]$Origen)

Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

# Recorta el borde transparente que deja el quitafondos y escala en alta calidad.
function Escalar([string]$archivo, [int]$ancho, [int]$alto, [bool]$cuadrado) {
    $src = [System.Drawing.Bitmap]::FromFile($archivo)
    $minX = $src.Width; $minY = $src.Height; $maxX = 0; $maxY = 0
    for ($y = 0; $y -lt $src.Height; $y += 2) {
        for ($x = 0; $x -lt $src.Width; $x += 2) {
            if ($src.GetPixel($x, $y).A -gt 8) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    $rect = [System.Drawing.Rectangle]::FromLTRB($minX, $minY, [Math]::Min($maxX + 2, $src.Width), [Math]::Min($maxY + 2, $src.Height))
    if ($alto -eq 0) { $alto = [int][Math]::Round($ancho * $rect.Height / $rect.Width) }
    if ($ancho -eq 0) { $ancho = [int][Math]::Round($alto * $rect.Width / $rect.Height) }
    $dst = New-Object System.Drawing.Bitmap $ancho, $alto
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $destino = New-Object System.Drawing.Rectangle 0, 0, $ancho, $alto
    if ($cuadrado) {
        # Encaja el recorte dentro del cuadrado sin deformarlo.
        $escala = [Math]::Min($ancho / $rect.Width, $alto / $rect.Height)
        $w = [int]($rect.Width * $escala); $h = [int]($rect.Height * $escala)
        $destino = New-Object System.Drawing.Rectangle ([int](($ancho - $w) / 2)), ([int](($alto - $h) / 2)), $w, $h
    }
    $g.DrawImage($src, $destino, $rect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $src.Dispose()
    return $dst
}

function Png([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Sidebar e iconos de la UI: el doble del tamano en pantalla, para pantallas al 200 %.
(Escalar (Join-Path $Origen 'isotipo-Photoroom.png') 0 96 $false).Save((Join-Path $assets 'isotipo.png'))
(Escalar (Join-Path $Origen 'imagotipo-Photoroom.png') 0 88 $false).Save((Join-Path $assets 'imagotipo.png'))

# Icono de la app (exe, ventana, bandeja) desde el favicon: ICO con entradas PNG.
$tamanos = 16, 20, 24, 32, 40, 48, 64, 128, 256
$imagenes = foreach ($t in $tamanos) { , (Png (Escalar (Join-Path $Origen 'favicon-Photoroom.png') $t $t $true)) }
$cabecera = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $cabecera
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$tamanos.Count)
$offset = 6 + 16 * $tamanos.Count
for ($i = 0; $i -lt $tamanos.Count; $i++) {
    $t = $tamanos[$i]; $len = $imagenes[$i].Length
    $bw.Write([byte]($(if ($t -ge 256) { 0 } else { $t }))); $bw.Write([byte]($(if ($t -ge 256) { 0 } else { $t })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $imagenes) { $bw.Write($img) }
[System.IO.File]::WriteAllBytes((Join-Path $assets 'colmillo.ico'), $cabecera.ToArray())
Get-ChildItem $assets | ForEach-Object { "{0,-16} {1,8:N0} bytes" -f $_.Name, $_.Length }
