#Requires -Version 5.1
param(
    [switch]$Demo,
    [switch]$Open,
    [switch]$Minimized
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $env:LOCALAPPDATA 'Programs\WirelessBatteryMoniter\WirelessBatteryMoniterTray.exe'
if (-not (Test-Path $exe)) {
    throw 'Installed application not found. Build a package, then run its Install.cmd or Start.cmd.'
}

$arguments = @()
if ($Demo) { $arguments += '--demo' }
if ($Open) { $arguments += '--open' }
if ($Minimized) { $arguments += '--minimized' }

if ($arguments.Count -gt 0) {
    Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden
} else {
    Start-Process -FilePath $exe -WindowStyle Hidden
}
Write-Host 'WirelessBatteryMoniter started. Check the system tray (Windows 11 may put it under ^).'

