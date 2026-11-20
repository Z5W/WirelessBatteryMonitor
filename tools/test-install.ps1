#Requires -Version 5.1
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$package=Join-Path $root 'dist\v1.2.3'
$test=Join-Path $root ('artifacts\tests\install-' + [guid]::NewGuid().ToString('N'))
$source=Join-Path $test 'package'
New-Item -ItemType Directory -Force $source | Out-Null
Get-ChildItem -LiteralPath $package -File | Where-Object Extension -ne '.zip' | Copy-Item -Destination $source
$savedLocal=$env:LOCALAPPDATA
try {
    $env:LOCALAPPDATA=Join-Path $test 'user'
    $target=Join-Path $env:LOCALAPPDATA 'Programs\WirelessBatteryMoniter'
    $exe=Join-Path $target 'WirelessBatteryMoniterTray.exe'
    & (Join-Path $source 'install.ps1') -SkipShellIntegration
    if ((Get-FileHash $exe).Hash -ne (Get-FileHash (Join-Path $source 'WirelessBatteryMoniter.exe')).Hash) {throw 'Initial install hash mismatch'}
    Write-Output 'PASS first install uses fixed executable name and hash'
    & (Join-Path $source 'install.ps1') -SkipShellIntegration
    if (@(Get-ChildItem (Split-Path $target) -Directory -Filter '.previous-*').Count -ne 0) {throw 'Unchanged reinstall created backup'}
    Write-Output 'PASS unchanged reinstall is idempotent'
    Add-Content (Join-Path $source 'README.md') 'Installer upgrade test'
    & (Join-Path $source 'install.ps1') -SkipShellIntegration
    if ((Get-FileHash (Join-Path $target 'README.md')).Hash -ne (Get-FileHash (Join-Path $source 'README.md')).Hash) {throw 'Upgrade content mismatch'}
    if (@(Get-ChildItem (Split-Path $target) -Directory -Filter '.previous-*').Count -ne 1) {throw 'Old installation not retained'}
    Write-Output 'PASS upgrade preserves path and old installation'
    Add-Content (Join-Path $source 'README.md') 'Locked upgrade test'
    $before=(Get-FileHash (Join-Path $target 'README.md')).Hash
    $lock=[IO.File]::Open($exe,'Open','Read','None')
    $rejected=$false
    try { & (Join-Path $source 'install.ps1') -SkipShellIntegration } catch { $rejected=$true } finally {$lock.Dispose()}
    if (!$rejected -or (Get-FileHash (Join-Path $target 'README.md')).Hash -ne $before) {throw 'Locked upgrade did not preserve old installation'}
    Write-Output 'PASS locked upgrade leaves old installation intact'
    $old=Join-Path $root 'artifacts\archive\releases-before-fixed-install\v1.2.2\WirelessBatteryMoniter.exe'
    if (Test-Path $old) {
        Copy-Item $old (Join-Path $source 'WirelessBatteryMoniter.exe') -Force
        $rejected=$false
        try { & (Join-Path $source 'install.ps1') -SkipShellIntegration } catch { $rejected=$_.Exception.Message -like '*Downgrade refused*' }
        if (!$rejected) {throw 'Downgrade was not rejected'}
        Write-Output 'PASS downgrade rejected'
    }
} finally { $env:LOCALAPPDATA=$savedLocal }

