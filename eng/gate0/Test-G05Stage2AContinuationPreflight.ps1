[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RuntimeRoot,
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [Parameter(Mandatory)] [string] $StagingRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [switch] $RequireRemoteVerification,
    [switch] $AllowCompletedContinuationAudit,
    [switch] $EnableTestInjection,
    [string] $TestObservationPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$repositoryParent = [IO.Path]::GetDirectoryName($repositoryRoot)
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AContinuationHelpers.psm1') -Force

function Get-Hash([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }
function Convert-ToPortableFailure([string] $Message) { [regex]::Replace([regex]::Replace($Message, '(?i)[A-Z]:\\[^\r\n]+', '<absolute-path>'), '(?i)\\\\[^\r\n]+', '<absolute-path>') }
function Assert-ExactProperties([object] $Value, [string[]] $Expected, [string] $Label) {
    if ($null -eq $Value) { throw "$Label is missing." }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object); $wanted = @($Expected | Sort-Object)
    if ($actual.Count -ne $wanted.Count -or @($actual | Where-Object { $_ -notin $wanted }).Count -ne 0 -or @($wanted | Where-Object { $_ -notin $actual }).Count -ne 0) { throw "$Label does not match its closed schema." }
}
function Assert-NoReparse([string] $Path, [string] $StopAt, [string] $Label) {
    $current = Get-Item -LiteralPath $Path -Force
    $stop = [IO.Path]::GetFullPath($StopAt).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label contains a reparse point." }
        if ($current.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Equals($stop, [StringComparison]::OrdinalIgnoreCase)) { return }
        $current = $current.Parent
    }
    throw "$Label does not resolve beneath the repository parent."
}
function Assert-ExactSibling([string] $Path, [string] $Name) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw "$Name must be absolute." }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $expected = (Join-Path $repositoryParent $Name).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (-not $full.Equals($expected, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $full -PathType Container)) { throw "$Name must be the exact existing non-reparse repository sibling." }
    Assert-NoReparse $full $repositoryParent $Name
    foreach ($item in @(Get-ChildItem -LiteralPath $full -Force -Recurse)) { if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Name contains a reparse point." } }
    return $full
}
function Get-TestObservation {
    if ([string]::IsNullOrWhiteSpace($TestObservationPath)) { if ($EnableTestInjection) { throw 'Test observation injection requires TestObservationPath.' }; return $null }
    if (-not $EnableTestInjection -or $env:REELFORGE_GATE0_TEST_INJECTION -ne '1') { throw 'Test observation injection requires both the explicit switch and REELFORGE_GATE0_TEST_INJECTION=1.' }
    if (-not $repositoryRoot.StartsWith(([IO.Path]::GetFullPath([IO.Path]::GetTempPath())), [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.gate0-test-repository-marker') -PathType Leaf)) { throw 'Test observation injection is prohibited outside a dedicated temporary copied test repository.' }
    $value = Get-Content -LiteralPath $TestObservationPath -Raw | ConvertFrom-Json -Depth 16
    Assert-ExactProperties $value @('logicalProcessorCount','totalPhysicalMemoryBytes','availablePhysicalMemoryBytes','activeMediaProcesses','availableFreeSpaceBytes') 'Test observation'
    return $value
}
function Read-Json([string] $Path, [string] $Label) { if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label is missing." }; Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100 -DateKind String }
function Assert-TrackedBinding([object] $Binding, [string] $Role) {
    Assert-ExactProperties $Binding @('role','path','sha256') "Continuation authorization $Role binding"
    if ([string]$Binding.role -ne $Role -or [string]$Binding.path -notmatch '^[a-z0-9][a-z0-9./-]+$' -or [string]$Binding.sha256 -notmatch '^[A-F0-9]{64}$') { throw "Continuation authorization $Role binding is invalid." }
    $path = Join-Path $repositoryRoot ([string]$Binding.path).Replace('/', [IO.Path]::DirectorySeparatorChar)
    if (-not [IO.Path]::GetFullPath($path).StartsWith("$repositoryRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or (Get-Hash $path) -ne [string]$Binding.sha256) { throw "Continuation authorization $Role binding changed." }
}
function Assert-V5Reevaluation([string] $SummaryPath, [string] $AuthorizationPath) {
    $authorization = Read-Json $AuthorizationPath 'V5 reevaluation authorization'
    if ($authorization.authorizationId -ne 'Gate0.G05.Stage2A.V5RetainedOutputReevaluation.20260827' -or $authorization.status -ne 'owner-approved-after-final-v5-freeze' -or $authorization.executionBoundary.reencodeAuthorized -or $authorization.executionBoundary.ffmpegAuthorized -or $authorization.executionBoundary.ffprobeAuthorized -or $authorization.executionBoundary.mediaProcessAuthorized -or -not $authorization.executionBoundary.retainedPcmReadAuthorized) { throw 'V5 reevaluation authorization is not the exact no-media authorization.' }
    $summary = Read-Json $SummaryPath 'V5 reevaluation result summary'
    Assert-ExactProperties $summary @('schemaVersion','summaryId','status','proof','freeze','authorization','reference','routes','executionBoundary','retention','limitations') 'V5 reevaluation result summary'
    Assert-ExactProperties $summary.executionBoundary @('retainedPcmRead','reencodePerformed','ffmpegInvoked','ffprobeInvoked','mediaProcessesStarted','originalV3RecordsModified') 'V5 reevaluation execution boundary'
    Assert-ExactProperties $summary.freeze @('path','sha256') 'V5 reevaluation summary freeze binding'
    Assert-ExactProperties $summary.authorization @('path','sha256') 'V5 reevaluation summary authorization binding'
    if ($summary.freeze.path -ne 'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5-freeze.json' -or $summary.authorization.path -ne 'eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-authorization.json') { throw 'V5 reevaluation summary freeze or authorization path changed.' }
    $freezePath = Join-Path $repositoryRoot 'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5-freeze.json'
    $summaryAuthorizationPath = Join-Path $repositoryRoot 'eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-authorization.json'
    if ($summary.freeze.sha256 -ne (Get-Hash $freezePath) -or $summary.authorization.sha256 -ne (Get-Hash $summaryAuthorizationPath)) { throw 'V5 reevaluation summary freeze or authorization binding changed.' }
    if ($authorization.finalFreeze.filename -ne [IO.Path]::GetFileName($freezePath) -or $authorization.finalFreeze.sha256 -ne (Get-Hash $freezePath) -or $authorization.finalFreeze.freezeId -ne 'Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.Frozen.20260827') { throw 'V5 reevaluation authorization final-freeze binding changed.' }
    if ($summary.schemaVersion -ne 1 -or $summary.summaryId -ne 'Gate0.G05.Stage2A.V5RetainedOutputReevaluation.ResultSummary.20260827' -or $summary.status -ne 'passed-no-media-continuation-prerequisite' -or -not $summary.executionBoundary.retainedPcmRead -or $summary.executionBoundary.reencodePerformed -or $summary.executionBoundary.ffmpegInvoked -or $summary.executionBoundary.ffprobeInvoked -or $summary.executionBoundary.mediaProcessesStarted -or $summary.executionBoundary.originalV3RecordsModified) { throw 'V5 reevaluation did not pass within its no-media execution boundary.' }
    $routes = @($summary.routes)
    $routeIds = @($routes | ForEach-Object { [string]$_.routeId } | Sort-Object -Unique)
    if ($routes.Count -ne 2 -or ($routeIds -join '|') -ne 'mp4-h264-aac|webm-vp9-opus' -or @($routes | Where-Object { -not [bool]$_.v5Passed -or [int]$_.failureCount -ne 0 }).Count -ne 0) { throw 'Both exact retained V5 routes must pass with zero failures before continuation.' }
    Assert-ExactProperties $summary.retention @('v2ProofRunId','v2ShardSha256','v2RootIndexSha256','archiveSha256','archiveManifestSha256','localByteVerified','r2IndependentlyRetrievedAndByteVerified') 'V5 reevaluation retention'
    if (-not $summary.retention.localByteVerified -or -not $summary.retention.r2IndependentlyRetrievedAndByteVerified) { throw 'V5 reevaluation retention is not independently local/R2 verified.' }
    return [ordered]@{ summaryPath='eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json'; summarySha256=(Get-Hash $SummaryPath); authorizationPath='eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-authorization.json'; authorizationSha256=(Get-Hash $AuthorizationPath); routes=@($routes | ForEach-Object { [string]$_.routeId }) }
}

$evidence = [ordered]@{ schemaVersion=1; preflightId='Gate0.G05.Stage2A.ContinuationPreflight.V1'; status='blocked'; checkedUtc=[DateTimeOffset]::UtcNow.ToString('O'); noMediaInvoked=$true; remoteVerificationRequired=[bool]$RequireRemoteVerification; criteria=[ordered]@{}; bindings=[ordered]@{}; retention=$null; host=$null; failures=@(); nonClaims=@('No media source was opened, decoded, rendered, or encoded.','This preflight creates no product, shipping-runtime, distribution, legal, or public hardware-floor claim.','A passed result authorizes no work beyond the separately owner-approved continuation boundary.') }
$output = $null
try {
    $artifact = Assert-ExactSibling $ArtifactRoot 'ReelForge.Gate0Artifacts'; $staging = Assert-ExactSibling $StagingRoot 'ReelForge.Gate0Staging'
    $output = [IO.Path]::GetFullPath($OutputDirectory); if ([IO.Path]::GetDirectoryName($output) -ne $staging -or (Test-Path -LiteralPath $output)) { throw 'OutputDirectory must be a new direct child of StagingRoot.' }; [IO.Directory]::CreateDirectory($output) | Out-Null
    $runtime = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd([IO.Path]::DirectorySeparatorChar); $expectedRuntime = Join-Path $artifact 'p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1'
    if (-not $runtime.Equals($expectedRuntime, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $runtime 'bin/ffmpeg.exe') -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $runtime 'bin/ffprobe.exe') -PathType Leaf)) { throw 'RuntimeRoot must be the exact retained P2 runtime with ffmpeg and ffprobe.' }
    $manifestPath = Join-Path $PSScriptRoot 'manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json'; $manifest = Read-Json $manifestPath 'P2 runtime manifest'
    if ($manifest.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820' -or (Get-Hash (Join-Path $runtime 'bin/ffmpeg.exe')) -ne $manifest.primaryTool.sha256 -or (Get-Hash (Join-Path $runtime 'bin/ffprobe.exe')) -ne $manifest.inspectionTool.sha256) { throw 'The active runtime does not match the exact approved P2 profile.' }; $evidence.criteria.p2Runtime = 'passed'
    $schedulePath = Join-Path $PSScriptRoot 'g0.5-stage2a-continuation-schedule.json'; $authorizationPath = Join-Path $PSScriptRoot 'g0.5-stage2a-continuation-authorization.json'
    $authorization = Read-G05Stage2AContinuationAuthorization $authorizationPath $repositoryRoot $schedulePath
    $schedule = [ordered]@{ path='eng/gate0/g0.5-stage2a-continuation-schedule.json'; sha256=$authorization.Schedule.Sha256; attemptCount=@($authorization.Schedule.Schedule.attempts).Count; cellCount=@($authorization.Schedule.ProofRunIds).Count }
    $evidence.bindings.schedule = $schedule; $evidence.bindings.authorization = [ordered]@{path='eng/gate0/g0.5-stage2a-continuation-authorization.json';sha256=$authorization.Sha256;authorizationId=$authorization.Authorization.authorizationId}; $evidence.criteria.scheduleAndAuthorization = 'passed'
    & (Join-Path $PSScriptRoot 'Test-Gate0EvidenceContainment.ps1') -ArtifactRoot $artifact -RequireEffectiveSeal -ExcludeSeparatelyValidatedV2Namespace | Out-Null
    $legacyRetention = & (Join-Path $PSScriptRoot 'Test-Gate0ArtifactRetention.ps1') -ArtifactRoot $artifact -ValidateIndexedFutureEvidenceSeparately
    if ($null -eq $legacyRetention -or [string]$legacyRetention.status -ne 'verified') { throw 'The retained Gate 0 corpus is not locally verified.' }
    $durableManifest = & (Join-Path $PSScriptRoot 'Test-Gate0ArtifactManifest.ps1')
    if ($null -eq $durableManifest -or [string]::IsNullOrWhiteSpace([string]$durableManifest.sourceManifestSha256)) { throw 'The durable artifact-manifest binding is not verified.' }
    $v2Result = & (Join-Path $PSScriptRoot 'Test-Gate0EvidenceV2Containment.ps1') -ArtifactRoot $artifact
    $v2RootPath = Join-Path $PSScriptRoot 'evidence/v2/root-index.json'
    $v2Root = Read-Json $v2RootPath 'V2 root index'
    $infrastructureRuns = @($v2Root.runs | Where-Object { $_.runKind -eq 'infrastructure' })
    $continuationRuns = @($v2Root.runs | Where-Object { $_.runKind -eq 'stage2a-continuation-cell' })
    $maximumContinuationRuns=if($AllowCompletedContinuationAudit){12}else{11}
    if ($infrastructureRuns.Count -ne 2 -or $continuationRuns.Count -gt $maximumContinuationRuns -or $v2Result.runCount -ne ($infrastructureRuns.Count + $continuationRuns.Count) -or [int64]$v2Result.logicalArtifactBytes -ne [int64]$v2Root.totals.logicalArtifactBytes -or -not $v2Result.localByteVerificationPerformed) { throw 'V2 containment is not the exact authorized continuation shard state.' }
    $scheduleCells = @($authorization.Schedule.Schedule.attempts | Group-Object proofRunId | ForEach-Object { $_.Group[0] })
    if ($scheduleCells.Count -ne 12) { throw 'The fixed continuation schedule does not define exactly twelve ordered cell identities.' }
    for ($i = 0; $i -lt $continuationRuns.Count; $i++) {
        if ($continuationRuns[$i].runKind -ne 'stage2a-continuation-cell' -or $continuationRuns[$i].disposition -ne 'authoritative' -or $continuationRuns[$i].evidenceGroupId -ne 'g05-stage2a-continuation-20260827' -or [string]$continuationRuns[$i].proofRunId -ne [string]$scheduleCells[$i].proofRunId -or [string]$continuationRuns[$i].cellId -ne [string]$scheduleCells[$i].cellId) {
            throw 'Current V2 continuation identities are not the exact ordered prefix of the frozen continuation schedule.'
        }
    }
    if ($RequireRemoteVerification) {
        $legacyRemote = & (Join-Path $PSScriptRoot 'Test-Gate0EvidenceContainment.ps1') -ArtifactRoot $artifact -RequireEffectiveSeal -ExcludeSeparatelyValidatedV2Namespace -Remote
        if (-not $legacyRemote.remoteByteVerificationPerformed -or $legacyRemote.remotelyVerifiedThisRun -ne $legacyRemote.logicalArtifactCount) { throw 'Exact remote V1 evidence byte verification did not complete.' }
        $durableRemote = & (Join-Path $PSScriptRoot 'Test-Gate0ArtifactManifest.ps1') -Remote
        if (-not $durableRemote.remoteByteVerificationPerformed -or $durableRemote.sourceManifestSha256 -ne $durableManifest.sourceManifestSha256 -or $durableRemote.remotelyVerifiedThisRun -ne $durableRemote.selectedLogicalArtifactCount -or $durableRemote.recordedRemoteVerifiedLogicalArtifacts -ne $durableRemote.requiredLogicalArtifactCount -or $durableRemote.selectedLogicalArtifactCount -ne $durableRemote.requiredLogicalArtifactCount -or [int64]$durableRemote.selectedLogicalArtifactBytes -ne 1024859725) { throw 'Exact remote durable source-inventory verification did not complete.' }
        $remote = & (Join-Path $PSScriptRoot 'Test-Gate0EvidenceV2Containment.ps1') -ArtifactRoot $artifact -Remote
        if (-not $remote.remoteByteVerificationPerformed -or $remote.remotelyVerifiedThisRun -ne $remote.logicalArtifactCount) { throw 'Exact remote V2 byte verification did not complete.' }
    }
    $evidence.criteria.v1AndV2Containment = 'passed'; $evidence.bindings.corpus=[ordered]@{legacyManifestSha256=[string]$legacyRetention.manifestSha256;durableSourceManifestSha256=[string]$durableManifest.sourceManifestSha256}; $evidence.bindings.v2=[ordered]@{rootPath='eng/gate0/evidence/v2/root-index.json';rootSha256=(Get-Hash $v2RootPath);logicalArtifactBytes=[int64]$v2Result.logicalArtifactBytes;infrastructureShardCount=$infrastructureRuns.Count;continuationShardCount=$continuationRuns.Count;continuationSchedulePrefixProofRunIds=@($continuationRuns | ForEach-Object {[string]$_.proofRunId});remoteVerified=[bool]$RequireRemoteVerification}
    $evidence.bindings.v5 = Assert-V5Reevaluation (Join-Path $PSScriptRoot 'g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json') (Join-Path $PSScriptRoot 'g0.5-stage2a-v5-retained-output-reevaluation-authorization.json');
    if ($evidence.bindings.v5.summarySha256 -ne (Get-Hash (Join-Path $PSScriptRoot 'g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json'))) { throw 'V5 reevaluation summary binding changed.' }
    $v5Summary = Read-Json (Join-Path $PSScriptRoot 'g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json') 'V5 reevaluation result summary'
    $v5Run = @($v2Root.runs | Where-Object { $_.proofRunId -eq [string]$v5Summary.retention.v2ProofRunId })
    if ($v5Run.Count -ne 1 -or $v5Run[0].runKind -ne 'infrastructure' -or [int]$v5Run[0].ordinal -ne 2 -or $v5Run[0].shardSha256 -ne $v5Summary.retention.v2ShardSha256) { throw 'V5 reevaluation does not bind the immutable V2 infrastructure shard.' }
    if ($continuationRuns.Count -eq 0 -and $v5Summary.retention.v2RootIndexSha256 -ne $evidence.bindings.v2.rootSha256) { throw 'The pre-continuation V5 reevaluation does not bind the exact V2 root.' }
    $evidence.criteria.v5Reevaluation = 'passed'
    $obs = Get-TestObservation; if ($null -eq $obs) { $memory=Get-CimInstance Win32_OperatingSystem; $obs=[pscustomobject]@{logicalProcessorCount=[Environment]::ProcessorCount;totalPhysicalMemoryBytes=([int64]$memory.TotalVisibleMemorySize*1KB);availablePhysicalMemoryBytes=([int64]$memory.FreePhysicalMemory*1KB);activeMediaProcesses=@(Get-Process -Name ffmpeg,ffprobe -ErrorAction SilentlyContinue | ForEach-Object {[ordered]@{id=$_.Id;processName=$_.ProcessName}});availableFreeSpaceBytes=([IO.DriveInfo]::new([IO.Path]::GetPathRoot($artifact))).AvailableFreeSpace} }
    if ([int]$obs.logicalProcessorCount -ne 16 -or [int64]$obs.totalPhysicalMemoryBytes -lt 32212254720 -or [int64]$obs.availablePhysicalMemoryBytes -lt 8589934592 -or @($obs.activeMediaProcesses).Count -ne 0) { throw 'The reference-host capacity or zero-active-media-process requirement is not satisfied.' }
    $current=[int64]78538843+[int64]$v2Result.logicalArtifactBytes; $perCell=[int64]38878888; $cellsRemaining=12-$continuationRuns.Count; $ceiling=[int64]805306368; $remaining=$ceiling-$current; $requiredRetentionReservation=$perCell*$cellsRemaining
    $completedAudit=($cellsRemaining-eq0)
    if($completedAudit -and -not $AllowCompletedContinuationAudit){throw 'Completed continuation state requires the explicit AllowCompletedContinuationAudit switch.'}
    if(-not$completedAudit-and$remaining-lt$requiredRetentionReservation){throw 'The V1-plus-V2 retention state cannot reserve every remaining authorized continuation cell.'}
    $freeFloor=[int64]3758096384; $required=if($completedAudit){$freeFloor}else{[Math]::Max($freeFloor,([int64]2147483648+(2*$perCell)))}; if([int64]$obs.availableFreeSpaceBytes -lt $required){throw 'The shared artifact/staging volume lacks the approved continuation free-space floor.'}
    $evidence.host=[ordered]@{logicalProcessorCount=[int]$obs.logicalProcessorCount;totalPhysicalMemoryBytes=[int64]$obs.totalPhysicalMemoryBytes;availablePhysicalMemoryBytes=[int64]$obs.availablePhysicalMemoryBytes;activeMediaProcessCount=@($obs.activeMediaProcesses).Count}; $evidence.retention=[ordered]@{v1PredecessorBytes=78538843;v2CurrentBytes=[int64]$v2Result.logicalArtifactBytes;currentRetainedBytes=$current;globalCeilingBytes=$ceiling;requiredReservationPerCellBytes=$perCell;continuationCellsAlreadyRetained=$continuationRuns.Count;continuationCellsRemaining=$cellsRemaining;completedContinuationAudit=$completedAudit;requiredReservationForRemainingCellsBytes=$requiredRetentionReservation;remainingAfterFullContinuationReservationBytes=$remaining-$requiredRetentionReservation;requiredFreeSpaceBytes=$required}; $evidence.criteria.hostAndRetention = 'passed'; $evidence.status='passed'
} catch { $evidence.failures=@((Convert-ToPortableFailure $_.Exception.Message)) }
if ($null -ne $output -and (Test-Path -LiteralPath $output -PathType Container)) { $evidence | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath (Join-Path $output 'g0.5-stage2a-continuation-preflight-evidence.json') -Encoding utf8NoBOM }
if ($evidence.status -ne 'passed') { Write-Error ($evidence.failures -join [Environment]::NewLine); exit 1 }
[pscustomobject]@{status='passed';evidence='g0.5-stage2a-continuation-preflight-evidence.json';noMediaInvoked=$true}

