#Requires -Version 5.1
# Release-package consistency check. Catches the failure mode where a document
# is added to docs/ after the archive was built, or where a shipped document
# links to a file that never made it into the package.
#
#   tools\verify-package.ps1 -Dist dist\v1.2.1 -Zip dist\v1.2.1\WirelessBatteryMoniter-v1.2.1.zip
#
# Without -Zip exactly one *.zip inside -Dist is used; when there is none the
# zip comparison is skipped with a warning so the folder can still be checked.
param(
    [string]$Dist = '',
    [string]$Zip = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Dist)) {
    $project = [xml](Get-Content (Join-Path $root 'src\WirelessBatteryMoniter\WirelessBatteryMoniter.csproj') -Raw)
    $Dist = Join-Path $root ('dist\v' + $project.Project.PropertyGroup.Version)
}
$Dist = [IO.Path]::GetFullPath($Dist).TrimEnd('\')
if (-not (Test-Path -LiteralPath $Dist -PathType Container)) { throw "Package folder not found: $Dist" }
$distPrefix = $Dist + '\'

$script:failures = New-Object 'System.Collections.Generic.List[string]'
function Add-Pass([string]$message) { Write-Host ('PASS  ' + $message) -ForegroundColor Green }
function Add-Fail([string]$message) {
    $script:failures.Add($message)
    Write-Host ('FAIL  ' + $message) -ForegroundColor Red
}

# The text files are UTF-8 without a BOM, so the encoding must be explicit;
# Windows PowerShell would otherwise decode them with the ANSI code page and
# turn a Chinese link target such as 对话日志.md into mojibake.
function Read-Utf8([string]$path) { return Get-Content -LiteralPath $path -Raw -Encoding UTF8 }

# 1. Files every release folder must contain.
$requiredFiles = @(
    'WirelessBatteryMoniter.exe',
    'WirelessBatteryMoniter.exe.config',
    'README.md',
    'QUICKSTART.txt',
    'LICENSE',
    'VERSION.txt',
    'Install.cmd',
    'install.ps1',
    'Start.cmd',
    'install-transaction.ps1'
)
foreach ($name in $requiredFiles) {
    if (Test-Path -LiteralPath (Join-Path $Dist $name) -PathType Leaf) { Add-Pass "required file: $name" }
    else { Add-Fail "required file missing: $name" }
}
# Reject unexpected files rather than silently shipping developer material.
foreach ($item in Get-ChildItem -LiteralPath $Dist -Force) {
    if ($item.PSIsContainer -or ($requiredFiles -notcontains $item.Name -and $item.Extension -ne '.zip')) {
        Add-Fail ("not in release allowlist: " + $item.Name)
    }
}

# 2. Every relative markdown link must resolve inside the package.
$linkPattern = [regex]'\]\(\s*(?<target><[^>]+>|[^)\s]+)(?:\s+"[^"]*")?\s*\)'
$externalPattern = [regex]'^(?:[A-Za-z][A-Za-z0-9+.-]*:|#|//)'
$linkChecked = 0
$markdownFiles = @(Get-ChildItem -LiteralPath $Dist -Filter '*.md' -File -Recurse | Sort-Object FullName)
foreach ($markdown in $markdownFiles) {
    $sourceName = $markdown.FullName.Substring($distPrefix.Length)
    $text = Read-Utf8 $markdown.FullName
    foreach ($match in $linkPattern.Matches($text)) {
        $target = $match.Groups['target'].Value.Trim()
        if ($target.StartsWith('<') -and $target.EndsWith('>')) {
            $target = $target.Substring(1, $target.Length - 2).Trim()
        }
        if ($externalPattern.IsMatch($target)) { continue }
        $target = ($target -split '#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($target)) { continue }
        try { $target = [Uri]::UnescapeDataString($target) } catch { }
        $linkChecked++
        $resolved = [IO.Path]::GetFullPath((Join-Path $markdown.DirectoryName $target))
        if (-not $resolved.StartsWith($distPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Fail "$sourceName : link escapes the package: $target"
        }
        elseif (Test-Path -LiteralPath $resolved -PathType Leaf) {
            Add-Pass "$sourceName : link target $target"
        }
        else {
            Add-Fail "$sourceName : link target missing: $target"
        }
    }
}
if ($markdownFiles.Count -gt 0) { Add-Pass "markdown links resolved: $linkChecked relative link(s) in $($markdownFiles.Count) file(s)" }

# 3. Plain-text docs\ references (QUICKSTART.txt points at docs\... directly).
$textReferencePattern = [regex]'docs\\[A-Za-z0-9._-]+'
$textChecked = 0
foreach ($textFile in @(Get-ChildItem -LiteralPath $Dist -Filter '*.txt' -File -Recurse | Sort-Object FullName)) {
    $sourceName = $textFile.FullName.Substring($distPrefix.Length)
    foreach ($match in $textReferencePattern.Matches((Read-Utf8 $textFile.FullName))) {
        $textChecked++
        $resolved = Join-Path $textFile.DirectoryName $match.Value
        if (Test-Path -LiteralPath $resolved -PathType Leaf) {
            Add-Pass "$sourceName : referenced $($match.Value)"
        }
        else {
            Add-Fail "$sourceName : referenced path missing: $($match.Value)"
        }
    }
}
if ($textChecked -gt 0) { Add-Pass "plain-text references resolved: $textChecked" }

# 4. The release executable must be able to register a tray icon.
$exe = Join-Path $Dist 'WirelessBatteryMoniter.exe'
if (Test-Path -LiteralPath $exe -PathType Leaf) {
    $acl = (& icacls.exe $exe) -join [Environment]::NewLine
    if ($acl -match 'Medium Mandatory Level' -or $acl -match 'S-1-16-8192') {
        Add-Pass 'release exe integrity is Medium'
    }
    else {
        Add-Fail 'release exe integrity is not Medium; a low-integrity process cannot register a tray icon'
    }
}

# 5. VERSION.txt must describe the executable that ships next to it.
$versionFile = Join-Path $Dist 'VERSION.txt'
if ((Test-Path -LiteralPath $versionFile -PathType Leaf) -and (Test-Path -LiteralPath $exe -PathType Leaf)) {
    $declared = (Read-Utf8 $versionFile)
    $fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    $expected = ($fileVersion -replace '\.0$', '')
    if ($declared -match ('WirelessBatteryMoniter\s+' + [regex]::Escape($expected) + '\b')) {
        Add-Pass "VERSION.txt matches executable version $fileVersion"
    }
    else {
        Add-Fail "VERSION.txt does not declare executable version $fileVersion"
    }
}

# 6. The archive must match the folder it was built from.
$archiveChecked = $false
if ([string]::IsNullOrWhiteSpace($Zip)) {
    $found = @(Get-ChildItem -LiteralPath $Dist -Filter '*.zip' -File | Sort-Object Name)
    if ($found.Count -eq 1) { $Zip = $found[0].FullName }
    elseif ($found.Count -eq 0) { Write-Host 'WARN  no zip in the package folder; archive checks skipped.' -ForegroundColor Yellow }
    else { throw "Multiple zip files found in $Dist; pass -Zip explicitly." }
}

if (-not [string]::IsNullOrWhiteSpace($Zip)) {
    $archiveChecked = $true
    $Zip = [IO.Path]::GetFullPath($Zip)
    if (-not (Test-Path -LiteralPath $Zip -PathType Leaf)) {
        Add-Fail "archive not found: $Zip"
    }
    else {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
        try {
            $entries = @{}
            foreach ($entry in $archive.Entries) {
                $name = $entry.FullName.Replace('/', '\')
                if ($requiredFiles -notcontains $name) {
                    Add-Fail "archive member not in release allowlist: $name"
                    continue
                }
                if ($entries.ContainsKey($name)) {
                    Add-Fail "duplicate archive member: $name"
                    continue
                }
                $entries[$name] = $entry
            }

            # Everything in the release folder must be archived, except other zips.
            $folderFiles = @(Get-ChildItem -LiteralPath $Dist -File -Recurse | Where-Object { $_.Name -notlike '*.zip' })
            foreach ($file in $folderFiles) {
                $relative = $file.FullName.Substring($distPrefix.Length)
                if (-not $entries.ContainsKey($relative)) {
                    Add-Fail "in the package folder but not in the archive: $relative"
                }
            }

            # Compare every file's content, including same-length changes.
            foreach ($name in $entries.Keys) {
                $target = Join-Path $Dist $name
                if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
                    Add-Fail "archive entry has no file in the package folder: $name"
                    continue
                }
                $stream = $entries[$name].Open()
                $sha = [System.Security.Cryptography.SHA256]::Create()
                try {
                    $archiveHash = -join ($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') })
                } finally {
                    $sha.Dispose()
                    $stream.Dispose()
                }
                $folderHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($archiveHash -eq $folderHash) {
                    Add-Pass "archive file matches (SHA256): $name"
                } else {
                    Add-Fail "archive file differs (SHA256): $name"
                }
            }

            Add-Pass "archive compared with the package folder: $($entries.Count) file(s)"
        }
        finally {
            $archive.Dispose()
        }
    }
}

Write-Host ''
if ($script:failures.Count -gt 0) {
    Write-Host ("RESULT: " + $script:failures.Count + " package check(s) failed.") -ForegroundColor Red
    exit 1
}
if (-not $archiveChecked) {
    Write-Host 'RESULT: package folder verified; the archive was not checked.' -ForegroundColor Green
    exit 0
}
Write-Host 'RESULT: package verified.' -ForegroundColor Green
exit 0

