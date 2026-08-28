Set-StrictMode -Version Latest

$script:V1ScheduleRelativePath = 'eng/gate0/g0.5-stage2a-schedule.json'
$script:V1ScheduleSha256 = 'C16D4A65EDDEA2A6213C0A60D371BE605FD3295EE2695EF2716762EA2F85B90E'
$script:ContinuationScheduleRelativePath = 'eng/gate0/g0.5-stage2a-continuation-schedule.json'
$script:ContinuationEvidenceGroupId = 'g05-stage2a-continuation-20260827'
$script:ContinuationAuthorizationRoles = [ordered]@{
    'owner-approval' = 'docs/gate-0-g0.5-stage2a-continuation-approval.md'
    'v2-shard-recovery-approval' = 'docs/gate-0-g0.5-stage2a-v2-shard-approval.md'
    schedule = 'eng/gate0/g0.5-stage2a-continuation-schedule.json'
    helper = 'eng/gate0/G05Stage2AContinuationHelpers.psm1'
    runner = 'eng/gate0/Invoke-G05Stage2AContinuation.ps1'
    preflight = 'eng/gate0/Test-G05Stage2AContinuationPreflight.ps1'
    'v2-writer-authorization' = 'eng/gate0/g0.5-stage2a-continuation-v2-writer-authorization.json'
    'v2-writer' = 'eng/gate0/Add-Gate0EvidenceV2Shard.ps1'
    'v2-containment' = 'eng/gate0/evidence/Gate0EvidenceContainmentV2.psm1'
    'v2-validator' = 'eng/gate0/Test-Gate0EvidenceV2Containment.ps1'
    'workload-contract' = 'eng/gate0/g0.5-stage2-workload-contract.json'
    'retention-contract' = 'eng/gate0/g0.5-stage2a-retention-contract.json'
    'v5-amendment' = 'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json'
    'v5-freeze' = 'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5-freeze.json'
    'v5-reevaluation-authorization' = 'eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-authorization.json'
    'v5-reevaluation-summary' = 'eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json'
    'v5-audio-module' = 'eng/gate0/G05Stage2AV5AudioOracle.psm1'
    'v5-freeze-validator' = 'eng/gate0/G05Stage2AV5FreezeValidation.psm1'
    'semantic-executor' = 'eng/gate0/G05Stage2ASemanticExecutor.psm1'
    'semantic-helper' = 'eng/gate0/G05Stage2ASemanticHelpers.psm1'
    'smoke-helper' = 'eng/gate0/G05Stage2SmokeHelpers.psm1'
    'marker-helper' = 'eng/gate0/G05MarkerSurvivabilityHelpers.psm1'
    'runtime-validator' = 'eng/gate0/Validate-P2Runtime.ps1'
    'runtime-manifest' = 'eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json'
    'fixture-inventory' = 'eng/gate0/fixture-source-inventory.json'
    'artifact-manifest' = 'eng/gate0/artifact-manifest.json'
    'legacy-evidence-validator' = 'eng/gate0/Test-Gate0EvidenceContainment.ps1'
    'artifact-retention-validator' = 'eng/gate0/Test-Gate0ArtifactRetention.ps1'
    'artifact-manifest-validator' = 'eng/gate0/Test-Gate0ArtifactManifest.ps1'
    'legacy-evidence-containment' = 'eng/gate0/evidence/Gate0EvidenceContainment.psm1'
    'artifact-tools' = 'eng/gate0/Gate0ArtifactTools.psm1'
    'r2-client-source' = 'eng/gate0/Gate0ArtifactR2Client.cs'
}

function Get-G05Stage2AContinuationSha256([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required file is missing: $Path" }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-G05Stage2AContinuationExactProperties([object] $Value, [string[]] $Expected, [string] $Label) {
    if ($null -eq $Value) { throw "$Label is missing." }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    if (@(Compare-Object -ReferenceObject $wanted -DifferenceObject $actual).Count -ne 0) { throw "$Label does not match its closed schema." }
}

function Assert-G05Stage2AContinuationRelativePath([string] $Path, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '^[A-Za-z]:|^[/\\]' -or $Path -match '(^|[/\\])\.\.([/\\]|$)' -or $Path -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$') {
        throw "$Label is not a safe repository-relative path."
    }
}

