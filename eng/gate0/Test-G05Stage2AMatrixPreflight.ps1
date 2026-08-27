[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RuntimeRoot,
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [Parameter(Mandatory)] [string] $StagingRoot,
    [switch] $PerCell,
    [int64] $ExpectedOrdinaryClosureBytes = 0,
    [int64] $ExpectedCompactRepeatBytes = 0,
    [int64] $ExpectedExceptionalClosureBytes = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$projectParent = [IO.Path]::GetDirectoryName($repositoryRoot)
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AMatrixHelpers.psm1') -Force

function Assert-G05Stage2AExactSibling([string] $Path, [string] $Name) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $expected = (Join-Path $projectParent $Name).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $full.Equals($expected, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $full -PathType Container)) { throw "$Name must be the exact existing non-reparse repository sibling." }
    foreach ($item in @((Get-Item -LiteralPath $full -Force)) + @(Get-ChildItem -LiteralPath $full -Force -Recurse)) { if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Name contains a reparse point." } }
    $full
}

$schedulePath = Join-Path $PSScriptRoot 'g0.5-stage2a-schedule.json'
$workloadPath = Join-Path $PSScriptRoot 'g0.5-stage2-workload-contract.json'
$authorizationPath = Join-Path $PSScriptRoot 'g0.5-stage2a-execution-authorization.json'
$containmentContractPath = Join-Path $PSScriptRoot 'g0.5-stage2-containment-dry-run-contract.json'
$runtimeValidatorPath = Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1'
$runtimeManifestPath = Join-Path $PSScriptRoot 'manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json'
$observation = [ordered]@{ scope = if ($PerCell) { 'per-cell' } else { 'full-matrix' }; checkedUtc=[DateTimeOffset]::UtcNow.ToString('O'); criteria=[ordered]@{}; environment=$null; reservation=$null; bindings=[ordered]@{}; noMediaInvoked=$true }
$runtimeEvidencePath = $null

