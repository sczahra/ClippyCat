$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$atlasPath = Join-Path $repoRoot "Assets\Merry\spritesheet.png"
$outputAtlasPath = Join-Path $PSScriptRoot "Merry-spritesheet-row6-frame3-corrected.png"
$afterCellPath = Join-Path $PSScriptRoot "Merry-row6-frame3-after.png"

$cellX = 3 * 192
$cellY = 6 * 208
$cellWidth = 192
$cellHeight = 208
$cellRectangle = [System.Drawing.Rectangle]::new($cellX, $cellY, $cellWidth, $cellHeight)

function Get-RegionHash {
    param(
        [System.Drawing.Bitmap] $Bitmap,
        [System.Drawing.Rectangle] $Rectangle
    )

    $region = $Bitmap.Clone($Rectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stream = [System.IO.MemoryStream]::new()
    $sha256 = [System.Security.Cryptography.SHA256]::Create()

    try {
        $region.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return [Convert]::ToHexString($sha256.ComputeHash($stream.ToArray()))
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
        $region.Dispose()
    }
}

$atlas = [System.Drawing.Bitmap]::new($atlasPath)

try {
    if ($atlas.Width -ne 1536 -or $atlas.Height -ne 2288) {
        throw "Unexpected Merry atlas dimensions: $($atlas.Width)x$($atlas.Height)"
    }

    $beforeCell = $atlas.Clone($cellRectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $correctedCell = [System.Drawing.Bitmap]::new($cellWidth, $cellHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $cellGraphics = [System.Drawing.Graphics]::FromImage($correctedCell)

    try {
        $cellGraphics.Clear([System.Drawing.Color]::Transparent)
        $cellGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $cellGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $cellGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $cellGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $cellGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        # Map the complete 192x208 source cell so its alpha>0 content moves from
        # (32,11)-(161,202) to approximately (29,4)-(163,203). This is a roughly
        # 4% content enlargement centered horizontally and anchored at the bottom.
        $scaleX = 134.0 / 129.0
        $scaleY = 199.0 / 191.0
        $destinationX = 29.0 - (32.0 * $scaleX)
        $destinationY = 4.0 - (11.0 * $scaleY)
        $destination = [System.Drawing.RectangleF]::new(
            [single]$destinationX,
            [single]$destinationY,
            [single]($cellWidth * $scaleX),
            [single]($cellHeight * $scaleY)
        )

        $cellGraphics.DrawImage($beforeCell, $destination)
    }
    finally {
        $cellGraphics.Dispose()
    }

    $correctedCell.Save($afterCellPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $correctedAtlas = $atlas.Clone(
        [System.Drawing.Rectangle]::new(0, 0, $atlas.Width, $atlas.Height),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $atlasGraphics = [System.Drawing.Graphics]::FromImage($correctedAtlas)

    try {
        $atlasGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $atlasGraphics.DrawImageUnscaled($correctedCell, $cellX, $cellY)
    }
    finally {
        $atlasGraphics.Dispose()
    }

    $unchangedRegions = @(
        [System.Drawing.Rectangle]::new(0, 0, 1536, 1248),
        [System.Drawing.Rectangle]::new(0, 1248, 576, 208),
        [System.Drawing.Rectangle]::new(768, 1248, 768, 208),
        [System.Drawing.Rectangle]::new(0, 1456, 1536, 832)
    )

    for ($index = 0; $index -lt $unchangedRegions.Count; $index++) {
        $beforeHash = Get-RegionHash -Bitmap $atlas -Rectangle $unchangedRegions[$index]
        $afterHash = Get-RegionHash -Bitmap $correctedAtlas -Rectangle $unchangedRegions[$index]

        if ($beforeHash -ne $afterHash) {
            throw "Pixels outside the target cell changed in validation region $index"
        }
    }

    $beforeTargetHash = Get-RegionHash -Bitmap $atlas -Rectangle $cellRectangle
    $afterTargetHash = Get-RegionHash -Bitmap $correctedAtlas -Rectangle $cellRectangle
    if ($beforeTargetHash -eq $afterTargetHash) {
        throw "Target cell did not change"
    }

    if (Test-Path -LiteralPath $outputAtlasPath) {
        Remove-Item -LiteralPath $outputAtlasPath -Force
    }

    $correctedAtlas.Save($outputAtlasPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $correctedAtlas.Dispose()
    $atlas.Dispose()
    $atlas = $null
    Copy-Item -LiteralPath $outputAtlasPath -Destination $atlasPath -Force

    $verifiedAtlas = [System.Drawing.Bitmap]::new($atlasPath)
    try {
        if ($verifiedAtlas.Width -ne 1536 -or $verifiedAtlas.Height -ne 2288) {
            throw "Corrected atlas dimensions changed unexpectedly"
        }

        [pscustomobject]@{
            AtlasWidth = $verifiedAtlas.Width
            AtlasHeight = $verifiedAtlas.Height
            PixelFormat = $verifiedAtlas.PixelFormat.ToString()
            TransparentCornerPreserved = ($correctedCell.GetPixel(0, 0).A -eq 0)
            OutsideTargetRegionsUnchanged = $true
            TargetCellChanged = $true
            ScaleXPercent = ($scaleX - 1.0) * 100
            ScaleYPercent = ($scaleY - 1.0) * 100
        } | ConvertTo-Json -Compress
    }
    finally {
        $verifiedAtlas.Dispose()
    }

    $correctedCell.Dispose()
    $beforeCell.Dispose()
}
finally {
    if ($null -ne $atlas) {
        $atlas.Dispose()
    }
}
