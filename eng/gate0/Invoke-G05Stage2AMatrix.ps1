[CmdletBinding()]
param(
    [string] $RuntimeRoot,
    [string] $ArtifactRoot,
    [string] $StagingRoot,
    [switch] $ExecuteMedia
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$projectParent = [IO.Path]::GetDirectoryName($repositoryRoot)
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AMatrixHelpers.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2SmokeHelpers.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2ASemanticHelpers.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2ASemanticExecutor.psm1') -Force

$schedulePath = Join-Path $PSScriptRoot 'g0.5-stage2a-schedule.json'
$authorizationPath = Join-Path $PSScriptRoot 'g0.5-stage2a-execution-authorization.json'
$workloadContractPath = Join-Path $PSScriptRoot 'g0.5-stage2-workload-contract.json'
$oracleContractPath = Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-contract.json'
$retentionContractPath = Join-Path $PSScriptRoot 'g0.5-stage2a-retention-contract.json'
$runnerHash = Get-G05Stage2ASha256 $PSCommandPath
$schedule = Read-G05Stage2ASchedule $schedulePath
$authorizationRecord = Read-G05Stage2AExecutionAuthorization $authorizationPath $repositoryRoot
$authorization = $authorizationRecord.Authorization
$workloadContract = Get-Content -LiteralPath $workloadContractPath -Raw | ConvertFrom-Json -Depth 100
$retentionContract = Read-G05Stage2ARetentionContract $retentionContractPath

$result = [ordered]@{
    schemaVersion = 1
    runnerId = 'Gate0.G05.Stage2A.MatrixRunner.V1'
    status = if ($ExecuteMedia) { 'live-execution-requested' } else { 'contract-only' }
    noMediaExecuted = -not [bool]$ExecuteMedia
    schedule = [ordered]@{path='eng/gate0/g0.5-stage2a-schedule.json';sha256=$schedule.Sha256;attemptCount=108;cellCount=18}
    runner = [ordered]@{path='eng/gate0/Invoke-G05Stage2AMatrix.ps1';sha256=$runnerHash}
    concurrency = 1
    statistics = [ordered]@{measuredValuesPerCell=5;warmupExcluded=$true;metrics=@('individual-observations','median','minimum','maximum','range','median-absolute-deviation')}
    contracts = [ordered]@{workload=[ordered]@{path='eng/gate0/g0.5-stage2-workload-contract.json';sha256=(Get-G05Stage2ASha256 $workloadContractPath);contractId=$workloadContract.contractId};audioOracle=[ordered]@{path='eng/gate0/g0.5-lossy-audio-oracle-contract.json';sha256=(Get-G05Stage2ASha256 $oracleContractPath)};retention=[ordered]@{path='eng/gate0/g0.5-stage2a-retention-contract.json';sha256=$retentionContract.Sha256;contractId=$retentionContract.Contract.contractId}}
    retention = [ordered]@{writer='Add-Gate0EvidenceShard.ps1';legacyAppendProhibited=$true;oneShardPerCell=$true;ordinaryClosure='first-successfully-completed-measured-attempt';warmup='fully-validated-and-retained-excluded-from-statistics';exceptionalAttempt='complete-closure';compactPassingRecordsPerCell=5;repeatedPassingAttempt='compact-only-after-complete-validation';requiredReservationPerCellBytes=[int64]$retentionContract.Contract.requiredReservationPerCellBytes;ceilingBytes=[int64]$retentionContract.Contract.stage2ARetentionCeilingBytes}
    semanticExecutor = [ordered]@{module='eng/gate0/G05Stage2ASemanticExecutor.psm1';helper='eng/gate0/G05Stage2ASemanticHelpers.psm1';validationSequence=@('strict-encode','strict-probe','packet-and-frame-timing','visual-oracle','audio-timing-and-oracle','process-and-partial-output-cleanup');compactRecordBinds=@('semantic-summary','probe-hash','decoded-video-identity-hash','decoded-audio-identity-hash','output-hash');encodedByteEqualityClaim=$false}
    integrityDisposition = 'A deterministic integrity failure suspends its route and records each remaining affected schedule row as blocked; slowness alone remains evidence.'
    limitations = @('Exact P2 runtime-route only. No product, WPF, cache, preview, shipping-runtime, distribution, or legal claim.')
}
if (-not $ExecuteMedia) { [pscustomobject]$result; return }

if ([string]::IsNullOrWhiteSpace($RuntimeRoot) -or [string]::IsNullOrWhiteSpace($ArtifactRoot) -or [string]::IsNullOrWhiteSpace($StagingRoot)) { throw 'Live execution requires explicit RuntimeRoot, ArtifactRoot, and StagingRoot.' }
if ([string]$authorization.status -ne 'owner-authorized-and-prerequisites-verified') { throw 'Stage 2A execution remains fail-closed until primary review binds the semantic executor and retention contract into an effective authorization.' }
& (Join-Path $PSScriptRoot 'Test-G05Stage2AMatrixPreflight.ps1') -RuntimeRoot $RuntimeRoot -ArtifactRoot $ArtifactRoot -StagingRoot $StagingRoot
throw 'Stage 2A live media execution is intentionally unavailable until the independently reviewed cell semantic-execution and retention implementation is present; no media was started.'
