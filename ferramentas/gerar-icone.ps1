# Gera o icone (bandeja/exe) e a logo do Pit Overlay's, desenhados por codigo (System.Drawing).
#   powershell -ExecutionPolicy Bypass -File ferramentas\gerar-icone.ps1
# Saidas: src\Poe2Overlay.App\Recursos\icone.ico, assets\icone.png (256) e assets\logo.png (titulo completo).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$raiz = Split-Path $PSScriptRoot -Parent
$recursos = Join-Path $raiz 'src\Poe2Overlay.App\Recursos'
$assets = Join-Path $raiz 'assets'
New-Item -ItemType Directory -Force $recursos, $assets | Out-Null

$ouro = [Drawing.Color]::FromArgb(255, 0xE8, 0xC4, 0x6A)
$ouroClaro = [Drawing.Color]::FromArgb(255, 0xF6, 0xE0, 0xA0)
$borda = [Drawing.Color]::FromArgb(255, 0xB0, 0x8C, 0x40)
$fundoTopo = [Drawing.Color]::FromArgb(255, 0x2E, 0x26, 0x1C)
$fundoBase = [Drawing.Color]::FromArgb(255, 0x0E, 0x0C, 0x0A)

function Novo-Retangulo-Arredondado([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Monograma: quadrado arredondado escuro, anel "O" dourado e um "P" dentro.
function Desenhar-Monograma([Drawing.Graphics]$g, [single]$x, [single]$y, [single]$t) {
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.PixelOffsetMode = 'HighQuality'

    $m = [Math]::Max(0.5, $t * 0.02)
    $caixa = Novo-Retangulo-Arredondado ($x + $m) ($y + $m) ($t - 2 * $m) ($t - 2 * $m) ($t * 0.22)
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.PointF]::new($x, $y)), ([Drawing.PointF]::new($x, $y + $t)), $fundoTopo, $fundoBase
    $g.FillPath($grad, $caixa)
    $g.DrawPath((New-Object Drawing.Pen $borda, ([Math]::Max(1, $t * 0.045))), $caixa)

    # Anel (o "O"). Em 16-20 px (bandeja) nao cabe anel + letra legivel: fica so um "P" grande.
    $pequeno = $t -le 20
    if (-not $pequeno) {
        $espessura = if ($t -le 32) { $t * 0.10 } else { $t * 0.085 }
        $d = $t * 0.70
        $cx = $x + ($t - $d) / 2; $cy = $y + ($t - $d) / 2
        $g.DrawEllipse((New-Object Drawing.Pen $ouro, $espessura), [single]$cx, [single]$cy, [single]$d, [single]$d)
    }

    # "P" centrado.
    $tamanho = if ($pequeno) { $t * 0.86 } else { $t * 0.50 }
    $fonte = New-Object Drawing.Font 'Georgia', ([single]$tamanho), ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
    $formato = New-Object Drawing.StringFormat
    $formato.Alignment = 'Center'; $formato.LineAlignment = 'Center'
    $area = [Drawing.RectangleF]::new($x, $y + $t * 0.035, $t, $t)
    $g.DrawString('P', $fonte, (New-Object Drawing.SolidBrush $ouroClaro), $area, $formato)
}

function Bitmap-Monograma([int]$t) {
    $bmp = New-Object Drawing.Bitmap $t, $t, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.Clear([Drawing.Color]::Transparent)
    Desenhar-Monograma $g 0 0 $t
    $g.Dispose()
    return $bmp
}

# Entrada DIB (32 bpp + máscara AND) para os tamanhos pequenos: é o formato que o Windows/NotifyIcon lê sempre.
function Bytes-Dib([Drawing.Bitmap]$bmp) {
    $t = $bmp.Width
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $linhaMascara = [int][Math]::Ceiling($t / 32.0) * 4
    $w.Write([int]40); $w.Write([int]$t); $w.Write([int]($t * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($t * $t * 4 + $linhaMascara * $t)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($yy = $t - 1; $yy -ge 0; $yy--) {
        for ($xx = 0; $xx -lt $t; $xx++) {
            $c = $bmp.GetPixel($xx, $yy)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] ($linhaMascara * $t)))
    $w.Flush()
    return $ms.ToArray()
}

function Bytes-Png([Drawing.Bitmap]$bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

# .ico com 16, 20, 24, 32, 40, 48, 64 (DIB) e 256 (PNG).
$tamanhos = 16, 20, 24, 32, 40, 48, 64, 256
$imagens = foreach ($t in $tamanhos) {
    $bmp = Bitmap-Monograma $t
    if ($t -eq 256) { $bmp.Save((Join-Path $assets 'icone.png'), [Drawing.Imaging.ImageFormat]::Png); , (Bytes-Png $bmp) }
    else { , (Bytes-Dib $bmp) }
    $bmp.Dispose()
}
$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$tamanhos.Count)
$deslocamento = 6 + 16 * $tamanhos.Count
for ($i = 0; $i -lt $tamanhos.Count; $i++) {
    $t = $tamanhos[$i]; $dados = $imagens[$i]
    $lado = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$dados.Length); $w.Write([int]$deslocamento)
    $deslocamento += $dados.Length
}
foreach ($dados in $imagens) { $w.Write([byte[]]$dados) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $recursos 'icone.ico'), $ico.ToArray())

# Logo com o título completo.
$logo = New-Object Drawing.Bitmap 1200, 340, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($logo)
$g.Clear([Drawing.Color]::Transparent)
Desenhar-Monograma $g 30 30 280
$g.TextRenderingHint = 'AntiAlias'
$titulo = New-Object Drawing.Font 'Georgia', 118, ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
$sub = New-Object Drawing.Font 'Segoe UI', 38, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
$gradTexto = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.PointF]::new(0, 70)), ([Drawing.PointF]::new(0, 210)), $ouroClaro, $borda
$g.DrawString("Pit Overlay's", $titulo, $gradTexto, 345, 62)
$subtitulo = 'consulta de pre' + [char]0x00E7 + 'os para Path of Exile 2' # sem acento no fonte: o PowerShell 5.1 le .ps1 como ANSI
$g.DrawString($subtitulo, $sub, (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 0xB8, 0xB0, 0xA0))), 352, 214)
$g.Dispose()
$logo.Save((Join-Path $assets 'logo.png'), [Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

Write-Host "Gerado: $recursos\icone.ico, $assets\icone.png, $assets\logo.png"
