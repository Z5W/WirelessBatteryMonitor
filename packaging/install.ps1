#Requires -Version 5.1
param([switch]$Launch, [switch]$SkipShellIntegration)
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA 'Programs\WirelessBatteryMoniter'
$exeTarget = Join-Path $target 'WirelessBatteryMoniterTray.exe'
. (Join-Path $source 'install-transaction.ps1')
$mapping = @{
    'WirelessBatteryMoniter.exe' = 'WirelessBatteryMoniterTray.exe'
    'WirelessBatteryMoniter.exe.config' = 'WirelessBatteryMoniterTray.exe.config'
    'README.md' = 'README.md'; 'QUICKSTART.txt' = 'QUICKSTART.txt'
    'LICENSE' = 'LICENSE'; 'VERSION.txt' = 'VERSION.txt'
}
$expected = @{}
foreach ($name in $mapping.Keys) {
    $expected[$mapping[$name]] = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash
}
$newVersion = [version](Get-Item -LiteralPath (Join-Path $source 'WirelessBatteryMoniter.exe')).VersionInfo.FileVersion
if (Test-Path -LiteralPath $exeTarget) {
    $oldVersion = [version](Get-Item -LiteralPath $exeTarget).VersionInfo.FileVersion
    if ($oldVersion -gt $newVersion) { throw "Installed version $oldVersion is newer than package $newVersion. Downgrade refused." }
}
$needsUpdate = $false
foreach ($name in $expected.Keys) {
    $file = Join-Path $target $name
    if (!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected[$name]) { $needsUpdate = $true }
}
if ($needsUpdate) {
    if (Get-Process -Name 'WirelessBatteryMoniterTray', 'WirelessBatteryMoniter' -ErrorAction SilentlyContinue) {
        throw 'Exit WirelessBatteryMoniter from its tray menu before installing this update. Closing its window only hides it.'
    }
    $parent = Split-Path -Parent $target
    $stage = Join-Path $parent ('.WirelessBatteryMoniter-staging-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $stage | Out-Null
    foreach ($name in $mapping.Keys) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $stage $mapping[$name]) }
    $verify = {
        param($directory)
        foreach ($name in $expected.Keys) {
            if ((Get-FileHash -LiteralPath (Join-Path $directory $name) -Algorithm SHA256).Hash -ne $expected[$name]) { throw "Installation hash mismatch: $name" }
        }
    }
    Publish-ReleaseDirectory -Staging $stage -Destination $target -Verify $verify
}
# Shell integration occurs only after a complete, verified application is installed.
if (!$SkipShellIntegration) {
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'WirelessBatteryMoniter.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exeTarget
$shortcut.Arguments = '--open'
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = $exeTarget + ',0'
$shortcut.WindowStyle = 1
$shortcut.Description = 'WirelessBatteryMoniter - wireless battery tray monitor'
$shortcut.Save()
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$existing = Get-ItemProperty -LiteralPath $runKey -Name 'WirelessBatteryMoniter' -ErrorAction SilentlyContinue
if ($existing) { Set-ItemProperty -LiteralPath $runKey -Name 'WirelessBatteryMoniter' -Value ('"' + $exeTarget + '" --minimized') }
}
Write-Host ('Installed and verified: ' + $exeTarget)
Write-Host 'Updates preserve this executable path. Historical tray cache is not modified.'
if ($Launch) { Start-Process -FilePath $exeTarget -ArgumentList '--open' -WorkingDirectory $target -WindowStyle Normal }

