param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot '..\assets\icon-source'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\assets')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Get-ContentBounds([System.Drawing.Bitmap]$bitmap) {
    $minX = $bitmap.Width; $minY = $bitmap.Height; $maxX = -1; $maxY = -1
    $rect = New-Object System.Drawing.Rectangle(0,0,$bitmap.Width,$bitmap.Height)
    $data = $bitmap.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $buffer = New-Object byte[] ($stride * $bitmap.Height)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0,$buffer,0,$buffer.Length)
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            $row = $y * $stride
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                if ($buffer[$row + $x * 4 + 3] -gt 8) {
                    if ($x -lt $minX) { $minX = $x }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }
    } finally { $bitmap.UnlockBits($data) }
    if ($maxX -lt 0) { throw 'Source image has no visible pixels.' }
    return New-Object System.Drawing.Rectangle($minX,$minY,($maxX - $minX + 1),($maxY - $minY + 1))
}

function New-SquareCanvas([string]$path) {
    $source = New-Object System.Drawing.Bitmap($path)
    try {
        $bounds = Get-ContentBounds $source
        $side = [Math]::Max($bounds.Width,$bounds.Height)
        $side = [int][Math]::Ceiling($side * 1.18)
        $left = [int]($bounds.X + $bounds.Width / 2 - $side / 2)
        $top = [int]($bounds.Y + $bounds.Height / 2 - $side / 2)
        $canvas = New-Object System.Drawing.Bitmap($side,$side,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage($source,(New-Object System.Drawing.Rectangle(0,0,$side,$side)),(New-Object System.Drawing.Rectangle($left,$top,$side,$side)),[System.Drawing.GraphicsUnit]::Pixel)
        } finally { $graphics.Dispose() }
        return $canvas
    } finally { $source.Dispose() }
}

function New-ScaledBitmap([System.Drawing.Bitmap]$source,[int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($source,(New-Object System.Drawing.Rectangle(0,0,$size,$size)))
    } finally { $graphics.Dispose() }
    return $bitmap
}

function Get-DibBytes([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $rect = New-Object System.Drawing.Rectangle(0,0,$size,$size)
    $data = $bitmap.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $buffer = New-Object byte[] ($stride * $size)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0,$buffer,0,$buffer.Length)
    } finally { $bitmap.UnlockBits($data) }
    $maskStride = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size * 2))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32]($size * $size * 4 + $maskStride * $size))
    $writer.Write([int32]0); $writer.Write([int32]0); $writer.Write([uint32]0); $writer.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = $y * $stride
        for ($x = 0; $x -lt $size; $x++) {
            $offset = $row + $x * 4
            $writer.Write($buffer[$offset + 2])
            $writer.Write($buffer[$offset + 1])
            $writer.Write($buffer[$offset])
            $writer.Write($buffer[$offset + 3])
        }
    }
    $mask = New-Object byte[] ($maskStride * $size)
    $writer.Write($mask)
    $writer.Flush()
    return $stream.ToArray()
}

function Write-Icon([System.Drawing.Bitmap]$canvas,[string]$path,[int[]]$sizes) {
    $images = @()
    foreach ($size in $sizes) {
        $scaled = New-ScaledBitmap $canvas $size
        try {
            if ($size -ge 256) {
                $stream = New-Object System.IO.MemoryStream
                $scaled.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
                $images += ,@{ Size = $size; Bytes = $stream.ToArray(); Png = $true }
                $stream.Dispose()
            } else {
                $images += ,@{ Size = $size; Bytes = (Get-DibBytes $scaled); Png = $false }
            }
        } finally { $scaled.Dispose() }
    }
    $stream = New-Object System.IO.FileStream($path,[System.IO.FileMode]::Create,[System.IO.FileAccess]::Write)
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
        $offset = 6 + $images.Count * 16
        foreach ($image in $images) {
            $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }
        foreach ($image in $images) { $writer.Write($image.Bytes) }
    } finally { $writer.Dispose(); $stream.Dispose() }
}

$sizes = @(16,20,24,32,40,48,64,128,256)
$jobs = @(
    @{ Source = 'black-lines.png';  Target = 'tray-dark.ico' },
    @{ Source = 'white-lines.png';  Target = 'tray-white.ico' }
)
foreach ($job in $jobs) {
    $sourcePath = Join-Path $SourceDirectory $job.Source
    $targetPath = Join-Path $OutputDirectory $job.Target
    $canvas = New-SquareCanvas $sourcePath
    try { Write-Icon $canvas $targetPath $sizes } finally { $canvas.Dispose() }
    Write-Output ($job.Target + ' <- ' + $job.Source + ' (' + (Get-Item $targetPath).Length + ' bytes)')
}
