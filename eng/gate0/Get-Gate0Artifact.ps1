[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactId,
    [Parameter(Mandatory)] [string] $DestinationPath,
    [string] $SourceManifestPath,
    [string] $ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force

if ([string]::IsNullOrWhiteSpace($SourceManifestPath)) { $SourceManifestPath = Join-Path $PSScriptRoot 'artifact-retention-manifest.json' }
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = Join-Path $PSScriptRoot 'artifact-manifest.json' }
if (-not [IO.Path]::IsPathRooted($DestinationPath)) { throw 'DestinationPath must be absolute.' }
$destination = [IO.Path]::GetFullPath($DestinationPath)
if (Test-Path -LiteralPath $destination) { throw 'DestinationPath already exists.' }
$destinationDirectory = [IO.Path]::GetDirectoryName($destination)
if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) { throw 'DestinationPath parent directory does not exist.' }

$source = Read-Gate0SourceInventory ([IO.Path]::GetFullPath($SourceManifestPath))
$remote = Read-Gate0RemoteManifest ([IO.Path]::GetFullPath($ManifestPath))
Assert-Gate0ManifestPair $source $remote
$entry = @($source.Entries | Where-Object ArtifactId -eq $ArtifactId)
if ($entry.Count -ne 1) { throw "Unknown or duplicate Gate 0 artifact ID: $ArtifactId" }
$entry = $entry[0]
$record = @($remote.Manifest.artifacts | Where-Object logicalArtifactId -eq $ArtifactId)
if ($record.Count -ne 1 -or $record[0].retentionStatus -ne 'remote-verified') {
    throw "Artifact is not recorded as remotely verified: $ArtifactId"
}

$temporary = Join-Path $destinationDirectory ".reelforge-gate0-download-$([Guid]::NewGuid().ToString('N')).tmp"
$bundle = New-Gate0R2ClientBundle
try {
    $bundle.Client.DownloadObjectAsync(
        $bundle.BucketName,
        $entry.ObjectKey,
        $temporary).GetAwaiter().GetResult()
    Test-Gate0DownloadedArtifact $entry $temporary
    [IO.File]::Move($temporary, $destination)
}
finally {
    $bundle.HttpClient.Dispose()
    if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force }
}

[pscustomobject]@{
    logicalArtifactId = $entry.ArtifactId
    destinationPath = $destination
    byteSize = $entry.Size
    sha256 = $entry.Sha256
    r2ObjectKey = $entry.ObjectKey
    status = 'downloaded-and-verified'
}
