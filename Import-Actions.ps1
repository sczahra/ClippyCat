[CmdletBinding(SupportsShouldProcess = $true)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing.Common

function Assert-Valid {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-PetConfig {
    param(
        [object]$Action,
        [string]$PetName
    )

    $property = $Action.pets.PSObject.Properties |
        Where-Object { $_.Name.Equals($PetName, [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1

    Assert-Valid ($null -ne $property) "Action '$($Action.name)' is missing pet configuration for '$PetName'."
    return $property.Value
}

function Get-SafeSourceFolder {
    param(
        [string]$ActionsRoot,
        [string]$RelativeFolder,
        [string]$ActionName,
        [string]$PetName
    )

    Assert-Valid (-not [string]::IsNullOrWhiteSpace($RelativeFolder)) "Action '$ActionName' has no sourceFolder for '$PetName'."

    $fullPath = [IO.Path]::GetFullPath((Join-Path $ActionsRoot $RelativeFolder))
    $safePrefix = $ActionsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-Valid ($fullPath.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) "Action '$ActionName' sourceFolder escapes Assets/actions: $RelativeFolder"
    return $fullPath
}

function Test-FramePng {
    param(
        [string]$Path,
        [int]$CellWidth,
        [int]$CellHeight
    )

    $image = [Drawing.Bitmap]::new($Path)

    try {
        Assert-Valid ($image.RawFormat.Guid -eq [Drawing.Imaging.ImageFormat]::Png.Guid) "Frame is not a PNG: $Path"
        Assert-Valid ([Drawing.Image]::IsAlphaPixelFormat($image.PixelFormat)) "Frame has no alpha channel: $Path"
        Assert-Valid ($image.Width -le $CellWidth -and $image.Height -le $CellHeight) "Frame exceeds ${CellWidth}x${CellHeight}: $Path ($($image.Width)x$($image.Height))"

        $hasVisiblePixel = $false
        $hasTransparentPixel = $false

        for ($y = 0; $y -lt $image.Height -and (-not $hasVisiblePixel -or -not $hasTransparentPixel); $y++) {
            for ($x = 0; $x -lt $image.Width -and (-not $hasVisiblePixel -or -not $hasTransparentPixel); $x++) {
                $alpha = $image.GetPixel($x, $y).A
                if ($alpha -gt 0) { $hasVisiblePixel = $true }
                if ($alpha -lt 255) { $hasTransparentPixel = $true }
            }
        }

        Assert-Valid $hasVisiblePixel "Frame is completely transparent: $Path"
        Assert-Valid $hasTransparentPixel "Frame has no transparent pixels: $Path"
    }
    finally {
        $image.Dispose()
    }
}

function Copy-BitmapPixels {
    param(
        [Drawing.Bitmap]$Source,
        [Drawing.Bitmap]$Destination,
        [Drawing.Rectangle]$SourceRectangle,
        [Drawing.Point]$DestinationPoint
    )

    $destinationRectangle = [Drawing.Rectangle]::new(
        $DestinationPoint.X,
        $DestinationPoint.Y,
        $SourceRectangle.Width,
        $SourceRectangle.Height
    )
    $pixelFormat = [Drawing.Imaging.PixelFormat]::Format32bppArgb
    $sourceData = $Source.LockBits($SourceRectangle, [Drawing.Imaging.ImageLockMode]::ReadOnly, $pixelFormat)
    $destinationData = $Destination.LockBits($destinationRectangle, [Drawing.Imaging.ImageLockMode]::WriteOnly, $pixelFormat)

    try {
        $rowBytes = $SourceRectangle.Width * 4
        $buffer = [byte[]]::new($rowBytes)

        for ($row = 0; $row -lt $SourceRectangle.Height; $row++) {
            $sourcePointer = [IntPtr]::Add($sourceData.Scan0, $row * $sourceData.Stride)
            $destinationPointer = [IntPtr]::Add($destinationData.Scan0, $row * $destinationData.Stride)
            [Runtime.InteropServices.Marshal]::Copy($sourcePointer, $buffer, 0, $rowBytes)
            [Runtime.InteropServices.Marshal]::Copy($buffer, 0, $destinationPointer, $rowBytes)
        }
    }
    finally {
        $Source.UnlockBits($sourceData)
        $Destination.UnlockBits($destinationData)
    }
}

function Get-BitmapHash {
    param(
        [Drawing.Bitmap]$Bitmap,
        [Drawing.Rectangle]$Rectangle
    )

    $pixelFormat = [Drawing.Imaging.PixelFormat]::Format32bppArgb
    $data = $Bitmap.LockBits($Rectangle, [Drawing.Imaging.ImageLockMode]::ReadOnly, $pixelFormat)
    $sha = [Security.Cryptography.SHA256]::Create()

    try {
        $rowBytes = $Rectangle.Width * 4
        $buffer = [byte[]]::new($rowBytes)

        for ($row = 0; $row -lt $Rectangle.Height; $row++) {
            $pointer = [IntPtr]::Add($data.Scan0, $row * $data.Stride)
            [Runtime.InteropServices.Marshal]::Copy($pointer, $buffer, 0, $rowBytes)
            $null = $sha.TransformBlock($buffer, 0, $rowBytes, $buffer, 0)
        }

        $null = $sha.TransformFinalBlock([byte[]]::new(0), 0, 0)
        return [Convert]::ToHexString($sha.Hash)
    }
    finally {
        $sha.Dispose()
        $Bitmap.UnlockBits($data)
    }
}

function Update-PetAtlas {
    param(
        [string]$ProjectRoot,
        [string]$ActionsRoot,
        [object]$Manifest,
        [string]$PetName,
        [string]$RunStamp,
        [bool]$DryRun
    )

    $atlasPath = Join-Path $ProjectRoot "Assets/$PetName/spritesheet.png"
    Assert-Valid (Test-Path -LiteralPath $atlasPath -PathType Leaf) "Atlas not found: $atlasPath"

    $atlasBytes = [IO.File]::ReadAllBytes($atlasPath)
    $atlasStream = [IO.MemoryStream]::new($atlasBytes, $false)
    $atlas = [Drawing.Bitmap]::new($atlasStream)
    $output = $null

    try {
        $cellWidth = [int]$Manifest.cellWidth
        $cellHeight = [int]$Manifest.cellHeight
        $atlasColumns = [int]$Manifest.atlasColumns
        $baseRowCount = [int]$Manifest.baseRowCount
        $atlasWidth = $cellWidth * $atlasColumns

        Assert-Valid ($atlas.Width -eq $atlasWidth) "$PetName atlas width must be $atlasWidth, found $($atlas.Width)."
        Assert-Valid ($atlas.Height % $cellHeight -eq 0) "$PetName atlas height is not a multiple of $cellHeight."
        Assert-Valid ([Drawing.Image]::IsAlphaPixelFormat($atlas.PixelFormat)) "$PetName atlas has no alpha channel."

        $currentRows = [int]($atlas.Height / $cellHeight)
        Assert-Valid ($currentRows -ge $baseRowCount) "$PetName atlas has fewer than $baseRowCount protected base rows."

        $assignedRows = @{}

        foreach ($action in $Manifest.actions) {
            $pet = Get-PetConfig $action $PetName
            if ($null -ne $pet.atlasRow) {
                $row = [int]$pet.atlasRow
                Assert-Valid ($row -ge $baseRowCount) "Action '$($action.name)' attempts to use protected $PetName row $row."
                Assert-Valid (-not $assignedRows.ContainsKey($row)) "$PetName row $row is assigned to more than one action."
                $assignedRows[$row] = $action.name
            }
        }

        for ($row = $baseRowCount; $row -lt $currentRows; $row++) {
            Assert-Valid ($assignedRows.ContainsKey($row)) "$PetName atlas contains undeclared dedicated row $row; refusing to overwrite it."
        }

        $targetRows = $baseRowCount
        if ($assignedRows.Count -gt 0) {
            $targetRows = [Math]::Max($targetRows, ([int](($assignedRows.Keys | Measure-Object -Maximum).Maximum) + 1))
        }

        $output = [Drawing.Bitmap]::new($atlasWidth, $targetRows * $cellHeight, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($output)
        try { $graphics.Clear([Drawing.Color]::Transparent) }
        finally { $graphics.Dispose() }

        $baseRectangle = [Drawing.Rectangle]::new(0, 0, $atlasWidth, $baseRowCount * $cellHeight)
        Copy-BitmapPixels $atlas $output $baseRectangle ([Drawing.Point]::Empty)

        foreach ($action in $Manifest.actions) {
            $pet = Get-PetConfig $action $PetName
            if (-not [bool]$pet.enabled) { continue }

            $sourceFolder = Get-SafeSourceFolder $ActionsRoot ([string]$pet.sourceFolder) ([string]$action.name) $PetName
            $targetRow = [int]$pet.atlasRow

            for ($index = 0; $index -lt [int]$action.frameCount; $index++) {
                $framePath = Join-Path $sourceFolder "$index.png"
                $frame = [Drawing.Bitmap]::new($framePath)
                try {
                    $x = ($index * $cellWidth) + [int](($cellWidth - $frame.Width) / 2)
                    $y = ($targetRow * $cellHeight) + [int](($cellHeight - $frame.Height) / 2)
                    $sourceRectangle = [Drawing.Rectangle]::new(0, 0, $frame.Width, $frame.Height)
                    Copy-BitmapPixels $frame $output $sourceRectangle ([Drawing.Point]::new($x, $y))
                }
                finally {
                    $frame.Dispose()
                }
            }
        }

        $samePixels = $atlas.Width -eq $output.Width -and $atlas.Height -eq $output.Height
        if ($samePixels) {
            $fullRectangle = [Drawing.Rectangle]::new(0, 0, $atlas.Width, $atlas.Height)
            $samePixels = (Get-BitmapHash $atlas $fullRectangle) -eq (Get-BitmapHash $output $fullRectangle)
        }

        if ($samePixels) {
            Write-Host "  ${PetName}: unchanged; no backup or write needed."
            return $false
        }

        if ($DryRun) {
            Write-Host "  ${PetName}: would rebuild atlas to $($output.Width)x$($output.Height); no files written." -ForegroundColor Cyan
            return $true
        }

        $backupRoot = Join-Path $ProjectRoot "Assets/backups"
        if (-not (Test-Path -LiteralPath $backupRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $backupRoot | Out-Null
        }

        $backupPath = Join-Path $backupRoot "$PetName-spritesheet-$RunStamp.png"
        Copy-Item -LiteralPath $atlasPath -Destination $backupPath
        Write-Host "  ${PetName}: backup created at $backupPath"

        $temporaryPath = Join-Path (Split-Path -Parent $atlasPath) "spritesheet.actions-tmp.png"
        try {
            $output.Save($temporaryPath, [Drawing.Imaging.ImageFormat]::Png)
            $saved = [Drawing.Bitmap]::new($temporaryPath)
            try {
                $savedBaseHash = Get-BitmapHash $saved $baseRectangle
                $originalBaseHash = Get-BitmapHash $atlas $baseRectangle
                Assert-Valid ($savedBaseHash -eq $originalBaseHash) "$PetName protected base rows changed during PNG encoding; atlas not replaced."
            }
            finally {
                $saved.Dispose()
            }

            [IO.File]::Move($temporaryPath, $atlasPath, $true)
        }
        finally {
            if (Test-Path -LiteralPath $temporaryPath) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }

        Write-Host "  ${PetName}: rebuilt $($output.Width)x$($output.Height) atlas."
        return $true
    }
    finally {
        if ($null -ne $output) { $output.Dispose() }
        $atlas.Dispose()
        $atlasStream.Dispose()
    }
}

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$actionsRoot = Join-Path $projectRoot "Assets/actions"
$manifestPath = Join-Path $actionsRoot "actions.json"
$petNames = @("Marmalade", "Merry")
$dryRun = [bool]$WhatIfPreference
$runStamp = Get-Date -Format "yyyyMMdd-HHmmssfff"

Write-Host "[1/5] Locating action manifest..."
Assert-Valid (Test-Path -LiteralPath $manifestPath -PathType Leaf) "Action manifest not found: $manifestPath"

Write-Host "[2/5] Validating manifest..."
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
Assert-Valid ([int]$manifest.schemaVersion -eq 1) "Unsupported action manifest schemaVersion: $($manifest.schemaVersion)"
Assert-Valid ([int]$manifest.cellWidth -gt 0 -and [int]$manifest.cellHeight -gt 0) "Cell dimensions must be positive."
Assert-Valid ([int]$manifest.atlasColumns -gt 0) "atlasColumns must be positive."
Assert-Valid ([int]$manifest.baseRowCount -gt 0) "baseRowCount must be positive."
Assert-Valid ($manifest.actions.Count -gt 0) "Manifest contains no actions."

$actionNames = @{}
$rowAssignments = @{}
$autonomousTotals = @{}
foreach ($petName in $petNames) { $autonomousTotals[$petName] = 0 }

foreach ($action in $manifest.actions) {
    $actionName = [string]$action.name
    Assert-Valid (-not [string]::IsNullOrWhiteSpace($actionName)) "Action name cannot be empty."
    Assert-Valid (-not $actionNames.ContainsKey($actionName.ToLowerInvariant())) "Duplicate action name: $actionName"
    $actionNames[$actionName.ToLowerInvariant()] = $true

    $enabledCount = 0
    foreach ($petName in $petNames) {
        $pet = Get-PetConfig $action $petName
        if ($null -ne $pet.atlasRow) {
            $row = [int]$pet.atlasRow
            Assert-Valid ($row -ge [int]$manifest.baseRowCount) "Action '$actionName' attempts to use protected $petName row $row."
            $rowKey = "$petName|$row"
            Assert-Valid (-not $rowAssignments.ContainsKey($rowKey)) "$petName row $row is assigned more than once."
            $rowAssignments[$rowKey] = $actionName
        }

        if ([bool]$pet.enabled) {
            $enabledCount++
            Assert-Valid ($null -ne $pet.atlasRow) "Enabled action '$actionName' has no atlasRow for '$petName'."
            $autonomousTotals[$petName] += [int]$action.autonomousWeight
        }
    }

    Assert-Valid ([int]$action.autonomousWeight -ge 0) "Action '$actionName' has a negative autonomousWeight."
    if ($enabledCount -gt 0) {
        Assert-Valid ([int]$action.frameCount -gt 0) "Enabled action '$actionName' must have a positive frameCount."
        Assert-Valid ([int]$action.frameCount -le [int]$manifest.atlasColumns) "Action '$actionName' frameCount exceeds atlasColumns."
        Assert-Valid ([int]$action.frameMs -gt 0) "Enabled action '$actionName' must have positive frameMs."
        Assert-Valid ([int]$action.minDurationMs -gt 0 -and [int]$action.maxDurationMs -gt [int]$action.minDurationMs) "Enabled action '$actionName' has invalid duration bounds."
    }
}

foreach ($petName in $petNames) {
    Assert-Valid ($autonomousTotals[$petName] -le 12) "$petName autonomous action weights exceed the smallest available Idle bucket (12)."
}

Write-Host "[3/5] Validating enabled source frames..."
foreach ($action in $manifest.actions) {
    foreach ($petName in $petNames) {
        $pet = Get-PetConfig $action $petName
        $sourceFolder = Get-SafeSourceFolder $actionsRoot ([string]$pet.sourceFolder) ([string]$action.name) $petName

        if (-not [bool]$pet.enabled) {
            if (-not (Test-Path -LiteralPath $sourceFolder -PathType Container)) {
                Write-Host "  $petName/$($action.name): pending; source folder is absent."
            }
            continue
        }

        Assert-Valid (Test-Path -LiteralPath $sourceFolder -PathType Container) "Enabled action source folder not found: $sourceFolder"
        $pngFiles = @(Get-ChildItem -LiteralPath $sourceFolder -File -Filter "*.png")
        Assert-Valid ($pngFiles.Count -eq [int]$action.frameCount) "$petName/$($action.name) requires $($action.frameCount) PNG files, found $($pngFiles.Count)."

        for ($index = 0; $index -lt [int]$action.frameCount; $index++) {
            $framePath = Join-Path $sourceFolder "$index.png"
            Assert-Valid (Test-Path -LiteralPath $framePath -PathType Leaf) "$petName/$($action.name) is missing frame $index.png."
            Test-FramePng $framePath ([int]$manifest.cellWidth) ([int]$manifest.cellHeight)
        }

        Write-Host "  $petName/$($action.name): $($action.frameCount) frames valid."
    }
}

Write-Host "[4/5] $($(if ($dryRun) { 'Planning' } else { 'Rebuilding' })) declared action rows..."
$changedCount = 0
foreach ($petName in $petNames) {
    if (Update-PetAtlas $projectRoot $actionsRoot $manifest $petName $runStamp $dryRun) {
        $changedCount++
    }
}

Write-Host "[5/5] Action import complete."
if ($dryRun) {
    Write-Host "Dry run validated all inputs; $changedCount atlas(es) would change. No files were written." -ForegroundColor Green
}
else {
    Write-Host "$changedCount atlas(es) changed; unchanged atlases were left untouched and unbacked up." -ForegroundColor Green
}
