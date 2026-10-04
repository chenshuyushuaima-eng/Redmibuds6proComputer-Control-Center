# SPDX-License-Identifier: AGPL-3.0-or-later
param([string]$Version = '', [string]$OutputDirectory = 'release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version.' }
$output = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectRoot $OutputDirectory }))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$workDirectory = Join-Path $output ('staging-' + [Guid]::NewGuid().ToString('N'))
$sourceName = 'MiBudsControl-' + $Version + '-source'
$windowsName = 'MiBudsControl-' + $Version + '-windows-x64'
$sourceDirectory = Join-Path $workDirectory $sourceName
$windowsDirectory = Join-Path $workDirectory $windowsName
New-Item -ItemType Directory -Path $sourceDirectory,$windowsDirectory | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-PackageZip([string]$directory,[string]$zipPath,[string]$entryRoot) {
    $zipStream = [IO.File]::Open($zipPath,[IO.FileMode]::Create,[IO.FileAccess]::Write)
    try {
        $zip = New-Object IO.Compression.ZipArchive($zipStream,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File -Force) {
                $relative = $file.FullName.Substring($directory.Length + 1).Replace('\','/')
                $entryName = $entryRoot + '/' + $relative
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$entryName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $zip.Dispose() }
    } finally { $zipStream.Dispose() }
}

try {
    # Explicit source list excludes personal inputs, logs, generated files and old releases.
    $rootFiles = @('.gitignore','.gitattributes','VERSION','README.md','CHANGELOG.md','CONTRIBUTING.md','AUTHORS.md','LICENSE','LICENSE.en','THIRD_PARTY_NOTICES.md','build.ps1')
    foreach ($name in $rootFiles) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $sourceDirectory
    }
    $extensions = @{
        '.github' = @('.yml','.yaml','.md')
        'assets' = @('.ico','.png')
        'docs' = @('.md','.json')
        'src' = @('.cs','.csproj','.xaml','.manifest')
        'tools' = @('.cs','.ps1')
    }
    foreach ($folder in $extensions.Keys) {
        $folderPath = Join-Path $projectRoot $folder
        foreach ($file in Get-ChildItem -LiteralPath $folderPath -Recurse -File -Force) {
            $relative = $file.FullName.Substring($projectRoot.Length + 1)
            if ($relative -match '(?:^|[\\/])(?:bin|obj|diagnostics)(?:[\\/]|$)' -or $extensions[$folder] -notcontains $file.Extension.ToLowerInvariant()) { continue }
            $target = Join-Path $sourceDirectory $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
    & (Join-Path $sourceDirectory 'build.ps1') -OutputDirectory $windowsDirectory -PublicRelease
    $sourceZip = Join-Path $output ($sourceName + '.zip')
    $windowsZip = Join-Path $output ($windowsName + '.zip')
    Write-PackageZip $sourceDirectory $sourceZip $sourceName
    Write-PackageZip $windowsDirectory $windowsZip $windowsName
    $checksums = foreach ($path in @($sourceZip,$windowsZip)) {
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $hash + '  ' + [IO.Path]::GetFileName($path)
    }
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),$checksums,[Text.Encoding]::ASCII)
    Write-Output $sourceZip
    Write-Output $windowsZip
} finally {
    # Check the absolute path before removing this script's temporary staging directory.
    $workFullPath = [IO.Path]::GetFullPath($workDirectory)
    $outputPrefix = $output.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $workFullPath.StartsWith($outputPrefix,[StringComparison]::OrdinalIgnoreCase) -or $workFullPath -eq $output) {
        throw 'Refusing cleanup outside the package output directory.'
    }
    if (Test-Path -LiteralPath $workFullPath) { Remove-Item -LiteralPath $workFullPath -Recurse -Force }
}
