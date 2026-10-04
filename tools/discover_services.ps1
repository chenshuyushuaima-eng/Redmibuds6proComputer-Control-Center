param([Parameter(Mandatory=$true)][string]$Address, [switch]$Probe)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Devices.Bluetooth.BluetoothDevice,Windows.Devices.Bluetooth,ContentType=WindowsRuntime]
$null = [Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceServicesResult,Windows.Devices.Bluetooth,ContentType=WindowsRuntime]
$null = [Windows.Devices.Bluetooth.BluetoothCacheMode,Windows.Devices.Bluetooth,ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and
    $_.GetParameters().Count -eq 1 -and $_.GetGenericArguments().Count -eq 1
} | Select-Object -First 1
function Wait-WinRT($Operation, $ResultType) {
    $task = $asTask.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    if (-not $task.Wait(15000)) { $Operation.Cancel(); throw 'Bluetooth operation timed out after 15 seconds' }
    return $task.Result
}
$value = [Convert]::ToUInt64(($Address -replace '[:-]', ''), 16)
$device = Wait-WinRT ([Windows.Devices.Bluetooth.BluetoothDevice]::FromBluetoothAddressAsync($value)) ([Windows.Devices.Bluetooth.BluetoothDevice])
if ($null -eq $device) { throw 'Paired Bluetooth device was not found' }
try {
    Write-Output "Device: $($device.Name); status: $($device.ConnectionStatus)"
    $result = Wait-WinRT ($device.GetRfcommServicesAsync([Windows.Devices.Bluetooth.BluetoothCacheMode]::Uncached)) ([Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceServicesResult])
    Write-Output "Discovery status: $($result.Error)"
    foreach ($service in $result.Services) {
        try {
            Write-Output ([pscustomobject]@{
                ServiceUuid = $service.ServiceId.Uuid.ToString()
                HostName = $service.ConnectionHostName.RawName
                ServiceName = $service.ConnectionServiceName
            } | ConvertTo-Json -Compress)
        } finally { $service.Dispose() }
    }
} finally { $device.Dispose() }
if ($Probe) {
    $probePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'diagnostics\RfcommProbe.exe'
    if (-not (Test-Path -LiteralPath $probePath)) { & (Join-Path $PSScriptRoot 'build-probe.ps1') }
    & $probePath $Address
    if ($LASTEXITCODE -ne 0) { throw 'Read-only authentication probe failed' }
}
