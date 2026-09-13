$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$target = [System.Drawing.Rectangle]::new(0, 8 * 208, 192, 208)

function Measure-AlphaBounds {
    param(
        [System.Drawing.Bitmap] $Bitmap,
        [int] $Step,
        [int] $AlphaThreshold
    )

    $minX = $Bitmap.Width
    $minY = $Bitmap.Height
    $maxX = -1
    $maxY = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y += $Step) {
        for ($x = 0; $x -lt $Bitmap.Width; $x += $Step) {
            if ($Bitmap.GetPixel($x, $y).A -lt $AlphaThreshold) {
                continue
            }

            $minX = [Math]::Min($minX, $x)
            $minY = [Math]::Min($minY, $y)
            $maxX = [Math]::Max($maxX, $x)
            $maxY = [Math]::Max($maxY, $y)
        }
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

$cells = @{}
$measurements = @{}

foreach ($pet in @("Marmalade", "Merry")) {
    $atlas = [System.Drawing.Bitmap]::new((Join-Path $repoRoot "Assets\$pet\spritesheet.png"))

    try {
        $cell = $atlas.Clone($target, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $cells[$pet] = $cell
        $suffix = if ($pet -eq "Merry") { "focused-before" } else { "focused" }
        $cell.Save(
            (Join-Path $PSScriptRoot "$pet-row8-frame0-$suffix.png"),
            [System.Drawing.Imaging.ImageFormat]::Png
        )

        $measurements[$pet] = [pscustomobject]@{
            Diagnostic = Measure-AlphaBounds -Bitmap $cell -Step 2 -AlphaThreshold 24
            FullPixel = Measure-AlphaBounds -Bitmap $cell -Step 1 -AlphaThreshold 24
            CompleteAlpha = Measure-AlphaBounds -Bitmap $cell -Step 1 -AlphaThreshold 1
        }
    }
    finally {
        $atlas.Dispose()
    }
}

$comparison = [System.Drawing.Bitmap]::new(384, 208, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt 208; $y++) {
    for ($x = 0; $x -lt 192; $x++) {
        $comparison.SetPixel($x, $y, $cells["Marmalade"].GetPixel($x, $y))
        $comparison.SetPixel($x + 192, $y, $cells["Merry"].GetPixel($x, $y))
    }
}
$comparison.Save((Join-Path $PSScriptRoot "row8-frame0-focused-comparison.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$comparison.Dispose()

foreach ($mode in @("Diagnostic", "FullPixel", "CompleteAlpha")) {
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
        WidthErrorPercent = $widthError * 100
        HeightErrorPercent = $heightError * 100
        SizeErrorPercent = [Math]::Max($widthError, $heightError) * 100
    } | ConvertTo-Json -Depth 4 -Compress
}

foreach ($cell in $cells.Values) {
    $cell.Dispose()
}
