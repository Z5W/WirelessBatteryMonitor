#Requires -Version 5.1
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$contracts = Join-Path $root '.tools\windows-sdk-contracts\ref\netstandard2.0'
if (-not (Test-Path $contracts)) {
    throw 'Missing Windows SDK contracts. Restore Microsoft.Windows.SDK.Contracts 10.0.22621.3233 or extract its NuGet package to .tools\windows-sdk-contracts.'
}
$output = Join-Path $root "artifacts\build\bin\WirelessBatteryMoniter\$Configuration\net48"
New-Item -ItemType Directory -Force $output | Out-Null
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
    'System.Runtime.Serialization.dll', 'System.Runtime.WindowsRuntime.dll', 'System.Runtime.dll',
    'System.ObjectModel.dll', 'System.Threading.Tasks.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) }
$references += @('UIAutomationClient.dll', 'UIAutomationTypes.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path (Join-Path $frameworkDir 'WPF') $_) }
$references += @(Get-ChildItem $contracts -Filter '*.winmd' | ForEach-Object { '/reference:' + $_.FullName })
$sourceRoot = Join-Path $root 'src\WirelessBatteryMoniter'
$sources = @(Get-ChildItem $sourceRoot -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | ForEach-Object { $_.FullName })
$fontResources = @(Get-ChildItem (Join-Path $sourceRoot 'Assets\Fonts') -File | Where-Object { $_.Extension -in '.ttf','.txt' } | ForEach-Object { '/resource:' + $_.FullName + ',WirelessBatteryMoniter.Fonts.' + $_.Name })
& (Join-Path $frameworkDir 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ "/out:$output\WirelessBatteryMoniter.exe" "/win32icon:$sourceRoot\Assets\app.ico" "/win32manifest:$sourceRoot\app.manifest" $references $fontResources $sources
if ($LASTEXITCODE -ne 0) { throw 'Framework compilation failed.' }
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>
'@ | Set-Content (Join-Path $output 'WirelessBatteryMoniter.exe.config') -Encoding UTF8


