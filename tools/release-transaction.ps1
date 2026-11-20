# Dot-source this file. Staging and destination must be siblings on one volume.
function Publish-ReleaseDirectory {
    param(
        [Parameter(Mandatory=$true)][string]$Staging,
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][scriptblock]$Verify
    )
    $stagePath = [IO.Path]::GetFullPath($Staging).TrimEnd('\')
    $destinationPath = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    $parent = [IO.Path]::GetDirectoryName($destinationPath)
    if ($stagePath -eq $destinationPath -or [IO.Path]::GetDirectoryName($stagePath) -ne $parent) {
        throw 'Staging and destination must be distinct sibling directories.'
    }
    if (-not (Test-Path -LiteralPath $stagePath -PathType Container)) { throw 'Staging directory is missing.' }
    $backup = Join-Path $parent ('.previous-' + [IO.Path]::GetFileName($destinationPath) + '-' + [Guid]::NewGuid().ToString('N'))
    $lockPath = Join-Path $parent '.publish.lock'
    $lock = $null
    $oldMoved = $false
    $newMoved = $false
    try {
        # A concurrent publisher fails before touching either release directory.
        $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        & $Verify $stagePath
        if (Test-Path -LiteralPath $destinationPath) {
            [IO.Directory]::Move($destinationPath, $backup)
            $oldMoved = $true
        }
        [IO.Directory]::Move($stagePath, $destinationPath)
        $newMoved = $true
        & $Verify $destinationPath
        if ($oldMoved) { Write-Host ('Previous release retained: ' + $backup) }
    } catch {
        $publishError = $_
        try {
            if ($newMoved) { [IO.Directory]::Move($destinationPath, $stagePath) }
            if ($oldMoved) { [IO.Directory]::Move($backup, $destinationPath) }
        } catch {
            throw "Publication and rollback failed. Preserve these paths for recovery: $backup ; $stagePath ; $destinationPath . Original error: $publishError . Rollback error: $_"
        }
        throw $publishError
    } finally {
        if ($null -ne $lock) { $lock.Dispose() }
    }
}
