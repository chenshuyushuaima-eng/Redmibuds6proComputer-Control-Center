$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$destination = Join-Path $root 'diagnostics'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$arguments = @('/nologo','/target:exe','/platform:x64',('/out:' + (Join-Path $destination 'RfcommProbe.exe')))
foreach ($name in @('System.Runtime.dll','System.Runtime.WindowsRuntime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Threading.Tasks.dll','System.Collections.dll','System.ObjectModel.dll')) {
    $arguments += '/r:' + (Join-Path $framework $name)
}
foreach ($name in @('Windows.Foundation.winmd','Windows.Devices.winmd','Windows.Networking.winmd','Windows.Storage.winmd')) {
    $arguments += '/r:' + (Join-Path $metadata $name)
}
$arguments += Join-Path $PSScriptRoot 'RfcommProbe.cs'
$arguments += Join-Path $PSScriptRoot 'BudsAuthentication.cs'
$arguments += Join-Path $PSScriptRoot 'WinrtAwaiter.cs'
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed' }
