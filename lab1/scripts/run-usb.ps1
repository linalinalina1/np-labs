param([Parameter(Mandatory=$true)][string]$UsbPartsDirectory)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$program = 'bin/Release/net8.0/ConcurrencyLab1.dll'
if (!(Test-Path $program)) { throw 'Build in Release first: dotnet build -c Release' }
foreach ($threads in @(1,2,4)) {
    Read-Host "Eject the USB using Windows Safely Remove Hardware, unplug it, reconnect it, verify drive path, then press Enter for $threads thread(s)"
    dotnet $program 2 --dir $UsbPartsDirectory --threads $threads
    if ($LASTEXITCODE -ne 0) { throw "Task 2 / $threads failed" }
}
