#Requires -Version 5.1
# Explicit developer action for release executables inherited from a low-
# integrity workspace. Never changes the workspace source tree or a system
# directory: every target must resolve to a WirelessBatteryMoniter.exe that
# lives inside this repository's dist or artifacts/build folder.
param(
    # Optional explicit list of release executables. When omitted, every
    # packaged WirelessBatteryMoniter.exe under dist/ is processed, including
    # versioned output folders such as dist/v1.2.1/.
    [string[]]$Path
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$distRoot = [IO.Path]::GetFullPath((Join-Path $root 'dist')).TrimEnd('\') + '\'
$buildRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts\build')).TrimEnd('\') + '\'
$exeName = 'WirelessBatteryMoniter.exe'
$mediumSid = 'S-1-16-8192'

function Resolve-Target([string]$candidate) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { throw 'An empty path was passed to -Path.' }
    # GetFullPath resolves both relative and absolute input before the scope
    # check, so a caller cannot escape dist/ with ".." segments.
    $full = [IO.Path]::GetFullPath($candidate)
    if (-not ($full.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase))) {
        throw "Refusing to change a path outside dist or artifacts/build: $full"
    }
    if (-not [string]::Equals([IO.Path]::GetFileName($full), $exeName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a file that is not $exeName`: $full"
    }
    return $full
}

$targets = @()
if ($Path) {
    foreach ($candidate in $Path) {
        $resolved = Resolve-Target $candidate
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Release executable not found: $resolved" }
        $targets += $resolved
    }
}
elseif (Test-Path -LiteralPath $distRoot -PathType Container) {
    $targets = @(Get-ChildItem -LiteralPath $distRoot -Filter $exeName -File -Recurse |
        Sort-Object FullName |
        ForEach-Object { Resolve-Target $_.FullName })
}

$targets = @($targets | Select-Object -Unique)
if ($targets.Count -eq 0) { throw 'Build the release first; no packaged executable was found under dist\\.' }

foreach ($file in $targets) {
    & icacls.exe $file /setintegritylevel M | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not set medium integrity on $file (icacls exit $LASTEXITCODE). No other path was changed."
    }

    # Read the label back instead of trusting icacls' exit code. Accept the
    # English label name or the locale-independent medium-integrity SID.
    $acl = (& icacls.exe $file) -join [Environment]::NewLine
    if ($acl -notmatch 'Medium Mandatory Level' -and $acl -notmatch [regex]::Escape($mediumSid)) {
        throw "The medium integrity label did not stick on $file. icacls reported:`r`n$acl"
    }
    Write-Host ('Medium integrity verified: ' + $file) -ForegroundColor Green
}

Write-Host ('Checked ' + $targets.Count + ' release executable(s); only named executables under dist or artifacts/build were changed.')
exit 0