try {
    $artifact = Assert-G05Stage2AExactSibling $ArtifactRoot 'ReelForge.Gate0Artifacts'
    $staging = Assert-G05Stage2AExactSibling $StagingRoot 'ReelForge.Gate0Staging'
    $runtime = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $expectedRuntime = Join-Path $artifact 'p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1'
    if (-not $runtime.Equals($expectedRuntime, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $runtime 'bin/ffmpeg.exe') -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $runtime 'bin/ffprobe.exe') -PathType Leaf)) { throw 'RuntimeRoot must be the exact retained P2 runtime with ffmpeg and ffprobe.' }
    $observation.criteria.roots = 'passed'
    $runtimeEvidencePath = Join-Path $staging ".stage2a-runtime-validation-$([Guid]::NewGuid().ToString('N')).json"
    & $runtimeValidatorPath -RuntimeRoot $runtime -ManifestPath $runtimeManifestPath -EvidencePath $runtimeEvidencePath | Out-Null
    if (-not (Test-Path -LiteralPath $runtimeEvidencePath -PathType Leaf)) { throw 'Exact P2 runtime validation did not produce identity evidence.' }
    $runtimeIdentity = Get-Content -LiteralPath $runtimeEvidencePath -Raw | ConvertFrom-Json -Depth 100
    if ($runtimeIdentity.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820' -or -not [bool]$runtimeIdentity.assessment.MatchesProfile -or @($runtimeIdentity.assessment.Issues).Count -ne 0) { throw 'The active runtime does not match the exact approved P2 profile.' }
    $observation.runtimeIdentity = [ordered]@{
        profileId = [string]$runtimeIdentity.profileId
        manifestSha256 = Get-G05Stage2ASha256 $runtimeManifestPath
        validatorSha256 = Get-G05Stage2ASha256 $runtimeValidatorPath
        primaryTool = [ordered]@{relativePath='bin/ffmpeg.exe';sha256=[string]$runtimeIdentity.observation.PrimaryTool.Sha256;version=[string]$runtimeIdentity.observation.PrimaryTool.Version}
        inspectionTool = [ordered]@{relativePath='bin/ffprobe.exe';sha256=[string]$runtimeIdentity.observation.InspectionTool.Sha256;version=[string]$runtimeIdentity.observation.InspectionTool.Version}
        runtimeFiles = @($runtimeIdentity.observation.RuntimeFiles | ForEach-Object { [ordered]@{relativePath=[string]$_.RelativePath;sha256=[string]$_.Sha256} })
        matchesProfile = $true
    }
    $observation.criteria.exactP2RuntimeIdentity = 'passed'
    $schedule = Read-G05Stage2ASchedule $schedulePath
    $observation.bindings.schedule = [ordered]@{path='eng/gate0/g0.5-stage2a-schedule.json';sha256=$schedule.Sha256}
    $workload = Get-Content -LiteralPath $workloadPath -Raw | ConvertFrom-Json -Depth 100
    if ($workload.contractId -ne 'Gate0.G05.Stage2.Workloads.V1.OwnerApproved.20260826' -or $workload.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820') { throw 'Frozen Stage 2 workload contract identity changed.' }
    $observation.bindings.workload = [ordered]@{path='eng/gate0/g0.5-stage2-workload-contract.json';sha256=(Get-G05Stage2ASha256 $workloadPath)}
    $observation.bindings.containment = [ordered]@{path='eng/gate0/g0.5-stage2-containment-dry-run-contract.json';sha256=(Get-G05Stage2ASha256 $containmentContractPath)}
    if (-not (Test-Path -LiteralPath $authorizationPath -PathType Leaf)) { throw 'The hash-bound Stage 2A execution authorization is missing.' }
    $authorization = Read-G05Stage2AExecutionAuthorization $authorizationPath $repositoryRoot
    $observation.bindings.authorization = [ordered]@{path='eng/gate0/g0.5-stage2a-execution-authorization.json';sha256=$authorization.Sha256;status=$authorization.Authorization.status}
    & (Join-Path $PSScriptRoot 'Test-Gate0EvidenceContainment.ps1') -ArtifactRoot $artifact -RequireEffectiveSeal | Out-Null
    $observation.criteria.sealedCorpusAndFutureIndex = 'passed'
    if (-not $PerCell) { & (Join-Path $PSScriptRoot 'Test-Gate0ArtifactRetention.ps1') -ArtifactRoot $artifact | Out-Null; $observation.criteria.fullLegacyCorpus = 'passed' }
    $environment = Get-G05Stage2AEnvironmentObservation
    $observation.environment = $environment
    if ([int]$environment.logicalProcessorCount -ne 16 -or [int64]$environment.totalPhysicalMemoryBytes -lt 32212254720 -or [int64]$environment.availablePhysicalMemoryBytes -lt 8589934592) { throw 'The owner reference-host processor or memory floor is not satisfied.' }
    if (@($environment.activeMediaProcesses).Count -ne 0) { throw 'An active ffmpeg or ffprobe process blocks the Stage 2A preflight.' }
    $observation.criteria.referenceHostAndCleanMediaProcesses = 'passed'
    $currentFutureBytes = [int64]((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/root-index.json') -Raw | ConvertFrom-Json -Depth 16).totals.logicalArtifactBytes)
    if ($PerCell -and ($ExpectedOrdinaryClosureBytes -le 0 -or $ExpectedCompactRepeatBytes -le 0 -or $ExpectedExceptionalClosureBytes -le 0)) { throw 'Per-cell preflight requires positive contract-bound ordinary, compact-repeat, and exceptional-closure reservations.' }
    $observation.reservation = Get-G05Stage2AReservation $currentFutureBytes $ExpectedOrdinaryClosureBytes $ExpectedCompactRepeatBytes $ExpectedExceptionalClosureBytes
    if (-not $observation.reservation.passed) { throw 'The Stage 2A evidence ceiling cannot reserve the next cell without exceeding 768 MiB.' }
    $fixedHostReserveBytes = [int64]2147483648
    $fullPreflightFloorBytes = [int64]3758096384
    $incrementalPeakBytes = $fixedHostReserveBytes + (2 * [int64]$observation.reservation.requiredForNextCellBytes)
    $requiredFreeSpaceBytes = if ($PerCell) { [Math]::Max($fullPreflightFloorBytes, $incrementalPeakBytes) } else { $fullPreflightFloorBytes }
    $artifactDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($artifact))
    $stagingDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($staging))
    $observation.storage = [ordered]@{artifactAvailableFreeSpaceBytes=[int64]$artifactDrive.AvailableFreeSpace;stagingAvailableFreeSpaceBytes=[int64]$stagingDrive.AvailableFreeSpace;requiredFreeSpaceBytes=$requiredFreeSpaceBytes;fixedHostReserveBytes=$fixedHostReserveBytes;fullPreflightFloorBytes=$fullPreflightFloorBytes;incrementalPeakFormula='2 GiB fixed host reserve plus two times the retained next-cell reservation; never below the approved 3.5 GiB full-preflight floor'}
    if ($artifactDrive.AvailableFreeSpace -lt $requiredFreeSpaceBytes -or $stagingDrive.AvailableFreeSpace -lt $requiredFreeSpaceBytes) { throw 'Artifact or staging volume lacks the approved full/pre-cell free-space floor.' }
    $observation.criteria.incrementalRetentionReservation = 'passed'
    $observation.status = 'passed'
}
catch { $observation.status = 'blocked'; $observation.failure = $_.Exception.Message; throw }
finally {
    if ($null -ne $runtimeEvidencePath -and (Test-Path -LiteralPath $runtimeEvidencePath -PathType Leaf)) { Remove-Item -LiteralPath $runtimeEvidencePath -Force }
    $observation
}
