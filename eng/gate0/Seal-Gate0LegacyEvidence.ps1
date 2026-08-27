[CmdletBinding()]
param(
    [string] $ArtifactRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainment.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$sealPath = Join-Path $PSScriptRoot 'evidence/legacy-seal.json'
if (Test-Path -LiteralPath $sealPath -PathType Leaf) {
    $existing = Assert-Gate0LegacyEvidenceSeal $repositoryRoot -RequireEffective
    [pscustomobject]@{ status = 'already-effective'; sealPath = 'eng/gate0/evidence/legacy-seal.json'; rootIndexSha256 = $existing.Root.Sha256 }
    return
}

$legacyStatus = & git -C $repositoryRoot status --porcelain=v1 -- eng/gate0/artifact-retention-manifest.json eng/gate0/artifact-manifest.json
if ($LASTEXITCODE -ne 0) { throw 'Could not verify the Git status of the legacy manifests.' }
if (@($legacyStatus).Count -ne 0) { throw 'The working tree contains an unrecorded legacy-manifest append or rewrite.' }

$sourcePath = Join-Path $PSScriptRoot 'artifact-retention-manifest.json'
$durablePath = Join-Path $PSScriptRoot 'artifact-manifest.json'
$source = Read-Gate0SourceInventory $sourcePath
$durable = Read-Gate0RemoteManifest $durablePath
Assert-Gate0ManifestPair $source $durable
if (-not [bool]$durable.Manifest.status.secondPrivateCopyVerified -or $durable.Manifest.status.retentionCondition -ne 'complete') {
    throw 'The durable legacy corpus is not recorded as a complete independently verified second copy.'
}

$artifactRootResolved = Resolve-Gate0ArtifactRoot $ArtifactRoot
& (Join-Path $PSScriptRoot 'Test-Gate0ArtifactRetention.ps1') -ArtifactRoot $artifactRootResolved | Out-Null
$remoteVerification = & (Join-Path $PSScriptRoot 'Test-Gate0ArtifactManifest.ps1') -ArtifactRoot $artifactRootResolved -Local -Remote
if (-not [bool]$remoteVerification.remoteByteVerificationPerformed -or
    [int]$remoteVerification.remotelyVerifiedThisRun -ne 4101 -or [int]$remoteVerification.recordedRemoteVerifiedLogicalArtifacts -ne 4101 -or
    -not [bool]$remoteVerification.secondPrivateCopyVerified -or [string]$remoteVerification.retentionCondition -ne 'complete') {
    throw 'The live legacy R2 corpus did not pass complete independent retrieval and byte verification.'
}

$pending = Assert-Gate0LegacyEvidenceSeal $repositoryRoot
$root = $pending.Root
$seal = [ordered]@{
    schemaVersion = 1
    sealId = 'Gate0.LegacyEvidenceSeal.20260827'
    effectiveUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceManifestPath = 'eng/gate0/artifact-retention-manifest.json'
    sourceManifestSha256 = $source.Sha256
    durableManifestPath = 'eng/gate0/artifact-manifest.json'
    durableManifestSha256 = Get-Gate0EvidenceSha256 $durablePath
    logicalArtifactCount = [int]$source.Manifest.totals.fileCount
    logicalArtifactBytes = [int64]$source.Manifest.totals.totalBytes
    rootIndexPath = 'eng/gate0/evidence/root-index.json'
    initialRootIndexSha256 = $root.Sha256
    retentionCondition = 'complete-and-independently-byte-verified'
    limitations = @(
        'This seal freezes the legacy tracked manifests; it does not select a shipping runtime or approve distribution.',
        'Future evidence is added only through immutable shards and ordered root-index entries.'
    )
}
Write-Gate0EvidenceUtf8Atomic $sealPath (($seal | ConvertTo-Json -Depth 16) + "`n")
[void](Assert-Gate0LegacyEvidenceSeal $repositoryRoot -RequireEffective)
[pscustomobject]@{ status = 'effective'; sealPath = 'eng/gate0/evidence/legacy-seal.json'; sealSha256 = Get-Gate0EvidenceSha256 $sealPath; rootIndexSha256 = $root.Sha256 }
