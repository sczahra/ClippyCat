$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$targets = @(
    @{ Row = 9; Frame = 6 },
    @{ Row = 10; Frame = 5 },
    @{ Row = 8; Frame = 0 }
)

foreach ($target in $targets) {
    foreach ($pet in @("Marmalade", "Merry")) {
        $atlasPath = Join-Path $repoRoot "Assets\$pet\spritesheet.png"
        $atlas = [System.Drawing.Bitmap]::new($atlasPath)

        try {
            $rectangle = [System.Drawing.Rectangle]::new(
                $target.Frame * 192,
                $target.Row * 208,
                192,
                208
            )
            $cell = $atlas.Clone($rectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

            try {
                $outputPath = Join-Path $PSScriptRoot "$pet-row$($target.Row)-frame$($target.Frame).png"
                $cell.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $cell.Dispose()
            }
        }
        finally {
            $atlas.Dispose()
        }
    }
}
