# SPDX-License-Identifier: AGPL-3.0-or-later
param(
    [Parameter(Mandatory = $true)][string]$ApkPath,
    [string]$OutputDirectory = 'assets',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$apk = (Resolve-Path -LiteralPath $ApkPath).Path
$destination = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectRoot $OutputDirectory }
$target = Join-Path $destination 'fitness_detect.wav'
if ((Test-Path -LiteralPath $target) -and -not $Force) {
    throw 'fitness_detect.wav already exists. Use -Force to replace it.'
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($apk)
try {
    # Known raw/fitness_detect entry in com.mi.earphone 1.38.0.
    $entry = $archive.GetEntry('res/Pir.wav')
    if ($null -eq $entry) {
        throw 'This APK does not contain res/Pir.wav. Supported reference: Xiaomi Earbuds 1.38.0.'
    }
    $source = $entry.Open()
    try {
        $buffer = New-Object IO.MemoryStream
        try {
            $source.CopyTo($buffer)
            $bytes = $buffer.ToArray()
        } finally { $buffer.Dispose() }
    } finally { $source.Dispose() }
    if ($bytes.Length -lt 12 -or [Text.Encoding]::ASCII.GetString($bytes,0,4) -ne 'RIFF' -or [Text.Encoding]::ASCII.GetString($bytes,8,4) -ne 'WAVE') {
        throw 'The extracted resource is not a WAV file.'
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    [IO.File]::WriteAllBytes($target,$bytes)
    Write-Output $target
} finally { $archive.Dispose() }