function Read-G05Stage2AContinuationSchedule([string] $Path, [string] $RepositoryRoot) {
    $raw = Get-Content -LiteralPath $Path -Raw
    $schedule = $raw | ConvertFrom-Json -Depth 32
    Assert-G05Stage2AContinuationExactProperties $schedule @('schemaVersion','scheduleId','status','sourceSchedule','evidenceGroupId','counterbalanceRule','excludedGroups','attempts','limitations') 'Stage 2A continuation schedule'
    if ($schedule.schemaVersion -ne 2 -or $schedule.scheduleId -ne 'Gate0.G05.Stage2A.ContinuationSchedule.V2' -or $schedule.status -ne 'owner-approved-single-replacement-before-media' -or $schedule.evidenceGroupId -ne $script:ContinuationEvidenceGroupId) {
        throw 'Stage 2A continuation schedule identity is invalid.'
    }
    Assert-G05Stage2AContinuationExactProperties $schedule.sourceSchedule @('path','sha256','includedOriginalScheduleOrdinalStart','includedOriginalScheduleOrdinalEnd') 'Stage 2A continuation source schedule'
    if ($schedule.sourceSchedule.path -ne $script:V1ScheduleRelativePath -or $schedule.sourceSchedule.sha256 -ne $script:V1ScheduleSha256 -or [int]$schedule.sourceSchedule.includedOriginalScheduleOrdinalStart -ne 37 -or [int]$schedule.sourceSchedule.includedOriginalScheduleOrdinalEnd -ne 108) {
        throw 'Stage 2A continuation schedule source binding is invalid.'
    }
    if ((Get-G05Stage2AContinuationSha256 (Join-Path $RepositoryRoot $script:V1ScheduleRelativePath)) -ne $script:V1ScheduleSha256) { throw 'The immutable V1 schedule bytes changed.' }
    if ((@($schedule.excludedGroups) -join '|') -ne 'baseline-720p|typical-720p' -or [string]$schedule.counterbalanceRule -ne 'Exact projection of original Stage 2A schedule rows 37-108 preserves the approved group, candidate, and warmup/measured order; no resequencing is permitted.' -or @($schedule.attempts).Count -ne 72) {
        throw 'Stage 2A continuation schedule shape or counterbalance rule is invalid.'
    }

    $v1 = Get-Content -LiteralPath (Join-Path $RepositoryRoot $script:V1ScheduleRelativePath) -Raw | ConvertFrom-Json -Depth 32
    $expected = @($v1.attempts | Where-Object { [int]$_.globalOrdinal -ge 37 -and [int]$_.globalOrdinal -le 108 })
    if ($expected.Count -ne 72) { throw 'The immutable V1 schedule cannot supply the approved continuation projection.' }
    $cellProofIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($i = 0; $i -lt 72; $i++) {
        $actual = $schedule.attempts[$i]; $source = $expected[$i]
        Assert-G05Stage2AContinuationExactProperties $actual @('globalOrdinal','groupOrdinal','groupId','cellId','workloadId','resolutionId','candidateId','routeId','threadPolicyId','phase','cellAttemptOrdinal','phaseOrdinal','continuationOrdinal','originalScheduleOrdinal','proofRunId') "Stage 2A continuation attempt $($i + 1)"
        if ([int]$actual.globalOrdinal -ne ($i + 109) -or [int]$actual.continuationOrdinal -ne ($i + 1) -or [int]$actual.originalScheduleOrdinal -ne ($i + 37)) { throw 'Stage 2A continuation ordinals are not exact.' }
        foreach ($property in @('groupOrdinal','groupId','cellId','workloadId','resolutionId','candidateId','routeId','threadPolicyId','phase','cellAttemptOrdinal','phaseOrdinal')) {
            if ([string]$actual.$property -ne [string]$source.$property) { throw "Stage 2A continuation attempt projection changed $property." }
        }
        $proofId = if ($i -lt 6) { 'g05-stage2a-continuation-r1-20260827-stress-720p-webm-eight' } else { "$($script:ContinuationEvidenceGroupId)-$($actual.cellId)" }
        if ([string]$actual.proofRunId -ne $proofId) { throw 'Stage 2A continuation proof run identifier is invalid.' }
        [void]$cellProofIds.Add($proofId)
    }
    if ($cellProofIds.Count -ne 12) { throw 'Stage 2A continuation must contain exactly twelve cell proof run identifiers.' }
    [pscustomobject]@{ Schedule=$schedule; Sha256=(Get-G05Stage2AContinuationSha256 $Path); ProofRunIds=@($cellProofIds | Sort-Object) }
}

