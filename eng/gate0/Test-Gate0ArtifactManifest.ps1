[CmdletBinding()]
param(
    [switch] $Local,
    [switch] $Remote,
    [switch] $UpdateManifest,
    [switch] $RefreshSourceInventory,
    [string] $ArtifactId,
    [string] $ArtifactRoot,
    [string] $SourceManifestPath,
    [string] $ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force

if ($UpdateManifest -and -not $Remote) { throw '-UpdateManifest requires -Remote byte verification.' }
if ([string]::IsNullOrWhiteSpace($SourceManifestPath)) { $SourceManifestPath = Join-Path $PSScriptRoot 'artifact-retention-manifest.json' }
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = Join-Path $PSScriptRoot 'artifact-manifest.json' }
$source = Read-Gate0SourceInventory ([IO.Path]::GetFullPath($SourceManifestPath))
$remoteManifest = Read-Gate0RemoteManifest ([IO.Path]::GetFullPath($ManifestPath))
if ($RefreshSourceInventory) {
    $remoteManifest = Update-Gate0RemoteSourceInventory $source $remoteManifest.Path
}
Assert-Gate0ManifestPair $source $remoteManifest

$targets = if ([string]::IsNullOrWhiteSpace($ArtifactId)) {
    @($source.Entries)
}
else {
    @($source.Entries | Where-Object ArtifactId -eq $ArtifactId)
}
if ($targets.Count -eq 0) { throw "Unknown Gate 0 artifact ID: $ArtifactId" }

if ($Local) {
    $root = Resolve-Gate0ArtifactRoot $ArtifactRoot
    foreach ($entry in $targets) { [void] (Get-Gate0LocalArtifactPath $entry $root) }
}

$remoteVerified = 0
if ($Remote) {
    $bundle = New-Gate0R2ClientBundle
    $verifiedKeys = @{}
    try {
        foreach ($entry in $targets) {
            if (-not $verifiedKeys.ContainsKey($entry.ObjectKey)) {
                $metadata = $bundle.Client.HeadObjectAsync(
                    $bundle.BucketName,
                    $entry.ObjectKey).GetAwaiter().GetResult()
                if ($null -eq $metadata) { throw "Required R2 artifact object is missing: $($entry.ObjectKey)" }
                if ($null -ne $metadata.ContentLength -and [int64] $metadata.ContentLength -ne $entry.Size) {
                    throw "Required R2 artifact object has the wrong size: $($entry.ObjectKey)"
                }
                Invoke-Gate0RemoteByteVerification $bundle $entry
                $verifiedKeys[$entry.ObjectKey] = $true
            }
            $remoteVerified++
            if ($UpdateManifest) {
                $verifiedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                $remoteManifest = Save-Gate0RemoteVerifiedReceipt $source $remoteManifest.Path $entry 'independently-retrieved-and-verified' $verifiedUtc
            }
        }
    }
    finally {
        $bundle.HttpClient.Dispose()
    }
}

$recorded = @($remoteManifest.Manifest.artifacts).Count
[pscustomobject]@{
    manifestId = [string] $remoteManifest.Manifest.manifestId
    artifactSetId = [string] $source.Manifest.artifactSetId
    sourceManifestSha256 = $source.Sha256
    selectedLogicalArtifactCount = $targets.Count
    selectedLogicalArtifactBytes = [int64] (($targets | Measure-Object Size -Sum).Sum)
    localByteVerificationPerformed = [bool] $Local
    remoteByteVerificationPerformed = [bool] $Remote
    remotelyVerifiedThisRun = $remoteVerified
    recordedRemoteVerifiedLogicalArtifacts = $recorded
    requiredLogicalArtifactCount = [int] $source.Manifest.totals.fileCount
    secondPrivateCopyVerified = [bool] $remoteManifest.Manifest.status.secondPrivateCopyVerified
    retentionCondition = [string] $remoteManifest.Manifest.status.retentionCondition
}
