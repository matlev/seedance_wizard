[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [Parameter(Mandatory)] [string] $SourceRoot,
    [Parameter(Mandatory)] [string] $ProofRunId,
    [Parameter(Mandatory)] [string] $EvidenceGroupId,
    [Parameter(Mandatory)] [string] $CellId,
    [Parameter(Mandatory)] [string] $DestinationName,
    [ValidateSet('containment-no-media','p2-runtime-route')] [string] $EvidenceBoundary = 'p2-runtime-route',
    [ValidateSet('authoritative','passed','failed','blocked','superseded')] [string] $Disposition = 'authoritative',
    [Parameter(Mandatory)] [string[]] $ContractIdentity,
    [Parameter(Mandatory)] [string] $Provenance,
    [Parameter(Mandatory)] [string[]] $ProducerRuntimeIdentity,
    [string[]] $LicenseRecords = @(),
    [string] $AttemptBindingsPath = '',
    [ValidateSet('None','AfterRemoteVerification','AfterPayloadMove','AfterShardMove')] [string] $FaultInjection = 'None',
    [switch] $SkipRemoteForIsolatedTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'evidence/Gate0EvidenceContainment.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AMatrixHelpers.psm1') -Force

function Assert-DirectoryTreeHasNoReparsePoint([string] $Root, [string] $Label) {
    foreach ($item in @((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label contains a reparse point: $($item.FullName)" }
    }
}

function Assert-IsolatedTestMode([string] $RepositoryRoot, [string] $ResolvedArtifactRoot) {
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $RepositoryRoot.StartsWith("$temporary$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
        -not $ResolvedArtifactRoot.StartsWith("$temporary$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot '.gate0-containment-test-sentinel') -PathType Leaf) -or
        (Test-Path -LiteralPath (Join-Path $RepositoryRoot '.git'))) {
        throw 'Fault injection and remote bypass are permitted only in an isolated copied test repository.'
    }
}

function Get-Gate0EvidenceArtifactId([string] $PortableRelativePath) {
    $pathBytes = [Text.Encoding]::UTF8.GetBytes($PortableRelativePath)
    $pathHash = [Security.Cryptography.SHA256]::HashData($pathBytes)
    "artifact-$([Convert]::ToHexString($pathHash).ToLowerInvariant())"
}

function Assert-Stage2AExecutionAuthorization([string] $RepositoryRoot, [string[]] $Identities) {
    $relativePath = 'eng/gate0/g0.5-stage2a-execution-authorization.json'
    $authorizationPath = Join-Path $RepositoryRoot $relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $authorizationPath -PathType Leaf)) {
        throw 'Stage 2A evidence append is blocked until the exact schedule and runner authorization is present and valid.'
    }
    Assert-Gate0EvidenceNoReparsePointAncestors $authorizationPath $RepositoryRoot
    $text = Get-Content -LiteralPath $authorizationPath -Raw
    Assert-Gate0EvidenceMetadataText $text 'Stage 2A execution authorization'
    $verified = Read-G05Stage2AExecutionAuthorization $authorizationPath $RepositoryRoot
    if ([string]$verified.Authorization.status -ne 'owner-authorized-and-prerequisites-verified') {
        throw 'Stage 2A evidence append is blocked until the exact execution authorization is effective.'
    }
    foreach ($binding in @($verified.Authorization.bindings)) {
        $boundPath = Join-Path $RepositoryRoot ([string]$binding.path).Replace('/', [IO.Path]::DirectorySeparatorChar)
        Assert-Gate0EvidenceNoReparsePointAncestors $boundPath $RepositoryRoot
    }
    $authorizationSha = [string]$verified.Sha256
    if ($Identities -notcontains "repository:$relativePath" -or $Identities -notcontains "sha256:$authorizationSha") {
        throw 'Stage 2A evidence must bind the exact tracked execution authorization identity.'
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$rootIndexPath = Join-Path $PSScriptRoot 'evidence/root-index.json'
$stage2Directory = Join-Path $PSScriptRoot 'evidence/stage2'
if (-not (Test-Path -LiteralPath $stage2Directory -PathType Container)) { [IO.Directory]::CreateDirectory($stage2Directory) | Out-Null }
Assert-Gate0EvidenceIdentifier $ProofRunId 'ProofRunId'
Assert-Gate0EvidenceIdentifier $EvidenceGroupId 'EvidenceGroupId'
Assert-Gate0EvidenceIdentifier $CellId 'CellId'
Assert-Gate0EvidenceRelativePath $DestinationName 'DestinationName'
foreach ($identity in $ContractIdentity) {
    if ($identity -notmatch '^(repository|sha256):[A-Za-z0-9._/-]+$' -or $identity.Contains('\') -or $identity.Contains('..')) { throw 'ContractIdentity must use a portable repository: or sha256: scope.' }
}
if ($EvidenceBoundary -eq 'p2-runtime-route') { Assert-Stage2AExecutionAuthorization $repositoryRoot $ContractIdentity }
if ([string]::IsNullOrWhiteSpace($Provenance)) { throw 'Provenance is required.' }
foreach ($identity in @($ProducerRuntimeIdentity) + @($LicenseRecords)) {
    if ($identity -notmatch '^(repository|sha256):[A-Za-z0-9._/-]+$' -or $identity.Contains('\') -or $identity.Contains('..')) { throw 'ProducerRuntimeIdentity and LicenseRecords must use portable repository: or sha256: scopes.' }
}
if ($ProducerRuntimeIdentity.Count -eq 0) { throw 'ProducerRuntimeIdentity is required.' }
$source = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'SourceRoot does not exist.' }
Assert-DirectoryTreeHasNoReparsePoint $source 'SourceRoot'
Assert-Gate0EvidenceNoReparsePointAncestors $source ([IO.Path]::GetDirectoryName($repositoryRoot))
$sourceFiles = @(Get-ChildItem -LiteralPath $source -File -Recurse | Sort-Object FullName)
if ($sourceFiles.Count -eq 0) { throw 'SourceRoot contains no evidence files.' }

$artifactRootResolved = Resolve-Gate0ArtifactRoot $ArtifactRoot
Assert-Gate0EvidenceNoReparsePointAncestors $artifactRootResolved ([IO.Path]::GetDirectoryName($repositoryRoot))
if ($FaultInjection -ne 'None' -or $SkipRemoteForIsolatedTest) { Assert-IsolatedTestMode $repositoryRoot $artifactRootResolved }
$expectedDestinationName = "future/stage2/$ProofRunId"
if ($DestinationName -ne $expectedDestinationName) { throw "DestinationName must be the dedicated future evidence namespace: $expectedDestinationName" }
$destination = [IO.Path]::GetFullPath((Join-Path $artifactRootResolved $DestinationName.Replace('/', [IO.Path]::DirectorySeparatorChar)))
$shardRelativePath = "stage2/$ProofRunId.manifest.json"
$shardPath = Join-Path $stage2Directory "$ProofRunId.manifest.json"

$lockPath = "$artifactRootResolved.stage2-append-lock"
$journalPath = "$artifactRootResolved.stage2-append-journal.json"
$lock = $null
$staging = "$artifactRootResolved.stage2-staging-$([Guid]::NewGuid().ToString('N'))"
$shardTemporary = Join-Path (Split-Path -Parent $stage2Directory) ".stage2-shard-$ProofRunId-$([Guid]::NewGuid().ToString('N')).tmp"
$payloadMoved = $false
$shardMoved = $false
$rootCommitted = $false
$preserveFailureEvidence = $false
try { $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { throw 'Another Gate 0 Stage 2 evidence append is active.' }
try {
    if (Test-Path -LiteralPath $journalPath -PathType Leaf) { throw 'A prior Stage 2 evidence append journal requires owner review before another append.' }
    $seal = Assert-Gate0LegacyEvidenceSeal $repositoryRoot -RequireEffective
    $root = $seal.Root
    if (-not $destination.StartsWith("$artifactRootResolved$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $destination)) { throw 'Future evidence destination exists or escaped the artifact root.' }
    if (Test-Path -LiteralPath $shardPath) { throw 'The immutable evidence shard already exists.' }
    if (@($root.Index.runs | Where-Object { $_.proofRunId -eq $ProofRunId -or $_.cellId -eq $CellId }).Count -ne 0) { throw 'The root index already contains this proof-run or cell ID.' }
    & (Join-Path $PSScriptRoot 'Test-Gate0EvidenceContainment.ps1') -ArtifactRoot $artifactRootResolved -RequireEffectiveSeal | Out-Null
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    $artifactRecords = [Collections.Generic.List[object]]::new()
    $seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\','/')
        Assert-Gate0EvidenceRelativePath $relative 'Source evidence path'
        $portableDestination = "$DestinationName/$relative"
        Assert-Gate0EvidenceRelativePath $portableDestination 'Retained evidence path'
        $target = Join-Path $staging $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $sha = Get-Gate0EvidenceSha256 $target
        if ((Get-Item -LiteralPath $target).Length -ne $file.Length -or $sha -ne (Get-Gate0EvidenceSha256 $file.FullName)) { throw 'Evidence staging copy failed byte verification.' }
        $artifactId = Get-Gate0EvidenceArtifactId $portableDestination
        Assert-Gate0EvidenceIdentifier $artifactId 'Generated artifactId'
        if (-not $seenIds.Add($artifactId)) { throw "Evidence files produced a deterministic artifact ID collision for $portableDestination." }
        $artifactRecords.Add([pscustomobject][ordered]@{
            artifactId = $artifactId
            relativePath = $portableDestination
            byteSize = [int64]$file.Length
            sha256 = $sha
            r2ObjectKey = Get-Gate0ObjectKey $sha
            purpose = "Gate 0 evidence retained for $ProofRunId."
            retentionStatus = 'remote-verified'
            transferDisposition = 'deduplicated-object-verified-in-this-run'
            remotelyVerifiedUtc = '2099-12-31T23:59:59.9999999+00:00'
        })
    }

    $attemptBindings = @()
    if (-not [string]::IsNullOrWhiteSpace($AttemptBindingsPath)) {
        $attemptPath = [IO.Path]::GetFullPath($AttemptBindingsPath)
        if (-not $attemptPath.StartsWith("$source$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $attemptPath -PathType Leaf)) { throw 'AttemptBindingsPath must be one retained file under SourceRoot.' }
        $attemptBindings = @(Get-Content -LiteralPath $attemptPath -Raw | ConvertFrom-Json -Depth 32)
    }
    $totalBytes = [int64]0
    foreach ($record in $artifactRecords) { $totalBytes += [int64]$record.byteSize }
    if ([int64]$root.Index.totals.logicalArtifactBytes + $totalBytes -gt [int64]$root.Index.limits.stage2ARetentionCeilingBytes) { throw 'Candidate evidence would exceed the approved Stage 2A retention ceiling.' }
    $shard = [ordered]@{
        schemaVersion = 1
        shardId = 'Gate0.Stage2Evidence.Shard.V1'
        proofRunId = $ProofRunId
        evidenceGroupId = $EvidenceGroupId
        cellId = $CellId
        evidenceBoundary = $EvidenceBoundary
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
        contractIdentity = @($ContractIdentity)
        provenance = $Provenance
        producerRuntimeIdentity = @($ProducerRuntimeIdentity)
        licenseRecords = @($LicenseRecords)
        artifacts = @($artifactRecords)
        attempts = @($attemptBindings)
        disposition = $Disposition
        localRetention = 'verified'
        r2Retention = 'independently-retrieved-and-verified'
        totals = [ordered]@{ logicalArtifactCount = $artifactRecords.Count; logicalArtifactBytes = $totalBytes }
        limitations = @('This shard records Gate 0 proof infrastructure only and is not a product, shipping-runtime, distribution, or legal conclusion.')
    }
    [IO.File]::WriteAllText($shardTemporary, (($shard | ConvertTo-Json -Depth 32 -Compress) + "`n"), [Text.UTF8Encoding]::new($false))
    [void](Read-Gate0EvidenceShard $shardTemporary)
    if ([int64]$root.Shape.Bytes + 8192 -gt [int64]$root.Index.limits.maxRootIndexBytes -or [int]$root.Shape.Lines + 40 -gt [int]$root.Index.limits.maxRootIndexLines) { throw 'Candidate root index lacks conservative capacity for one bounded append.' }

    $journal = [ordered]@{
        schemaVersion = 1
        proofRunId = $ProofRunId
        evidenceGroupId = $EvidenceGroupId
        cellId = $CellId
        evidenceBoundary = $EvidenceBoundary
        disposition = $Disposition
        contractIdentity = @($ContractIdentity)
        provenance = $Provenance
        producerRuntimeIdentity = @($ProducerRuntimeIdentity)
        licenseRecords = @($LicenseRecords)
        attemptBindingsRelativePath = if ([string]::IsNullOrWhiteSpace($AttemptBindingsPath)) { $null } else { [IO.Path]::GetRelativePath($source, [IO.Path]::GetFullPath($AttemptBindingsPath)).Replace('\','/') }
        phase = 'prepared'
        stagingPath = $staging
        destinationName = $DestinationName
        artifactCount = $artifactRecords.Count
        artifactBytes = $totalBytes
        objectKeys = @($artifactRecords | ForEach-Object r2ObjectKey)
        artifacts = @($artifactRecords)
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    Write-Gate0EvidenceUtf8Atomic $journalPath (($journal | ConvertTo-Json -Depth 16) + "`n")

    $bundle = $null
    $verifiedKeys = @{}
    try {
        if (-not $SkipRemoteForIsolatedTest) { $preserveFailureEvidence = $true; $bundle = New-Gate0R2ClientBundle }
        foreach ($record in $artifactRecords) {
            $relative = ([string]$record.relativePath).Substring($DestinationName.Length + 1)
            $target = Join-Path $staging $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
            $transferDisposition = 'independently-retrieved-and-verified'
            if (-not $SkipRemoteForIsolatedTest) {
                $remoteEntry = [pscustomobject]@{ ArtifactId = [string]$record.artifactId; Size = [int64]$record.byteSize; Sha256 = [string]$record.sha256; ObjectKey = [string]$record.r2ObjectKey }
                if ($verifiedKeys.ContainsKey($remoteEntry.ObjectKey)) { $transferDisposition = 'deduplicated-object-verified-in-this-run' }
                else {
                    $metadata = $bundle.Client.HeadObjectAsync($bundle.BucketName, $remoteEntry.ObjectKey).GetAwaiter().GetResult()
                    if ($null -ne $metadata -and $null -ne $metadata.ContentLength -and [int64]$metadata.ContentLength -ne $remoteEntry.Size) { throw 'Existing future-evidence R2 object has the wrong size.' }
                    if ($null -eq $metadata) {
                        $created = $bundle.Client.PutObjectIfAbsentAsync($bundle.BucketName, $remoteEntry.ObjectKey, $target, $remoteEntry.Sha256).GetAwaiter().GetResult()
                        $transferDisposition = if ($created) { 'uploaded-and-verified' } else { 'concurrent-create-verified' }
                    } else { $transferDisposition = 'existing-object-verified' }
                    Invoke-Gate0RemoteByteVerification $bundle $remoteEntry
                    $verifiedKeys[$remoteEntry.ObjectKey] = $true
                }
            }
            $record.transferDisposition = $transferDisposition
            $record.remotelyVerifiedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            $journal.phase = 'remoteInProgress'
            $journal.artifacts = @($artifactRecords)
            Write-Gate0EvidenceUtf8Atomic $journalPath (($journal | ConvertTo-Json -Depth 32) + "`n")
        }
    }
    finally { if ($null -ne $bundle) { $bundle.HttpClient.Dispose() } }
    $journal.phase = 'remoteVerified'
    Write-Gate0EvidenceUtf8Atomic $journalPath (($journal | ConvertTo-Json -Depth 16) + "`n")
    if ($FaultInjection -eq 'AfterRemoteVerification') { $preserveFailureEvidence = $true; throw 'Injected containment failure after remote verification.' }

    [IO.File]::WriteAllText($shardTemporary, (($shard | ConvertTo-Json -Depth 32 -Compress) + "`n"), [Text.UTF8Encoding]::new($false))
    $validatedShard = Read-Gate0EvidenceShard $shardTemporary
    $prior = @($root.Index.runs)
    $previous = if ($prior.Count) { $prior[-1] } else { $null }
    $entry = [ordered]@{
        ordinal = $prior.Count + 1
        runKind = if ($EvidenceBoundary -eq 'containment-no-media') { 'infrastructure' } else { 'stage2a-cell' }
        proofRunId = $ProofRunId
        evidenceGroupId = $EvidenceGroupId
        cellId = $CellId
        shardPath = $shardRelativePath
        shardSha256 = $validatedShard.Sha256
        entrySha256 = ''
        previousRunId = if ($null -eq $previous) { $null } else { [string]$previous.proofRunId }
        previousRunEntrySha256 = if ($null -eq $previous) { $null } else { [string]$previous.entrySha256 }
        disposition = $Disposition
        logicalArtifactCount = $artifactRecords.Count
        logicalArtifactBytes = $totalBytes
        localRetention = 'verified'
        r2Retention = 'independently-retrieved-and-verified'
    }
    $entryObject = [pscustomobject]$entry
    $entry.entrySha256 = Get-Gate0EvidenceEntryHash $entryObject
    $candidate = $root.Index.PSObject.Copy()
    $candidate.runs = @($prior) + @([pscustomobject]$entry)
    $candidate.totals.runCount = @($candidate.runs).Count
    $candidate.totals.logicalArtifactCount = [int]$root.Index.totals.logicalArtifactCount + $artifactRecords.Count
    $candidate.totals.logicalArtifactBytes = [int64]$root.Index.totals.logicalArtifactBytes + $totalBytes
    if ([int64]$candidate.totals.logicalArtifactBytes -gt [int64]$candidate.limits.stage2ARetentionCeilingBytes) { throw 'Candidate evidence would exceed the approved Stage 2A retention ceiling.' }
    $candidateText = ($candidate | ConvertTo-Json -Depth 64) + "`n"
    $candidatePath = "$rootIndexPath.tmp-$([Guid]::NewGuid().ToString('N'))"
    [IO.File]::WriteAllText($candidatePath, $candidateText, [Text.UTF8Encoding]::new($false))
    $candidateShape = Get-Gate0EvidenceFileShape $candidatePath
    if ($candidateShape.Bytes -gt 131072 -or $candidateShape.Lines -gt 400) { throw 'Candidate root index exceeds its approved cap.' }

    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    Assert-Gate0EvidenceNoReparsePointAncestors (Split-Path -Parent $destination) $artifactRootResolved
    [IO.Directory]::Move($staging, $destination)
    $payloadMoved = $true
    if ($FaultInjection -eq 'AfterPayloadMove') { throw 'Injected containment failure after payload move.' }
    [IO.File]::Move($shardTemporary, $shardPath)
    $shardMoved = $true
    if ($FaultInjection -eq 'AfterShardMove') { throw 'Injected containment failure after shard move.' }
    [void](Read-Gate0EvidenceRootIndex $candidatePath)
    Write-Gate0EvidenceUtf8Atomic $rootIndexPath $candidateText
    $rootCommitted = $true
    [void](Assert-Gate0LegacyEvidenceSeal $repositoryRoot -RequireEffective)
    Remove-Item -LiteralPath $journalPath -Force
    $preserveFailureEvidence = $false
    [pscustomobject]@{
        proofRunId = $ProofRunId
        cellId = $CellId
        shardPath = $shardRelativePath
        shardSha256 = $validatedShard.Sha256
        rootIndexSha256 = Get-Gate0EvidenceSha256 $rootIndexPath
        logicalArtifactCount = $artifactRecords.Count
        logicalArtifactBytes = $totalBytes
        localRetention = 'verified'
        r2Retention = 'independently-retrieved-and-verified'
    }
}
catch {
    if (-not $rootCommitted -and -not $preserveFailureEvidence) {
        if ($shardMoved -and (Test-Path -LiteralPath $shardPath -PathType Leaf)) { Remove-Item -LiteralPath $shardPath -Force }
        if ($payloadMoved -and (Test-Path -LiteralPath $destination -PathType Container)) { Remove-Item -LiteralPath $destination -Recurse -Force }
        if (Test-Path -LiteralPath $journalPath -PathType Leaf) { Remove-Item -LiteralPath $journalPath -Force }
    }
    throw
}
finally {
    if (-not $preserveFailureEvidence) {
        foreach ($temporary in @($staging, $shardTemporary, "$rootIndexPath.tmp-*")) {
            Get-Item -Path $temporary -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
        }
    }
    if ($null -ne $lock) { $lock.Dispose() }
}
