#Requires -Version 5.1
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Clean,
    [switch]$FrameworkCompiler,
    [switch]$Package,
    [switch]$VersionedOutput # Compatibility alias: also requests a versioned release package
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\WirelessBatteryMoniter\WirelessBatteryMoniter.csproj'
$nugetConfig = Join-Path $root 'NuGet.config'

function Remove-BuildItem([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $workspacePrefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the workspace: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) { Remove-Item -LiteralPath $fullPath -Recurse -Force }
}

$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) {
        $FrameworkCompiler = $true
    }
    if ($command) { $dotnet = $command.Source }
}

$environmentNames = @('TMP', 'TEMP', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'APPDATA', 'LOCALAPPDATA', 'DOTNET_NOLOGO', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
# Keep temporary state inside the repository. This also avoids restricted
# per-user temp directories on locked-down Windows installations.
$tmp = Join-Path $root 'artifacts\temp'
New-Item -ItemType Directory -Force $tmp | Out-Null
$env:TMP = $tmp
$env:TEMP = $tmp
$env:DOTNET_CLI_HOME = Join-Path $root '.tools'
$env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'
$env:APPDATA = Join-Path $root '.tools\appdata'
$env:LOCALAPPDATA = Join-Path $root '.tools\localappdata'
New-Item -ItemType Directory -Force $env:APPDATA, $env:LOCALAPPDATA | Out-Null
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

if ($Clean) {
    Remove-BuildItem (Join-Path $root 'artifacts\build')
}

$installedSdks = @()
if (-not $FrameworkCompiler) { $installedSdks = @(& $dotnet --list-sdks) }
if ($FrameworkCompiler -or $installedSdks.Count -eq 0) {
    Write-Host 'Building with the installed .NET Framework compiler...' -ForegroundColor Cyan
    & (Join-Path $root 'tools\build-framework.ps1') -Configuration $Configuration
} else {
    Write-Host 'Restoring packages...' -ForegroundColor Cyan
    & $dotnet restore $project --configfile $nugetConfig -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & $dotnet build $project -c $Configuration --no-restore -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
}

$output = Join-Path $root "artifacts\build\bin\WirelessBatteryMoniter\$Configuration\net48"
$developmentExe = Join-Path $output 'WirelessBatteryMoniter.exe'
& (Join-Path $root 'tools\repair-release-integrity.ps1') -Path $developmentExe
if ($LASTEXITCODE -ne 0) { throw 'Development executable integrity repair failed.' }
Write-Host ('Development build: ' + $developmentExe)
if (-not ($Package -or $VersionedOutput)) { return }

$version = (Get-Item $developmentExe).VersionInfo.FileVersion -replace '\.0$', ''
$destination = Join-Path $root ('dist\v' + $version)
$dist = Join-Path $root ('dist\.staging-' + [Guid]::NewGuid().ToString('N'))
try {
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item -LiteralPath $developmentExe -Destination (Join-Path $dist 'WirelessBatteryMoniter.exe') -Force
& (Join-Path $root 'tools\repair-release-integrity.ps1') -Path (Join-Path $dist 'WirelessBatteryMoniter.exe')
if ($LASTEXITCODE -ne 0) { throw 'Release executable integrity repair failed.' }
Copy-Item -LiteralPath (Join-Path $output 'WirelessBatteryMoniter.exe.config') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'docs\user\README.md') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'docs\user\QUICKSTART.txt') -Destination $dist -Force
Set-Content (Join-Path $dist 'VERSION.txt') ("WirelessBatteryMoniter " + $version + "`r`nBuilt " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
# Explicit distribution allowlist: no source, conversation history, test logs or developer docs.
$releaseFiles = @('WirelessBatteryMoniter.exe', 'WirelessBatteryMoniter.exe.config',
    'README.md', 'QUICKSTART.txt', 'LICENSE', 'VERSION.txt', 'Install.cmd', 'install.ps1', 'Start.cmd', 'install-transaction.ps1')
Copy-Item -LiteralPath (Join-Path $root 'tools\release-transaction.ps1') -Destination (Join-Path $dist 'install-transaction.ps1')
foreach ($name in @('Install.cmd', 'install.ps1', 'Start.cmd')) {
    Copy-Item -LiteralPath (Join-Path $root ('packaging\' + $name)) -Destination $dist -Force
}
$zip = Join-Path $dist ("WirelessBatteryMoniter-v" + $version + ".zip")
$packageFiles = @($releaseFiles | ForEach-Object { Join-Path $dist $_ })
Compress-Archive -LiteralPath $packageFiles -DestinationPath $zip -CompressionLevel Optimal -Force
. (Join-Path $root 'tools\release-transaction.ps1')
$verifyRelease = {
    param($directory)
    & (Join-Path $root 'tools\verify-package.ps1') -Dist $directory -Zip (Join-Path $directory ("WirelessBatteryMoniter-v" + $version + ".zip"))
    if ($LASTEXITCODE -ne 0) { throw "Package verification failed: $directory" }
}
Publish-ReleaseDirectory -Staging $dist -Destination $destination -Verify $verifyRelease
Write-Host ('Release folder: ' + $destination)
Write-Host ('Package: ' + (Join-Path $destination ("WirelessBatteryMoniter-v" + $version + ".zip")))
} finally {
    # Keep failed staging for diagnosis/recovery; never delete rollback evidence.
    if (Test-Path -LiteralPath $dist) { Write-Warning ("Unpublished staging retained: " + $dist) }
}

} finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
}

