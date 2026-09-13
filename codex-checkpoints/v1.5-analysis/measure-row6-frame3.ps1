$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cellX = 3 * 192
$cellY = 6 * 208
$cellWidth = 192
$cellHeight = 208

function Measure-AlphaBounds {
    param(
        [System.Drawing.Bitmap] $Bitmap,
        [int] $Step
    )

    $minX = $Bitmap.Width
    $minY = $Bitmap.Height
    $maxX = -1
    $maxY = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y += $Step) {
        for ($x = 0; $x -lt $Bitmap.Width; $x += $Step) {
            if ($Bitmap.GetPixel($x, $y).A -lt 24) {
                continue
            }

            $minX = [Math]::Min($minX, $x)
            $minY = [Math]::Min($minY, $y)
            $maxX = [Math]::Max($maxX, $x)
            $maxY = [Math]::Max($maxY, $y)
        }
    }

    if ($maxX -lt $minX -or $maxY -lt $minY) {
        return $null
    }

    $width = $maxX - $minX + 1
    $height = $maxY - $minY + 1

    [pscustomobject]@{
        Left = $minX
        Top = $minY
        Right = $maxX + 1
        Bottom = $maxY + 1
        Width = $width
        Height = $height
        CenterX = $minX + $width / 2.0
        CenterY = $minY + $height / 2.0
    }
}

$measurements = @{}
$cells = @{}

foreach ($pet in @("Marmalade", "Merry")) {
    $atlasPath = Join-Path $repoRoot "Assets\$pet\spritesheet.png"
    $atlas = [System.Drawing.Bitmap]::new($atlasPath)

    try {
        $rectangle = [System.Drawing.Rectangle]::new($cellX, $cellY, $cellWidth, $cellHeight)
        $cell = $atlas.Clone($rectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $cellPath = Join-Path $PSScriptRoot "$pet-row6-frame3.png"
        $cell.Save($cellPath, [System.Drawing.Imaging.ImageFormat]::Png)

        $cells[$pet] = $cell
        $measurements[$pet] = [pscustomobject]@{
            Diagnostic = Measure-AlphaBounds -Bitmap $cell -Step 2
            FullPixel = Measure-AlphaBounds -Bitmap $cell -Step 1
        }
    }
    finally {
        $atlas.Dispose()
    }
}

$comparison = [System.Drawing.Bitmap]::new(384, 208, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($comparison)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.DrawImageUnscaled($cells["Marmalade"], 0, 0)
$graphics.DrawImageUnscaled($cells["Merry"], 192, 0)
$comparison.Save((Join-Path $PSScriptRoot "row6-frame3-comparison.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$comparison.Dispose()

foreach ($cell in $cells.Values) {
    $cell.Dispose()
}

foreach ($mode in @("Diagnostic", "FullPixel")) {
    $reference = $measurements["Marmalade"].$mode
    $merry = $measurements["Merry"].$mode
    $widthError = [Math]::Abs($merry.Width - $reference.Width) / [double]$reference.Width
    $heightError = [Math]::Abs($merry.Height - $reference.Height) / [double]$reference.Height

    [pscustomobject]@{
        Mode = $mode
        Marmalade = $reference
        Merry = $merry
        CenterDx = $merry.CenterX - $reference.CenterX
        CenterDy = $merry.CenterY - $reference.CenterY
        BottomDelta = $merry.Bottom - $reference.Bottom
        SizeErrorPercent = [Math]::Max($widthError, $heightError) * 100
    } | ConvertTo-Json -Depth 4 -Compress
}