function Read-G05Stage2AContinuationAuthorization([string] $Path, [string] $RepositoryRoot, [string] $SchedulePath) {
    $schedule = Read-G05Stage2AContinuationSchedule $SchedulePath $RepositoryRoot
    $authorization = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 32
    Assert-G05Stage2AContinuationExactProperties $authorization @('schemaVersion','authorizationId','authorizationScope','status','exactCellCount','exactAttemptCount','maximumNewCellCount','scheduleBinding','bindings','continuationProofRunIds','limitations') 'Stage 2A continuation authorization'
    if ($authorization.schemaVersion -ne 2 -or $authorization.authorizationId -ne 'Gate0.G05.Stage2A.ContinuationAuthorization.V2' -or $authorization.authorizationScope -ne 'owner-authorized-stage2a-single-replacement' -or [string]$authorization.status -ne 'owner-authorized-single-replacement-effective' -or [int]$authorization.exactCellCount -ne 12 -or [int]$authorization.exactAttemptCount -ne 72 -or [int]$authorization.maximumNewCellCount -ne 1) { throw 'Stage 2A continuation authorization identity or exact matrix counts are invalid.' }
    Assert-G05Stage2AContinuationExactProperties $authorization.scheduleBinding @('path','sha256') 'Stage 2A continuation authorization schedule binding'
    if ($authorization.scheduleBinding.path -ne $script:ContinuationScheduleRelativePath -or $authorization.scheduleBinding.sha256 -ne $schedule.Sha256) { throw 'Stage 2A continuation authorization schedule binding changed.' }
    $expectedProofIds = @($schedule.ProofRunIds | Sort-Object)
    $rawProofIds = @($authorization.continuationProofRunIds | ForEach-Object { [string]$_ })
    $actualProofIds = @($rawProofIds | Sort-Object -Unique)
    if ($rawProofIds.Count -ne 12 -or @($rawProofIds | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -ne 0 -or $actualProofIds.Count -ne 12 -or ($actualProofIds -join '|') -ne ($expectedProofIds -join '|')) { throw 'Stage 2A continuation authorization proof run identifiers are not exact.' }
    if (@($authorization.bindings).Count -ne $script:ContinuationAuthorizationRoles.Count) { throw 'Stage 2A continuation authorization does not have the exact required binding count.' }
    $roles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($binding in @($authorization.bindings)) {
        Assert-G05Stage2AContinuationExactProperties $binding @('role','path','sha256') 'Stage 2A continuation authorization binding'
        if (-not $roles.Add([string]$binding.role) -or -not $script:ContinuationAuthorizationRoles.Contains([string]$binding.role) -or [string]$binding.path -ne $script:ContinuationAuthorizationRoles[[string]$binding.role] -or [string]$binding.sha256 -notmatch '^[A-F0-9]{64}$') { throw 'Stage 2A continuation authorization binding is invalid or duplicated.' }
        Assert-G05Stage2AContinuationRelativePath ([string]$binding.path) 'Stage 2A continuation authorization binding path'
        $boundPath = Join-Path $RepositoryRoot ([string]$binding.path).Replace('/', [IO.Path]::DirectorySeparatorChar)
        if ((Get-G05Stage2AContinuationSha256 $boundPath) -ne [string]$binding.sha256) { throw "Stage 2A continuation authorization binding changed: $($binding.role)." }
    }
    foreach ($role in $script:ContinuationAuthorizationRoles.Keys) { if (-not $roles.Contains([string]$role)) { throw "Stage 2A continuation authorization is missing the $role binding." } }
    [pscustomobject]@{ Authorization=$authorization; Sha256=(Get-G05Stage2AContinuationSha256 $Path); Schedule=$schedule }
}

Export-ModuleMember -Function Get-G05Stage2AContinuationSha256,Assert-G05Stage2AContinuationExactProperties,Read-G05Stage2AContinuationSchedule,Read-G05Stage2AContinuationAuthorization
