# Fabrique src\SDRLink.ico : carré bleu marine arrondi, « SDR » en gros, « LINK » en petit dessous.
# Une image dessinée pour chaque taille (barre des tâches 16 à 48, Explorateur jusqu'à 256), en PNG dans l'ico.
param([string]$Out = (Join-Path $PSScriptRoot '..\src\SDRLink.ico'))
Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = if ($s -le 24) { 'SingleBitPerPixelGridFit' } else { 'AntiAliasGridFit' }
    $g.Clear([Drawing.Color]::Transparent)
    # Fond arrondi.
    $r = [Math]::Max(2, [int]($s * 0.18))
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $w = $s - 1
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $w - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $bg = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Rectangle 0, 0, $s, $s), ([Drawing.Color]::FromArgb(20, 40, 150)), ([Drawing.Color]::FromArgb(0, 0, 90)), 90
    $g.FillPath($bg, $path)
    $orange = [Drawing.Color]::FromArgb(255, 165, 40)
    if ($s -le 24) {
        # Petites tailles : lettres dessinées au pixel près (une police lissée y devient illisible).
        $big = @{ S = 'XXX', 'X..', 'XXX', '..X', 'XXX'; D = 'XX.', 'X.X', 'X.X', 'X.X', 'XX.'; R = 'XX.', 'X.X', 'XX.', 'X.X', 'X.X' }
        $small = @{ L = 'X..', 'X..', 'X..', 'XXX'; I = 'X', 'X', 'X', 'X'; N = 'X..X', 'XX.X', 'X.XX', 'X..X'; K = 'X.X', 'XX.', 'XX.', 'X.X' }
        function DrawPix($text, $font, $sx, $sy, $top, $color) {
            $wd = 0; foreach ($ch in $text.ToCharArray()) { $wd += $font["$ch"][0].Length * $sx + $sx }
            $wd -= $sx
            $x0 = [Math]::Floor(($s - $wd) / 2)
            $br = New-Object Drawing.SolidBrush $color
            foreach ($ch in $text.ToCharArray()) {
                $gl = $font["$ch"]
                for ($row = 0; $row -lt $gl.Count; $row++) { for ($col = 0; $col -lt $gl[$row].Length; $col++) {
                    if ($gl[$row][$col] -eq 'X') { $g.FillRectangle($br, $x0 + $col * $sx, $top + $row * $sy, $sx, $sy) } } }
                $x0 += $gl[0].Length * $sx + $sx
            }
        }
        $g.SmoothingMode = 'None'
        $k = if ($s -ge 24) { 2 } else { 1 }
        $topY = if ($s -ge 24) { 3 } elseif ($s -ge 20) { 4 } else { 2 }
        DrawPix 'SDR' $big $k $k $topY ([Drawing.Color]::White)
        DrawPix 'LINK' $small 1 1 ($topY + 5 * $k + [Math]::Max(2, $k + 1)) $orange
    } else {
        # SDR (blanc) dans le haut, LINK (orange) dessous, chacun réduit jusqu'à tenir dans la largeur.
        $sf = New-Object Drawing.StringFormat
        $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
        $sf.FormatFlags = 'NoWrap, NoClip'; $sf.Trimming = 'None'
        function Fit($text, $em, $maxW) {
            do { $f = New-Object Drawing.Font 'Arial', ([float]$em), ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel); $em *= 0.95 }
            while ($g.MeasureString($text, $f, 10000, [Drawing.StringFormat]::GenericTypographic).Width -gt $maxW)
            $f
        }
        $f1 = Fit 'SDR' ($s * 0.44) ($s * 0.84)
        $f2 = Fit 'LINK' ($s * 0.26) ($s * 0.72)
        $top = New-Object Drawing.RectangleF 0, ([float]($s * 0.06)), $s, ([float]($s * 0.54))
        $bot = New-Object Drawing.RectangleF 0, ([float]($s * 0.58)), $s, ([float]($s * 0.34))
        $g.DrawString('SDR', $f1, [Drawing.Brushes]::White, $top, $sf)
        $g.DrawString('LINK', $f2, (New-Object Drawing.SolidBrush $orange), $bot, $sf)
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    if ($s -ge 256) { $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png) }
    else {
        # Image DIB 32 bits (lue partout, y compris par .NET), de bas en haut, suivie du masque ET (vide).
        $w2 = New-Object IO.BinaryWriter $ms
        $w2.Write([UInt32]40); $w2.Write([Int32]$s); $w2.Write([Int32]($s * 2)); $w2.Write([UInt16]1); $w2.Write([UInt16]32)
        $w2.Write([UInt32]0); $w2.Write([UInt32]0); $w2.Write([Int32]0); $w2.Write([Int32]0); $w2.Write([UInt32]0); $w2.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $w2.Write([byte]$c.B); $w2.Write([byte]$c.G); $w2.Write([byte]$c.R); $w2.Write([byte]$c.A) } }
        $w2.Write((New-Object byte[] ([Math]::Floor(($s + 31) / 32) * 4 * $s)))
        $w2.Flush()
    }
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}
# En-tête ICO, répertoire, puis les PNG.
$o = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $o
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Out), $o.ToArray())
"OK : $Out ($($o.Length) octets)"
