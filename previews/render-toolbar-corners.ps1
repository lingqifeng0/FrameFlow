$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# New drawings from the player's layout and fonts, not edits of a screenshot.
$previewPath = Join-Path $PSScriptRoot 'window-0096ff-slots-preview.png'
$barPreviewPath = Join-Path $PSScriptRoot 'header-0096ff-controls-preview.png'
$footerPreviewPath = Join-Path $PSScriptRoot 'footer-0096ff-slots-preview.png'
$capacityPath = Join-Path $PSScriptRoot 'toolbar-slot-allocation.json'
$toolbarSlotCapacity = 16
$occupiedSlots = @('frame-back', 'frame-forward', 'screenshot', 'bookmark', 'ab-loop', 'timestamp')
if ($occupiedSlots.Count -gt $toolbarSlotCapacity) { throw 'Remove an occupied slot before adding another toolbar button.' }
$previewDpi = 168 # Matches the user's verified 175% Windows scaling.
$bitmap = [System.Drawing.Bitmap]::new(1280, 800)
$bitmap.SetResolution($previewDpi, $previewDpi)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::FromArgb(48, 53, 63))

function New-RoundedPath([System.Drawing.RectangleF]$rect, [single]$radius) {
    $diameter = $radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($rect.Left, $rect.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rect.Left, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$blueBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0, 150, 255)) # #0096FF
$frameBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(13, 15, 19))
$whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$mutedBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(151, 162, 180))
$activeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(33, 54, 85))
$titleFont = [System.Drawing.Font]::new('Microsoft YaHei UI', 10, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$glyphFont = [System.Drawing.Font]::new('Segoe MDL2 Assets', 26, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$headerGlyphFont = [System.Drawing.Font]::new('Segoe MDL2 Assets', 24, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$windowGlyphFont = [System.Drawing.Font]::new('Segoe MDL2 Assets', 18, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$bodyFont = [System.Drawing.Font]::new('Microsoft YaHei UI', 9, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$leftFormat = [System.Drawing.StringFormat]::new()
$leftFormat.Alignment = [System.Drawing.StringAlignment]::Near
$leftFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
$leftFormat.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap
$leftFormat.Trimming = [System.Drawing.StringTrimming]::EllipsisCharacter
$centerFormat = [System.Drawing.StringFormat]::new($leftFormat)
$centerFormat.Alignment = [System.Drawing.StringAlignment]::Center

function Draw-Glyph([System.Drawing.Graphics]$target, [int]$code, [System.Drawing.RectangleF]$rect, [System.Drawing.Font]$font = $glyphFont) {
    $target.DrawString([string][char]$code, $font, $whiteBrush, $rect, $centerFormat)
}

function Draw-VideoSkip([System.Drawing.Graphics]$target, [int]$direction, [System.Drawing.RectangleF]$rect) {
    $cx = $rect.Left + $rect.Width / 2
    $cy = $rect.Top + $rect.Height / 2
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($cx - 8 * $direction, $cy - 9),
        [System.Drawing.PointF]::new($cx + 5 * $direction, $cy),
        [System.Drawing.PointF]::new($cx - 8 * $direction, $cy + 9)
    )
    $target.FillPolygon($whiteBrush, $points)
    $barX = if ($direction -gt 0) { $cx + 8 } else { $cx - 11 }
    $target.FillRectangle($whiteBrush, [System.Drawing.RectangleF]::new($barX, $cy - 9, 3, 18))
}

function Draw-FrameStep([System.Drawing.Graphics]$target, [int]$direction, [System.Drawing.RectangleF]$rect) {
    $cx = $rect.Left + $rect.Width / 2
    $cy = $rect.Top + $rect.Height / 2
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 1.4)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $frame = New-RoundedPath ([System.Drawing.RectangleF]::new($cx - 12, $cy - 9, 24, 18)) 2
    $target.DrawPath($pen, $frame)
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($cx - 4 * $direction, $cy - 5),
        [System.Drawing.PointF]::new($cx + 3 * $direction, $cy),
        [System.Drawing.PointF]::new($cx - 4 * $direction, $cy + 5)
    )
    $target.DrawPolygon($pen, $points)
    $target.DrawLine($pen, [single]($cx + 7 * $direction), [single]($cy - 5), [single]($cx + 7 * $direction), [single]($cy + 5))
    $frame.Dispose()
    $pen.Dispose()
}

function Draw-Header([System.Drawing.Graphics]$target, [System.Drawing.RectangleF]$bar) {
    $headerPath = New-RoundedPath $bar ([single][Math]::Round(10 * $previewDpi / 96))
    $target.FillPath($blueBrush, $headerPath)
    $state = $target.Save()
    $target.SetClip($headerPath)
    $titleRect = [System.Drawing.RectangleF]::new($bar.Left + 17, $bar.Top, $bar.Width - 394, 44)
    $target.DrawString('硬件工程师培训02-硬件研发流程_ev.mp4', $titleFont, $whiteBrush, $titleRect, $leftFormat)
    $glyphs = @(0xE946, 0xE8E5, 0xE8FD, 0xE718, 0xE713)
    $firstX = $bar.Right - 360
    for ($index = 0; $index -lt $glyphs.Count; $index++) {
        Draw-Glyph $target $glyphs[$index] ([System.Drawing.RectangleF]::new($firstX + $index * 42, $bar.Top + 4, 42, 36)) $headerGlyphFont
    }
    $windowStart = $bar.Right - 124
    $divider = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(96, 190, 255), 1)
    $target.DrawLine($divider, [single]($windowStart - 10), [single]($bar.Top + 12), [single]($windowStart - 10), [single]($bar.Bottom - 12))
    $windowGlyphs = @(0xE921, 0xE922, 0xE8BB)
    for ($index = 0; $index -lt $windowGlyphs.Count; $index++) {
        Draw-Glyph $target $windowGlyphs[$index] ([System.Drawing.RectangleF]::new($windowStart + $index * 36, $bar.Top + 4, 36, 36)) $windowGlyphFont
    }
    $divider.Dispose()
    $target.Restore($state)
    $headerPath.Dispose()
}

function Draw-Footer([System.Drawing.Graphics]$target, [System.Drawing.RectangleF]$footer) {
    # Reserve geometry only. Empty slots have no control, hover state, or paint.
    $slotWidth = 44
    $customWidth = $footer.Width - 460
    $groupGap = 14
    $fittingSlots = [int][Math]::Floor(($customWidth - $groupGap) / $slotWidth)
    if ($fittingSlots -lt $toolbarSlotCapacity) { throw 'The preview width must fit all reserved custom slots.' }
    $script:capacityMetadata = [ordered]@{
        previewWindowWidth = 1280
        toolbarCapacity = $toolbarSlotCapacity
        occupiedCount = $occupiedSlots.Count
        remainingCount = $toolbarSlotCapacity - $occupiedSlots.Count
        occupiedSlots = $occupiedSlots
        customAreaWidth = $customWidth
        slotWidth = $slotWidth
        groupGap = $groupGap
        widthAtCapacity = $toolbarSlotCapacity * $slotWidth + $groupGap
        widthWithOneExtra = ($toolbarSlotCapacity + 1) * $slotWidth + $groupGap
        emptySlotsPainted = $false
        separatorsBetweenOccupiedButtonsOnly = $true
        fullCapacityRule = 'Remove an occupied custom button before adding a new one.'
        narrowWindowRule = 'Keep the 16-slot configuration and put temporarily hidden buttons in More.'
    }
    $progressY = $footer.Top + 14
    $target.DrawString('00:03:10', $bodyFont, $mutedBrush, [System.Drawing.RectangleF]::new($footer.Left + 14, $footer.Top + 1, 98, 28), $leftFormat)
    $target.DrawString('00:36:31', $bodyFont, $mutedBrush, [System.Drawing.RectangleF]::new($footer.Right - 114, $footer.Top + 1, 110, 28), $leftFormat)
    $trackPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(57, 63, 74), 3)
    $playedPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(111, 167, 255), 3)
    $target.DrawLine($trackPen, [single]($footer.Left + 116), [single]$progressY, [single]($footer.Right - 130), [single]$progressY)
    $target.DrawLine($playedPen, [single]($footer.Left + 116), [single]$progressY, [single]($footer.Left + 201), [single]$progressY)
    $target.FillEllipse($whiteBrush, [System.Drawing.RectangleF]::new($footer.Left + 197, $progressY - 4, 8, 8))
    $rowY = $footer.Top + 41
    $firstX = $footer.Left + 14
    $playPath = New-RoundedPath ([System.Drawing.RectangleF]::new($firstX, $rowY, 40, 36)) 7
    $target.FillPath($activeBrush, $playPath)
    Draw-Glyph $target 0xE768 ([System.Drawing.RectangleF]::new($firstX, $rowY, 40, 36))
    Draw-VideoSkip $target -1 ([System.Drawing.RectangleF]::new($firstX + 44, $rowY, 40, 36))
    Draw-VideoSkip $target 1 ([System.Drawing.RectangleF]::new($firstX + 88, $rowY, 40, 36))
    $target.DrawString('1.00 ×', $bodyFont, $whiteBrush, [System.Drawing.RectangleF]::new($firstX + 142, $rowY, 78, 36), $centerFormat)
    Draw-FrameStep $target -1 ([System.Drawing.RectangleF]::new($firstX + 242, $rowY, 40, 36))
    Draw-FrameStep $target 1 ([System.Drawing.RectangleF]::new($firstX + 286, $rowY, 40, 36))
    $tools = @(0xE722, 0xE734, 0xE8EE, 0xE8C8)
    for ($index = 0; $index -lt $tools.Count; $index++) {
        Draw-Glyph $target $tools[$index] ([System.Drawing.RectangleF]::new($firstX + 344 + $index * 44, $rowY, 40, 36))
    }
    $rightX = $footer.Right - 190
    $rightTools = @(0xE8F2, 0xE767, 0xE740, 0xE70F)
    for ($index = 0; $index -lt $rightTools.Count; $index++) {
        Draw-Glyph $target $rightTools[$index] ([System.Drawing.RectangleF]::new($rightX + $index * 44, $rowY, 40, 36))
    }
    $separatorPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(49, 56, 68), 1)
    foreach ($offset in @(42, 86, 284, 386, 430, 474)) {
        $target.DrawLine($separatorPen, [single]($firstX + $offset), [single]($rowY + 7), [single]($firstX + $offset), [single]($rowY + 29))
    }
    foreach ($offset in @(42, 86, 130)) {
        $target.DrawLine($separatorPen, [single]($rightX + $offset), [single]($rowY + 7), [single]($rightX + $offset), [single]($rowY + 29))
    }
    $groupPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(75, 83, 98), 1)
    # Do not draw a divider before the right controls across the unused area.
    foreach ($x in @(($firstX + 132), ($firstX + 230), ($firstX + 336))) {
        $target.DrawLine($groupPen, [single]$x, [single]($rowY + 4), [single]$x, [single]($rowY + 32))
    }
    $groupPen.Dispose()
    $separatorPen.Dispose()
    $playPath.Dispose()
    $playedPen.Dispose()
    $trackPen.Dispose()
}

