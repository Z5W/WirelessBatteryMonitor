#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'release-transaction.ps1')
$testRoot = Join-Path $root ('artifacts\tests\release-transaction-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $testRoot | Out-Null
function Assert-True($condition, $message) {
    if (-not $condition) { throw "FAIL: $message" }
    Write-Output "PASS: $message"
}
function New-Case($name) {
    $case = Join-Path $testRoot $name
    New-Item -ItemType Directory -Force (Join-Path $case 'stage'), (Join-Path $case 'release') | Out-Null
    Set-Content (Join-Path $case 'stage\marker.txt') 'new'
    Set-Content (Join-Path $case 'release\marker.txt') 'old'
    return $case
}
$case = New-Case 'pre-validation-fails'
$failed = $false
try { Publish-ReleaseDirectory (Join-Path $case 'stage') (Join-Path $case 'release') { param($p) throw 'Injected validation failure' } }
catch { $failed = $true }
Assert-True ($failed -and (Get-Content (Join-Path $case 'release\marker.txt')) -eq 'old') 'pre-validation failure leaves old release intact'

$case = New-Case 'post-validation-fails'
$failed = $false
try { Publish-ReleaseDirectory (Join-Path $case 'stage') (Join-Path $case 'release') {
    param($p)
    if ([IO.Path]::GetFileName($p) -eq 'release') { throw 'Injected final verification failure' }
} } catch { $failed = $true }
Assert-True ($failed -and (Get-Content (Join-Path $case 'release\marker.txt')) -eq 'old') 'post-validation failure restores old release'
Assert-True ((Get-Content (Join-Path $case 'stage\marker.txt')) -eq 'new') 'failed new release retained for diagnosis'

$case = New-Case 'promotion-fails'
$failed = $false
try { Publish-ReleaseDirectory (Join-Path $case 'stage') (Join-Path $case 'release') {
    param($p)
    # Make the actual directory promotion fail after validation, without mocking Move.
    [IO.Directory]::Move($p, $p + '-unavailable')
} } catch { $failed = $true }
Assert-True ($failed -and (Get-Content (Join-Path $case 'release\marker.txt')) -eq 'old') 'directory promotion failure restores old release'

$case = New-Case 'success'
Publish-ReleaseDirectory (Join-Path $case 'stage') (Join-Path $case 'release') { param($p) if (-not (Test-Path (Join-Path $p 'marker.txt'))) { throw 'Missing content' } }
Assert-True ((Get-Content (Join-Path $case 'release\marker.txt')) -eq 'new') 'successful transaction publishes new release'
$backup = @(Get-ChildItem -LiteralPath $case -Directory -Force | Where-Object { $_.Name -like '.previous-*' })
Assert-True ($backup.Count -eq 1 -and (Get-Content (Join-Path $backup[0].FullName 'marker.txt')) -eq 'old') 'successful transaction retains previous release'

$case = New-Case 'first-release'
$destination = Join-Path $case 'first'
Publish-ReleaseDirectory (Join-Path $case 'stage') $destination { param($p) }
Assert-True ((Get-Content (Join-Path $destination 'marker.txt')) -eq 'new') 'first publication works without existing release'

$case = New-Case 'locked'
$lock = [IO.File]::Open((Join-Path $case '.publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$failed = $false
try {
    try { Publish-ReleaseDirectory (Join-Path $case 'stage') (Join-Path $case 'release') { param($p) } }
    catch { $failed = $true }
} finally { $lock.Dispose() }
Assert-True ($failed -and (Get-Content (Join-Path $case 'release\marker.txt')) -eq 'old') 'concurrent publisher cannot change release'
Write-Output ('RESULT: release transaction tests passed; evidence: ' + $testRoot)
