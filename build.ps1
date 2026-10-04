# SPDX-License-Identifier: AGPL-3.0-or-later
param([string]$OutputDirectory = 'dist', [switch]$PublicRelease)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$destination = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
if ($PublicRelease -and (Test-Path -LiteralPath (Join-Path $destination 'fitness_detect.wav'))) {
    throw 'Public release output contains personal audio. Choose a fresh output directory.'
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:' + (Join-Path $destination 'MiBudsControl.exe')),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')),
    ('/resource:' + (Join-Path $PSScriptRoot 'src\Theme.xaml') + ',Theme.xaml'))
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\tray-white.ico') + ',TrayWhite.ico'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\tray-dark.ico') + ',TrayDark.ico'
foreach ($name in @('System.Runtime.dll','System.Runtime.WindowsRuntime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Threading.Tasks.dll','System.Collections.dll','System.ObjectModel.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Xaml.dll')) {
    $arguments += '/r:' + (Join-Path $framework $name)
}
foreach ($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')) { $arguments += '/r:' + (Join-Path $framework ('WPF\' + $name)) }
foreach ($name in @('Windows.Foundation.winmd','Windows.Devices.winmd','Windows.Networking.winmd','Windows.Storage.winmd')) { $arguments += '/r:' + (Join-Path $metadata $name) }
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$arguments += Join-Path $PSScriptRoot 'tools\BudsAuthentication.cs'
$arguments += Join-Path $PSScriptRoot 'tools\WinrtAwaiter.cs'
$compilerOutput = & (Join-Path $framework 'csc.exe') @arguments 2>&1
$compilerOutput | ForEach-Object { Write-Output $_ }
if ($LASTEXITCODE -ne 0) { throw ('Application compilation failed (csc exit ' + $LASTEXITCODE + ')') }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE'),(Join-Path $PSScriptRoot 'LICENSE.en'),(Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $PSScriptRoot 'AUTHORS.md') -Destination $destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\usage.md') -Destination (Join-Path $destination 'USAGE.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\fit-audio.md') -Destination $destination
$publishedTools = Join-Path $destination 'tools'
New-Item -ItemType Directory -Path $publishedTools -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\extract-fit-audio.ps1') -Destination $publishedTools
$fitAudio = Join-Path $PSScriptRoot 'assets\fitness_detect.wav'
if (-not $PublicRelease -and (Test-Path -LiteralPath $fitAudio)) {
    Copy-Item -LiteralPath $fitAudio -Destination $destination
}
Write-Output (Join-Path $destination 'MiBudsControl.exe')