# Ordinary window: round all four outer corners, including both bottom corners.
$window = [System.Drawing.RectangleF]::new(24, 24, 1232, 752)
$windowPath = New-RoundedPath $window 18
$graphics.FillPath($frameBrush, $windowPath)
$outerState = $graphics.Save()
$graphics.SetClip($windowPath)
$header = [System.Drawing.RectangleF]::new(31, 31, 1218, 44)
Draw-Header $graphics $header
$graphics.FillRectangle([System.Drawing.Brushes]::Black, 31, 75, 1218, 607)

Draw-Footer $graphics ([System.Drawing.RectangleF]::new(31, 682, 1218, 87))
$graphics.Restore($outerState)
$bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)

# A second independent drawing lets the title be viewed at its intended size.
$barBitmap = [System.Drawing.Bitmap]::new(1242, 68)
$barBitmap.SetResolution($previewDpi, $previewDpi)
$barGraphics = [System.Drawing.Graphics]::FromImage($barBitmap)
$barGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$barGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$barGraphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$barGraphics.Clear([System.Drawing.Color]::FromArgb(13, 15, 19))
Draw-Header $barGraphics ([System.Drawing.RectangleF]::new(12, 12, 1218, 44))
$barBitmap.Save($barPreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$barGraphics.Dispose()
$barBitmap.Dispose()

$footerBitmap = [System.Drawing.Bitmap]::new(1242, 111)
$footerBitmap.SetResolution($previewDpi, $previewDpi)
$footerGraphics = [System.Drawing.Graphics]::FromImage($footerBitmap)
$footerGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$footerGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$footerGraphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$footerGraphics.Clear([System.Drawing.Color]::FromArgb(13, 15, 19))
Draw-Footer $footerGraphics ([System.Drawing.RectangleF]::new(12, 12, 1218, 87))
$footerBitmap.Save($footerPreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$footerGraphics.Dispose()
$footerBitmap.Dispose()

$windowPath.Dispose()
$centerFormat.Dispose()
$leftFormat.Dispose()
$bodyFont.Dispose()
$glyphFont.Dispose()
$headerGlyphFont.Dispose()
$windowGlyphFont.Dispose()
$titleFont.Dispose()
$activeBrush.Dispose()
$mutedBrush.Dispose()
$whiteBrush.Dispose()
$frameBrush.Dispose()
$blueBrush.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
$capacityMetadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $capacityPath -Encoding utf8
Write-Output $previewPath
Write-Output $barPreviewPath
Write-Output $footerPreviewPath
Write-Output $capacityPath
