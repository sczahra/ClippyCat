$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cellWidth = 192
$cellHeight = 208
$framesPerRow = @(6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8)

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

function Compare-Bounds {
    param($Reference, $Merry)

    $widthError = [Math]::Abs($Merry.Width - $Reference.Width) / [double]$Reference.Width
    $heightError = [Math]::Abs($Merry.Height - $Reference.Height) / [double]$Reference.Height

    [pscustomobject]@{
        Reference = $Reference
        Merry = $Merry
        CenterDx = $Merry.CenterX - $Reference.CenterX
        CenterDy = $Merry.CenterY - $Reference.CenterY
        BottomDelta = $Merry.Bottom - $Reference.Bottom
        SizeErrorPercent = [Math]::Max($widthError, $heightError) * 100
        WidthErrorPercent = $widthError * 100
        HeightErrorPercent = $heightError * 100
    }
}

$marmaladeAtlas = [System.Drawing.Bitmap]::new((Join-Path $repoRoot "Assets\Marmalade\spritesheet.png"))
$merryAtlas = [System.Drawing.Bitmap]::new((Join-Path $repoRoot "Assets\Merry\spritesheet.png"))

try {
    $targetRectangle = [System.Drawing.Rectangle]::new(3 * $cellWidth, 6 * $cellHeight, $cellWidth, $cellHeight)
    $marmaladeTarget = $marmaladeAtlas.Clone($targetRectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $merryTarget = $merryAtlas.Clone($targetRectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $comparison = [System.Drawing.Bitmap]::new(384, 208, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($comparison)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImageUnscaled($marmaladeTarget, 0, 0)
    $graphics.DrawImageUnscaled($merryTarget, 192, 0)
    $comparison.Save((Join-Path $PSScriptRoot "row6-frame3-after-comparison.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $comparison.Dispose()

    $targetDiagnostic = Compare-Bounds `
        (Measure-AlphaBounds -Bitmap $marmaladeTarget -Step 2) `
        (Measure-AlphaBounds -Bitmap $merryTarget -Step 2)
    $targetFull = Compare-Bounds `
        (Measure-AlphaBounds -Bitmap $marmaladeTarget -Step 1) `
        (Measure-AlphaBounds -Bitmap $merryTarget -Step 1)

    [pscustomobject]@{
        Target = "row6-frame3"
        Diagnostic = $targetDiagnostic
        FullPixel = $targetFull
    } | ConvertTo-Json -Depth 5 -Compress

    $outliers = @()

    for ($row = 0; $row -lt $framesPerRow.Count; $row++) {
        for ($column = 0; $column -lt $framesPerRow[$row]; $column++) {
            $rectangle = [System.Drawing.Rectangle]::new(
                $column * $cellWidth,
                $row * $cellHeight,
                $cellWidth,
                $cellHeight
            )
            $marmaladeCell = $marmaladeAtlas.Clone($rectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $merryCell = $merryAtlas.Clone($rectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

            try {
                $comparisonResult = Compare-Bounds `
                    (Measure-AlphaBounds -Bitmap $marmaladeCell -Step 2) `
                    (Measure-AlphaBounds -Bitmap $merryCell -Step 2)

                if ($row -ne 6 -or $column -ne 3) {
                    $outliers += [pscustomobject]@{
                        Row = $row
                        Frame = $column
                        SizeErrorPercent = $comparisonResult.SizeErrorPercent
                        WidthErrorPercent = $comparisonResult.WidthErrorPercent
                        HeightErrorPercent = $comparisonResult.HeightErrorPercent
                        CenterDx = $comparisonResult.CenterDx
                        CenterDy = $comparisonResult.CenterDy
                        BottomDelta = $comparisonResult.BottomDelta
                        Marmalade = $comparisonResult.Reference
                        Merry = $comparisonResult.Merry
                    }
                }
            }
            finally {
                $marmaladeCell.Dispose()
                $merryCell.Dispose()
            }
        }
    }

    [pscustomobject]@{
        ComparedFrames = 73
        NextWorstOutliers = @($outliers | Sort-Object SizeErrorPercent -Descending | Select-Object -First 3)
    } | ConvertTo-Json -Depth 5 -Compress

    $marmaladeTarget.Dispose()
    $merryTarget.Dispose()
}
finally {
    $marmaladeAtlas.Dispose()
    $merryAtlas.Dispose()
}
