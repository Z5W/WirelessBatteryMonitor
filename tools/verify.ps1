#Requires -Version 5.1
param(
    [string]$Executable = '',
    [switch]$Hardware,
    [switch]$Tray
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $root 'artifacts\build\bin\WirelessBatteryMoniter\Release\net48\WirelessBatteryMoniter.exe' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$validation = Join-Path $root 'artifacts\tests\validation'
New-Item -ItemType Directory -Force $validation | Out-Null
$modes = @('--selftest')
if ($Hardware) { $modes += '--diagnose-scan' }
if ($Tray) { $modes += '--diagnose-tray' }
$failed = $false
foreach ($mode in $modes) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = $mode
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.EnvironmentVariables['TEMP'] = $validation
    $info.EnvironmentVariables['TMP'] = $validation
    $process = [System.Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(45000)) { throw "$mode has not completed within 45 seconds (PID $($process.Id))." }
    Write-Output "$mode exit=$($process.ExitCode)"
    if ($process.ExitCode -ne 0) { $failed = $true }
    $process.Dispose()
}
Get-Content (Join-Path $validation 'WirelessBatteryMoniter-selftest.txt')
if ($Hardware) { Get-Content (Join-Path $validation 'WirelessBatteryMoniter-diagnostics\scan.txt') }
if ($Tray) { Get-Content (Join-Path $validation 'WirelessBatteryMoniter-diagnostics\tray.txt') }
if ($failed) { throw 'One or more checks failed; inspect the reports above.' }
