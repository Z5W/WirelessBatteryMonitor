#Requires -Version 5.1
param([string]$Executable = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $root 'artifacts\build\bin\WirelessBatteryMoniter\Release\net48\WirelessBatteryMoniter.exe' }
$Executable = (Resolve-Path $Executable).Path
$out = Join-Path $root 'artifacts\tests\ui-redesign'
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item $Executable (Join-Path $out 'WirelessBatteryMoniter.exe') -Force
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
& (Join-Path $framework 'csc.exe') /nologo /target:exe "/win32manifest:$root\src\WirelessBatteryMoniter\app.manifest" "/out:$out\UiRegression.exe" "/reference:$Executable" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll (Join-Path $PSScriptRoot 'ui-regression.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI harness compilation failed.' }
& (Join-Path $out 'UiRegression.exe') $Executable $out | Tee-Object (Join-Path $out 'results.txt')
if ($LASTEXITCODE -ne 0) { throw 'UI regression failed.' }
