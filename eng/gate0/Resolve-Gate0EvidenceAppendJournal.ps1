[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [ValidateSet('failed','blocked')] [string] $Disposition = 'blocked',
    [Parameter(Mandatory)] [string] $OwnerReviewIdentity,
    [switch] $SkipRemoteForIsolatedTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainment.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force

if ($OwnerReviewIdentity -notmatch '^(repository|sha256):[A-Za-z0-9._/-]+$' -or $OwnerReviewIdentity.Contains('\') -or $OwnerReviewIdentity.Contains('..')) { throw 'OwnerReviewIdentity must be a portable repository: or sha256: identity.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$artifactRootResolved = Resolve-Gate0ArtifactRoot $ArtifactRoot
Assert-Gate0EvidenceNoReparsePointAncestors $artifactRootResolved ([IO.Path]::GetDirectoryName($repositoryRoot))
if ($SkipRemoteForIsolatedTest) {
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $repositoryRoot.StartsWith("$temporary$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.gate0-containment-test-sentinel')) -or (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) { throw 'Remote bypass is permitted only in an isolated copied test repository.' }
}
[void](Assert-Gate0LegacyEvidenceSeal $repositoryRoot -RequireEffective)
$journalPath = "$artifactRootResolved.stage2-append-journal.json"
if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf)) { throw 'No Stage 2 evidence append journal requires disposition.' }
$journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json -Depth 64
$required = @('schemaVersion','proofRunId','evidenceGroupId','cellId','evidenceBoundary','disposition','contractIdentity','provenance','producerRuntimeIdentity','licenseRecords','attemptBindingsRelativePath','phase','stagingPath','destinationName','artifactCount','artifactBytes','objectKeys','artifacts','createdUtc')
$actual = @($journal.PSObject.Properties.Name | Sort-Object)
if ($journal.schemaVersion -ne 1 -or @(Compare-Object -ReferenceObject @($required | Sort-Object) -DifferenceObject $actual).Count -ne 0 -or $journal.phase -notin @('prepared','remoteInProgress','remoteVerified')) { throw 'The append journal is not a recognized bounded recovery record.' }
Assert-Gate0EvidenceIdentifier ([string]$journal.proofRunId) 'journal proofRunId'
Assert-Gate0EvidenceRelativePath ([string]$journal.destinationName) 'journal destinationName'
$expectedDestination = "future/stage2/$([string]$journal.proofRunId)"
if ([string]$journal.destinationName -ne $expectedDestination) { throw 'The append journal destination is outside the dedicated future evidence namespace.' }
$shardPath = Join-Path $PSScriptRoot "evidence/stage2/$([string]$journal.proofRunId).manifest.json"
if (Test-Path -LiteralPath $shardPath -PathType Leaf) { throw 'An unindexed immutable shard already exists; automatic journal disposition is unsafe and remains blocked for owner review.' }

$source = [IO.Path]::GetFullPath([string]$journal.stagingPath).TrimEnd([IO.Path]::DirectorySeparatorChar)
$approvedPrefix = "$artifactRootResolved.stage2-staging-"
if (-not $source.StartsWith($approvedPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetDirectoryName($source) -ne [IO.Path]::GetDirectoryName($artifactRootResolved)) { throw 'The append journal staging path is not a transaction-owned sibling.' }
$destination = [IO.Path]::GetFullPath((Join-Path $artifactRootResolved $expectedDestination.Replace('/', [IO.Path]::DirectorySeparatorChar)))
$movedDestination = $false
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    if (-not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'The append journal has neither staged nor destination evidence bytes.' }
    $source = "$artifactRootResolved.stage2-staging-recovery-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::Move($destination, $source)
    $movedDestination = $true
}
Assert-Gate0EvidenceNoReparsePointAncestors $source ([IO.Path]::GetDirectoryName($repositoryRoot))
$files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
if ($files.Count -ne [int]$journal.artifactCount -or [int64](($files | Measure-Object -Property Length -Sum).Sum) -ne [int64]$journal.artifactBytes) { throw 'The append journal staging closure does not match its recorded count and bytes.' }
$journalArtifacts = @($journal.artifacts)
if ($journalArtifacts.Count -ne $files.Count) { throw 'The append journal artifact inventory does not match its staged files.' }
$journalByRelativePath = @{}
foreach ($artifact in $journalArtifacts) {
    $artifactProperties = @($artifact.PSObject.Properties.Name | Sort-Object)
    $expectedArtifactProperties = @('artifactId','relativePath','byteSize','sha256','r2ObjectKey','purpose','retentionStatus','transferDisposition','remotelyVerifiedUtc') | Sort-Object
    if (@(Compare-Object -ReferenceObject $expectedArtifactProperties -DifferenceObject $artifactProperties).Count -ne 0) { throw 'The append journal contains an unrecognized artifact record.' }
    $relativePath = [string]$artifact.relativePath
    $prefix = "$expectedDestination/"
    if (-not $relativePath.StartsWith($prefix, [StringComparison]::Ordinal) -or $journalByRelativePath.ContainsKey($relativePath)) { throw 'The append journal artifact path is outside its destination or duplicated.' }
    if ([string]$artifact.sha256 -notmatch '^[A-F0-9]{64}$') { throw 'The append journal artifact hash is invalid.' }
    $expectedObjectKey = Get-Gate0ObjectKey ([string]$artifact.sha256)
    if ([string]$artifact.r2ObjectKey -ne $expectedObjectKey) { throw 'The append journal artifact object key is not bound to its SHA-256.' }
    $journalByRelativePath[$relativePath] = $artifact
}
foreach ($file in $files) {
    $sourceRelative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\','/')
    Assert-Gate0EvidenceRelativePath $sourceRelative 'recovery source artifact path'
    $retainedRelative = "$expectedDestination/$sourceRelative"
    if (-not $journalByRelativePath.ContainsKey($retainedRelative)) { throw 'The staged recovery closure contains a file absent from the append journal.' }
    $record = $journalByRelativePath[$retainedRelative]
    if ([int64]$record.byteSize -ne $file.Length -or [string]$record.sha256 -ne (Get-Gate0EvidenceSha256 $file.FullName)) { throw 'A staged recovery artifact differs from its journal-bound size or SHA-256.' }
}

$reviewJournal = "$journalPath.owner-review-$([Guid]::NewGuid().ToString('N'))"
[IO.File]::Move($journalPath, $reviewJournal)
try {
    $producerIdentity = @($journal.producerRuntimeIdentity | ForEach-Object { [string]$_ }) + @($OwnerReviewIdentity)
    $arguments = @{
        ArtifactRoot = $artifactRootResolved
        SourceRoot = $source
        ProofRunId = [string]$journal.proofRunId
        EvidenceGroupId = [string]$journal.evidenceGroupId
        CellId = [string]$journal.cellId
        DestinationName = [string]$journal.destinationName
        EvidenceBoundary = [string]$journal.evidenceBoundary
        Disposition = $Disposition
        ContractIdentity = @($journal.contractIdentity | ForEach-Object { [string]$_ })
        Provenance = "$([string]$journal.provenance) Owner-reviewed recovery disposition: $Disposition."
        ProducerRuntimeIdentity = $producerIdentity
        LicenseRecords = @($journal.licenseRecords | ForEach-Object { [string]$_ })
    }
    if ($null -ne $journal.attemptBindingsRelativePath -and -not [string]::IsNullOrWhiteSpace([string]$journal.attemptBindingsRelativePath)) {
        Assert-Gate0EvidenceRelativePath ([string]$journal.attemptBindingsRelativePath) 'journal attempt bindings path'
        $arguments.AttemptBindingsPath = Join-Path $source ([string]$journal.attemptBindingsRelativePath).Replace('/', [IO.Path]::DirectorySeparatorChar)
    }
    if ($SkipRemoteForIsolatedTest) { $arguments.SkipRemoteForIsolatedTest = $true }
    $result = & (Join-Path $PSScriptRoot 'Add-Gate0EvidenceShard.ps1') @arguments
    Remove-Item -LiteralPath $source -Recurse -Force
    Remove-Item -LiteralPath $reviewJournal -Force
    $result
}
catch {
    if ($movedDestination -and (Test-Path -LiteralPath $source -PathType Container) -and -not (Test-Path -LiteralPath $destination)) { [IO.Directory]::Move($source, $destination) }
    if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf) -and (Test-Path -LiteralPath $reviewJournal -PathType Leaf)) { [IO.File]::Move($reviewJournal, $journalPath) }
    throw
}
