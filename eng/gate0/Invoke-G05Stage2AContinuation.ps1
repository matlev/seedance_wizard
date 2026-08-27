[CmdletBinding()]
param([string] $RuntimeRoot,[string] $ArtifactRoot,[string] $StagingRoot,[switch] $ExecuteMedia,[switch] $AllowCompletedContinuationAudit)

# Separate continuation runner. It never loads, redirects, or edits the accepted V1 runner.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$schedulePath=Join-Path $PSScriptRoot 'g0.5-stage2a-continuation-schedule.json'
$authorizationPath=Join-Path $PSScriptRoot 'g0.5-stage2a-continuation-authorization.json'
$preflightPath=Join-Path $PSScriptRoot 'Test-G05Stage2AContinuationPreflight.ps1'
$v2WriterPath=Join-Path $PSScriptRoot 'Add-Gate0EvidenceV2Shard.ps1'
$v2ValidatorPath=Join-Path $PSScriptRoot 'Test-Gate0EvidenceV2Containment.ps1'
$v2WriterAuthorizationPath=Join-Path $PSScriptRoot 'g0.5-stage2a-continuation-v2-writer-authorization.json'
$workloadPath=Join-Path $PSScriptRoot 'g0.5-stage2-workload-contract.json'
$audioContractPath=Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-contract.json'
$v5FreezePath=Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v5-freeze.json'
$v5AmendmentPath=Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v5.json'
$v5ResultPath=Join-Path $PSScriptRoot 'g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json'
$retentionContractPath=Join-Path $PSScriptRoot 'g0.5-stage2a-retention-contract.json'

# Bootstrap deliberately contains no repository imports.  Live authorization binds
# this runner and every later import before any repository-controlled code runs.
function Get-G05Stage2AContinuationBootstrapHash([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required bootstrap file is missing: $Path" }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-G05Stage2AContinuationBootstrapRelativePath([string] $Path, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '^[A-Za-z]:|^[/\\]' -or $Path -match '(^|[/\\])\.\.([/\\]|$)' -or $Path -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$') { throw "$Label is not a safe repository-relative path." }
}

function Assert-G05Stage2AContinuationBootstrapProperties([object] $Value, [string[]] $Expected, [string] $Label) {
    if ($null -eq $Value -or @((Compare-Object -ReferenceObject @($Expected | Sort-Object) -DifferenceObject @($Value.PSObject.Properties.Name | Sort-Object))).Count -ne 0) { throw "$Label does not match its closed bootstrap schema." }
}

function Read-G05Stage2AContinuationBootstrapSchedule([string] $Path) {
    $schedule = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 32
    Assert-G05Stage2AContinuationBootstrapProperties $schedule @('schemaVersion','scheduleId','status','sourceSchedule','evidenceGroupId','counterbalanceRule','excludedGroups','attempts','limitations') 'Continuation schedule'
    Assert-G05Stage2AContinuationBootstrapProperties $schedule.sourceSchedule @('path','sha256','includedOriginalScheduleOrdinalStart','includedOriginalScheduleOrdinalEnd') 'Continuation schedule source binding'
    if ($schedule.schemaVersion -ne 1 -or [string]$schedule.scheduleId -ne 'Gate0.G05.Stage2A.ContinuationSchedule.V1' -or [string]$schedule.status -ne 'owner-approved-fixed-before-continuation-media' -or [string]$schedule.evidenceGroupId -ne 'g05-stage2a-continuation-20260827' -or @($schedule.attempts).Count -ne 72) { throw 'Continuation schedule bootstrap identity is invalid.' }
    foreach($attempt in @($schedule.attempts)){Assert-G05Stage2AContinuationBootstrapProperties $attempt @('globalOrdinal','groupOrdinal','groupId','cellId','workloadId','resolutionId','candidateId','routeId','threadPolicyId','phase','cellAttemptOrdinal','phaseOrdinal','continuationOrdinal','originalScheduleOrdinal','proofRunId') 'Continuation schedule attempt'}
    $proofIds = @($schedule.attempts | ForEach-Object { [string]$_.proofRunId } | Sort-Object -Unique)
    if ($proofIds.Count -ne 12 -or @($schedule.attempts | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.proofRunId) -or [int]$_.globalOrdinal -lt 109 -or [int]$_.globalOrdinal -gt 180 }).Count) { throw 'Continuation schedule bootstrap shape is invalid.' }
    [pscustomobject]@{ Schedule=$schedule; Sha256=(Get-G05Stage2AContinuationBootstrapHash $Path); ProofRunIds=$proofIds }
}

function Read-G05Stage2AContinuationBootstrapAuthorization([string] $Path, [object] $Schedule) {
    $expectedRoles = [ordered]@{
        'owner-approval'='docs/gate-0-g0.5-stage2a-continuation-approval.md'; schedule='eng/gate0/g0.5-stage2a-continuation-schedule.json'; helper='eng/gate0/G05Stage2AContinuationHelpers.psm1'; runner='eng/gate0/Invoke-G05Stage2AContinuation.ps1'; preflight='eng/gate0/Test-G05Stage2AContinuationPreflight.ps1'; 'v2-writer-authorization'='eng/gate0/g0.5-stage2a-continuation-v2-writer-authorization.json'; 'v2-writer'='eng/gate0/Add-Gate0EvidenceV2Shard.ps1'; 'v2-containment'='eng/gate0/evidence/Gate0EvidenceContainmentV2.psm1'; 'v2-validator'='eng/gate0/Test-Gate0EvidenceV2Containment.ps1'; 'workload-contract'='eng/gate0/g0.5-stage2-workload-contract.json'; 'retention-contract'='eng/gate0/g0.5-stage2a-retention-contract.json'; 'v5-amendment'='eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json'; 'v5-freeze'='eng/gate0/g0.5-lossy-audio-oracle-amendment-v5-freeze.json'; 'v5-reevaluation-authorization'='eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-authorization.json'; 'v5-reevaluation-summary'='eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json'; 'v5-audio-module'='eng/gate0/G05Stage2AV5AudioOracle.psm1'; 'v5-freeze-validator'='eng/gate0/G05Stage2AV5FreezeValidation.psm1'; 'semantic-executor'='eng/gate0/G05Stage2ASemanticExecutor.psm1'; 'semantic-helper'='eng/gate0/G05Stage2ASemanticHelpers.psm1'; 'smoke-helper'='eng/gate0/G05Stage2SmokeHelpers.psm1'; 'marker-helper'='eng/gate0/G05MarkerSurvivabilityHelpers.psm1'; 'runtime-validator'='eng/gate0/Validate-P2Runtime.ps1'; 'runtime-manifest'='eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json'; 'fixture-inventory'='eng/gate0/fixture-source-inventory.json'; 'artifact-manifest'='eng/gate0/artifact-manifest.json'; 'legacy-evidence-validator'='eng/gate0/Test-Gate0EvidenceContainment.ps1'; 'artifact-retention-validator'='eng/gate0/Test-Gate0ArtifactRetention.ps1'; 'artifact-manifest-validator'='eng/gate0/Test-Gate0ArtifactManifest.ps1'; 'legacy-evidence-containment'='eng/gate0/evidence/Gate0EvidenceContainment.psm1'; 'artifact-tools'='eng/gate0/Gate0ArtifactTools.psm1'; 'r2-client-source'='eng/gate0/Gate0ArtifactR2Client.cs'
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Full continuation authorization is absent. No media was started.' }
    $authorization = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 32
    Assert-G05Stage2AContinuationBootstrapProperties $authorization @('schemaVersion','authorizationId','authorizationScope','status','exactCellCount','exactAttemptCount','scheduleBinding','bindings','continuationProofRunIds','limitations') 'Full continuation authorization'
    Assert-G05Stage2AContinuationBootstrapProperties $authorization.scheduleBinding @('path','sha256') 'Full continuation authorization schedule binding'
    if ($authorization.schemaVersion -ne 1 -or [string]$authorization.authorizationId -ne 'Gate0.G05.Stage2A.ContinuationAuthorization.V1' -or [string]$authorization.authorizationScope -ne 'owner-authorized-stage2a-continuation' -or [string]$authorization.status -ne 'owner-authorized-continuation-effective' -or [int]$authorization.exactCellCount -ne 12 -or [int]$authorization.exactAttemptCount -ne 72) { throw 'Full continuation authorization identity is not effective. No media was started.' }
    if ([string]$authorization.scheduleBinding.path -ne 'eng/gate0/g0.5-stage2a-continuation-schedule.json' -or [string]$authorization.scheduleBinding.sha256 -ne $Schedule.Sha256) { throw 'Full continuation authorization schedule binding changed. No media was started.' }
    $proofIds=@($authorization.continuationProofRunIds | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    if ($proofIds.Count -ne 12 -or ($proofIds -join '|') -ne (@($Schedule.ProofRunIds | Sort-Object) -join '|')) { throw 'Full continuation authorization proof identifiers are not exact. No media was started.' }
    if (@($authorization.bindings).Count -ne $expectedRoles.Count) { throw 'Full continuation authorization binding count is invalid. No media was started.' }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($binding in @($authorization.bindings)) {
        Assert-G05Stage2AContinuationBootstrapProperties $binding @('role','path','sha256') 'Full continuation authorization binding'
        $role=[string]$binding.role; $relative=[string]$binding.path; $hash=[string]$binding.sha256
        if (-not $seen.Add($role) -or -not $expectedRoles.Contains($role) -or $relative -ne $expectedRoles[$role] -or $hash -notmatch '^[A-F0-9]{64}$') { throw 'Full continuation authorization binding schema is invalid. No media was started.' }
        Assert-G05Stage2AContinuationBootstrapRelativePath $relative 'Full continuation authorization binding path'
        $boundPath=Join-Path $repositoryRoot $relative.Replace('/',[IO.Path]::DirectorySeparatorChar)
        if ((Get-G05Stage2AContinuationBootstrapHash $boundPath) -ne $hash) { throw "Full continuation authorization binding changed: $role. No media was started." }
    }
    foreach($role in $expectedRoles.Keys) { if(-not $seen.Contains([string]$role)) { throw "Full continuation authorization binding is missing $role. No media was started." } }
    [pscustomobject]@{ Authorization=$authorization; Sha256=(Get-G05Stage2AContinuationBootstrapHash $Path) }
}

# Kept in this self-bound runner so live continuation does not import the broader
# V1 matrix helper solely for its five-observation statistics projection.
function Get-G05Stage2AStatistics([double[]] $MeasuredValues) {
    if ($MeasuredValues.Count -ne 5 -or @($MeasuredValues | Where-Object { -not [double]::IsFinite($_) }).Count -ne 0) { throw 'Stage 2A statistics require exactly five finite measured values.' }
    $sorted = @($MeasuredValues | Sort-Object)
    $median = [double]$sorted[2]
    $deviations = @($MeasuredValues | ForEach-Object { [Math]::Abs($_ - $median) } | Sort-Object)
    [ordered]@{
        observations = @($MeasuredValues)
        minimum = [double]$sorted[0]
        maximum = [double]$sorted[-1]
        range = [double]($sorted[-1] - $sorted[0])
        median = $median
        medianAbsoluteDeviation = [double]$deviations[2]
        observationCount = 5
        warmupExcluded = $true
    }
}

function Assert-G05Stage2AContinuationNoActiveMedia([string]$Scope){if(@(Get-Process -Name ffmpeg,ffprobe -ErrorAction SilentlyContinue).Count){throw "Active ffmpeg or ffprobe process blocks continuation $Scope."}}
function Invoke-G05Stage2AContinuationPostAppendValidation([string]$ArtifactRoot){
    $local=& $v2ValidatorPath -ArtifactRoot $ArtifactRoot
    $remote=& $v2ValidatorPath -ArtifactRoot $ArtifactRoot -Remote
    [ordered]@{local=$local;remote=$remote}
}
function Get-G05Stage2AContinuationV2Shard {
    param([object] $Run)
    $v2Root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'evidence/v2')).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $relative=[string]$Run.shardPath
    if([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('\') -or $relative -match '^[A-Za-z]:|^[/\\]' -or $relative -match '(^|[/\\])\.\.([/\\]|$)'){throw 'Retained continuation shard path is unsafe.'}
    $path=[IO.Path]::GetFullPath((Join-Path $v2Root $relative.Replace('/',[IO.Path]::DirectorySeparatorChar)))
    if(-not $path.StartsWith($v2Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Retained continuation shard escapes the V2 repository evidence root.'}
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Retained continuation shard is missing: $($Run.proofRunId)."}
    if((Get-G05SmokeHash $path)-ne[string]$Run.shardSha256){throw "Retained continuation shard is no longer hash-bound: $($Run.proofRunId)."}
    Get-Content -LiteralPath $path -Raw|ConvertFrom-Json -Depth 64
}
function Get-G05Stage2AContinuationRetainedProofIds{param([string]$ArtifactRoot)$root=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/v2/root-index.json') -Raw|ConvertFrom-Json -Depth 32;@($root.runs|Where-Object runKind -eq 'stage2a-continuation-cell'|ForEach-Object{[string]$_.proofRunId})}
function Assert-G05Stage2AContinuationResumePrefix{param([object]$Schedule,[string]$ArtifactRoot)$expected=@($Schedule.attempts|Select-Object -ExpandProperty proofRunId -Unique);$actual=@(Get-G05Stage2AContinuationRetainedProofIds $ArtifactRoot);if($actual.Count-gt$expected.Count){throw 'V2 continuation evidence exceeds the fixed schedule.'};for($i=0;$i-lt$actual.Count;$i++){if($actual[$i]-ne$expected[$i]){throw 'V2 continuation evidence is not the exact completed prefix of the fixed schedule.'}};$actual.Count}
function Restore-G05Stage2AContinuationSuspendedRoutes {
    param([object]$Schedule,[string]$ArtifactRoot)
    $suspended=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $root=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/v2/root-index.json') -Raw|ConvertFrom-Json -Depth 32
    foreach($run in @($root.runs|Where-Object runKind -eq 'stage2a-continuation-cell')) {
        $shard=Get-G05Stage2AContinuationV2Shard $run
        foreach($attempt in @($shard.attempts)) {
            $row=@($Schedule.attempts|Where-Object { [string]$_.proofRunId -eq [string]$run.proofRunId -and [int]$_.globalOrdinal -eq [int]$attempt.ordinal })
            if($row.Count-ne1){throw 'Retained continuation attempt cannot be mapped to exactly one frozen schedule row.'}
            if([string]$attempt.disposition -in @('byte-divergent','semantically-divergent','structurally-divergent')){[void]$suspended.Add([string]$row[0].routeId)}
        }
    }
    $suspended
}
function Get-G05Stage2AContinuationRetainedSummaries {
    param([string]$ArtifactRoot)
    $root=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/v2/root-index.json') -Raw|ConvertFrom-Json -Depth 32
    $summaries=@()
    foreach($run in @($root.runs|Where-Object runKind -eq 'stage2a-continuation-cell')) {
        $shard=Get-G05Stage2AContinuationV2Shard $run
        foreach($attempt in @($shard.attempts)) {
            $relative=[string]$attempt.recordPath
            if([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('\') -or $relative -match '^[A-Za-z]:|^[/\\]' -or $relative -match '(^|[/\\])\.\.([/\\]|$)'){throw 'Retained continuation attempt record path is unsafe.'}
            $artifactFull=[IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
            $path=[IO.Path]::GetFullPath((Join-Path $artifactFull $relative.Replace('/',[IO.Path]::DirectorySeparatorChar)))
            if(-not $path.StartsWith($artifactFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Retained continuation attempt record escapes ArtifactRoot.'}
            if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-G05SmokeHash $path)-ne[string]$attempt.recordSha256){throw 'Retained continuation summary is missing or no longer hash-bound.'}
            $summaries+=,(Get-Content -LiteralPath $path -Raw|ConvertFrom-Json -Depth 100)
        }
    }
    @($summaries)
}
function Invoke-G05Stage2AContinuationCapturedCommand{
    param([string]$Executable,[string[]]$Tokens,[string]$StdoutPath,[string]$StderrPath,[hashtable]$Roots)
    & $Executable @Tokens 1> $StdoutPath 2> $StderrPath
    Protect-G05Stage2AContinuationLog $StdoutPath $Roots
    Protect-G05Stage2AContinuationLog $StderrPath $Roots
    [ordered]@{exitCode=$LASTEXITCODE;stdoutSha256=(Get-G05SmokeHash $StdoutPath);stderrSha256=(Get-G05SmokeHash $StderrPath)}
}
function Protect-G05Stage2AContinuationLog{
    param([string]$Path,[hashtable]$Roots)
    if(Test-Path -LiteralPath $Path -PathType Leaf){
        $text=[IO.File]::ReadAllText($Path)
        foreach($root in $Roots.Values){if(-not[string]::IsNullOrWhiteSpace([string]$root)){$text=$text.Replace([string]$root,'{redacted-root}',[StringComparison]::OrdinalIgnoreCase)}}
        [IO.File]::WriteAllText($Path,$text,[Text.UTF8Encoding]::new($false))
    }
}
function ConvertTo-G05Stage2AContinuationSanitizedText{
    param([string]$Value,[hashtable]$Roots)
    $result=if($null-eq$Value){''}else{$Value}
    foreach($entry in $Roots.GetEnumerator()){$root=[string]$entry.Value;if(-not[string]::IsNullOrWhiteSpace($root)){$result=$result.Replace($root,"{$($entry.Key)}",[StringComparison]::OrdinalIgnoreCase)}}
    $result
}
function Get-G05Stage2AContinuationDescriptor {
    param($Contract, $Route, $Variant, $Probe)
    $expected = @($Contract.markerQualification.requiredRouteQualityProfiles | Where-Object { $_.routeId -eq $Route.id -and $_.qualityProfileId -eq $Route.qualityProfileId })
    $video = @($Probe.streams | Where-Object codec_type -eq 'video')
    $audio = @($Probe.streams | Where-Object codec_type -eq 'audio')
    if ($expected.Count -ne 1 -or $video.Count -ne 1 -or $audio.Count -ne 1) { throw 'Frozen output descriptor has an unexpected stream shape.' }
    $actual = [ordered]@{ formatName=[string]$Probe.format.format_name; videoCodec=[string]$video[0].codec_name; videoProfile=[string]$video[0].profile; pixelFormat=[string]$video[0].pix_fmt; width=[int]$video[0].width; height=[int]$video[0].height; rFrameRate=[string]$video[0].r_frame_rate; avgFrameRate=[string]$video[0].avg_frame_rate; audioCodec=[string]$audio[0].codec_name; audioProfile=if($null -eq $audio[0].PSObject.Properties['profile']){$null}else{[string]$audio[0].profile}; audioSampleRate=[int]$audio[0].sample_rate; audioChannels=[int]$audio[0].channels; audioChannelLayout=[string]$audio[0].channel_layout }
    $wanted = $expected[0].observedDescriptor
    $checks = [ordered]@{ formatName=$actual.formatName -eq $wanted.formatName; videoCodec=$actual.videoCodec -eq $wanted.videoCodec; videoProfile=$actual.videoProfile -eq $wanted.videoProfile; pixelFormat=$actual.pixelFormat -eq $wanted.pixelFormat; width=$actual.width -eq [int]$Variant.width; height=$actual.height -eq [int]$Variant.height; rFrameRate=$actual.rFrameRate -eq $wanted.frameRate; avgFrameRate=$actual.avgFrameRate -eq $wanted.frameRate; audioCodec=$actual.audioCodec -eq $wanted.audioCodec; audioProfile=if($null -eq $wanted.audioProfile){$true}else{$actual.audioProfile -eq $wanted.audioProfile}; audioSampleRate=$actual.audioSampleRate -eq [int]$wanted.audioSampleRate; audioChannels=$actual.audioChannels -eq [int]$wanted.audioChannels; audioChannelLayout=$actual.audioChannelLayout -eq $wanted.audioChannelLayout }
    if (@($checks.Values | Where-Object { -not $_ }).Count) { throw ('Frozen output descriptor mismatch: ' + (@($checks.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object Key) -join ',')) }
    [ordered]@{ passed=$true; expected=[ordered]@{formatName=$wanted.formatName;videoCodec=$wanted.videoCodec;videoProfile=$wanted.videoProfile;pixelFormat=$wanted.pixelFormat;width=[int]$Variant.width;height=[int]$Variant.height;frameRate=$wanted.frameRate;audioCodec=$wanted.audioCodec;audioProfile=$wanted.audioProfile;audioSampleRate=[int]$wanted.audioSampleRate;audioChannels=[int]$wanted.audioChannels;audioChannelLayout=$wanted.audioChannelLayout}; observed=$actual; criteria=$checks; videoStream=$video[0]; audioStream=$audio[0] }
}

function Assert-G05Stage2AContinuationV4Closure {
    param([string]$AudioContractPath)
    $freezePath = Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v4-freeze.json'
    $controlPath = Join-Path $PSScriptRoot 'g0.5-structured-audio-control-result-summary.json'
    $retentionPath = Join-Path $PSScriptRoot 'g0.5-structured-audio-control-retention-result-summary.json'
    foreach($path in @($freezePath,$controlPath,$retentionPath)) { if(-not (Test-Path -LiteralPath $path -PathType Leaf)){ throw 'Frozen V4 audio-oracle closure is incomplete before continuation media.' } }
    $freeze=Get-Content -LiteralPath $freezePath -Raw|ConvertFrom-Json -Depth 64; $control=Get-Content -LiteralPath $controlPath -Raw|ConvertFrom-Json -Depth 64; $retention=Get-Content -LiteralPath $retentionPath -Raw|ConvertFrom-Json -Depth 64
    if([string]$freeze.v3Contract.sha256 -ne (Get-G05SmokeHash $AudioContractPath) -or [string]$freeze.v4Amendment.sha256 -ne (Get-G05SmokeHash (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v4.json')) -or [string]$control.freezeId -ne [string]$freeze.freezeId -or [string]$retention.freeze.retainedSnapshotSha256 -ne (Get-G05SmokeHash $freezePath) -or [string]$control.status -ne 'passed-controls-only-retention-pending' -or [string]$retention.status -ne 'complete' -or -not [bool]$freeze.controlEvidence.allDeclaredDispositionsPassed -or -not [bool]$freeze.controlEvidence.legacyHashesAndDispositionsPreserved -or [bool]$freeze.controlEvidence.routeOutputsEvaluated -or [int]$freeze.controlEvidence.structuredControlCount -ne 5 -or [int]$freeze.controlEvidence.legacyV3ControlCount -ne 12 -or -not [bool]$control.legacyV3Controls.allFrozenHashesAndDispositionsPreserved -or -not [bool]$control.legacyV3Controls.allNoOverlayEffectiveOracleDispositionsPreserved -or [bool]$control.executionBoundaries.routeOutputsEvaluated -or [string]$control.controlReport.sha256 -ne [string]$freeze.controlEvidence.sha256 -or [string]$retention.controlGroup.controlReportSha256 -ne [string]$freeze.controlEvidence.sha256) { throw 'Frozen V4 audio-oracle amendment/control/retention chain does not bind before continuation media.' }
    [ordered]@{audioContractSha256=(Get-G05SmokeHash $AudioContractPath);freezeSha256=(Get-G05SmokeHash $freezePath);controlSha256=(Get-G05SmokeHash $controlPath);retentionSha256=(Get-G05SmokeHash $retentionPath)}
}

function Assert-G05Stage2AContinuationV5Closure {
    param([string]$ArtifactRoot,[string]$StagingRoot,[object]$Reevaluation)
    $archive=Join-Path $ArtifactRoot 'future/stage2/v2/g05-stage2a-v5-infrastructure-20260827T220228625Z/v5-final-evidence.zip'
    if(-not(Test-Path -LiteralPath $archive -PathType Leaf)){throw 'The exact retained V5 final-evidence archive is absent; continuation cannot infer a repo-only closure.'}
    $archiveHash=Get-G05SmokeHash $archive
    if($archiveHash-ne[string]$Reevaluation.retention.archiveSha256){throw 'The exact retained V5 final-evidence archive is not hash-bound by the retained reevaluation summary.'}
    $validatedStagingRoot=[IO.Path]::GetFullPath($StagingRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $closureRoot=[IO.Path]::GetFullPath((Join-Path $validatedStagingRoot ("g05-stage2a-v5-closure-$([Guid]::NewGuid().ToString('N'))")))
    if(-not $closureRoot.StartsWith($validatedStagingRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'V5 closure extraction root escapes the validated staging root.'}
    [IO.Directory]::CreateDirectory($closureRoot)|Out-Null
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip=[IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $required=@('freeze/g0.5-lossy-audio-oracle-amendment-v5-freeze.json','controls/g0.5-v5-stress-audio-oracle-control-results.json','controls/g0.5-v5-stress-audio-oracle-freeze-candidate.json','controls/v4-controls/g0.5-structured-audio-oracle-control-results.json')
            if(@($required|Sort-Object -Unique).Count-ne4){throw 'V5 closure extraction entry contract is not unique.'}
            $paths=@{}
            $destinations=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($entryName in $required){$entry=$zip.GetEntry($entryName);if($null-eq$entry){throw "The exact retained V5 closure entry is absent: $entryName."};$name=[IO.Path]::GetFileName($entryName);if([string]::IsNullOrWhiteSpace($name)-or $name-ne$entryName.Split('/')[-1] -or -not $destinations.Add($name)){throw 'V5 closure extraction destination is unsafe or duplicated.'};$destination=Join-Path $closureRoot $name;$entry.ExtractToFile($destination,$false);$paths[$entryName]=$destination}
        } finally {$zip.Dispose()}
        $closure=Assert-G05Stage2AV5FinalFreezeClosure $paths['freeze/g0.5-lossy-audio-oracle-amendment-v5-freeze.json'] $paths['controls/g0.5-v5-stress-audio-oracle-control-results.json'] $paths['controls/g0.5-v5-stress-audio-oracle-freeze-candidate.json'] $paths['controls/v4-controls/g0.5-structured-audio-oracle-control-results.json'] $PSScriptRoot
        [ordered]@{archiveSha256=$archiveHash;finalFreezeSha256=(Get-G05SmokeHash $paths['freeze/g0.5-lossy-audio-oracle-amendment-v5-freeze.json']);closure=$closure}
    } finally { if($closureRoot.StartsWith($validatedStagingRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)-and(Test-Path -LiteralPath $closureRoot)){Remove-Item -LiteralPath $closureRoot -Recurse -Force} }
}
function Test-G05Stage2AContinuationSemanticCell{
 param($Cell,$Rows,[string]$CellRoot,[string]$RuntimeRoot,[string]$ArtifactRoot,$WorkloadContract,$AudioContract,$V5Amendment,$RetentionContract,[Collections.Generic.HashSet[string]]$SuspendedRoutes)
 $fixture=Join-Path $ArtifactRoot 'fixtures';$ffmpeg=Join-Path $RuntimeRoot 'bin/ffmpeg.exe';$ffprobe=Join-Path $RuntimeRoot 'bin/ffprobe.exe';$portableRoots=@{runtimeRoot=$RuntimeRoot;artifactRoot=$ArtifactRoot;stage=$CellRoot};$truthRoot=Join-Path $CellRoot 'audio-truth';[IO.Directory]::CreateDirectory($truthRoot)|Out-Null
 $descriptor=@($AudioContract.referenceDescriptors|Where-Object id -eq $Cell.Workload.audioReferenceDescriptor);if($descriptor.Count-ne1){throw 'Frozen audio descriptor is absent.'};$truth=Join-Path $truthRoot 'reference.s16le';New-G05Stage2AAudioTruth $fixture $Cell.Workload $truth $descriptor[0]|Out-Null
 $truthRecord=[ordered]@{rawSha256=(Get-G05SmokeHash $truth);contentNormalizedSha256=(Get-G05Stage2AContentNormalizedAudioHash $truth ([int64]$descriptor[0].referencePcmSize) 1024)}
 $overlay=$null;if($Cell.Workload.id-eq'typical-2v4a'){$v4=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v4.json') -Raw|ConvertFrom-Json -Depth 64;$overlay=@($v4.descriptorOverlays|Where-Object referenceDescriptorId -eq $descriptor[0].id)[0];Assert-G05SmokeReferenceRelativeOverlay $descriptor[0] $overlay}elseif($Cell.Workload.id-eq'stress-4v8a'){$overlay=@($V5Amendment.descriptorOverlays|Where-Object referenceDescriptorId -eq $descriptor[0].id)[0];if($overlay.Count-ne1){throw 'The exact V5 stress overlay is absent.'};Assert-G05Stage2AV5StressOverlay $descriptor[0] $overlay}
 $summaries=@()
 foreach($row in $Rows){$attemptRoot=Join-Path $CellRoot ("attempt-$($row.cellAttemptOrdinal)");[IO.Directory]::CreateDirectory($attemptRoot)|Out-Null;$summary=[ordered]@{attemptId="stage2a-continuation-$($row.globalOrdinal)";originalAttemptId="stage2a-$($row.originalScheduleOrdinal)";globalOrdinal=[int]$row.globalOrdinal;phase=[string]$row.phase;disposition='blocked';startedUtc=[DateTimeOffset]::UtcNow.ToString('O');completedUtc=$null;selectedComponents=[ordered]@{inputProfiles=@($Cell.Workload.inputs.profile);filters=@('scale','format','setpts','crop','overlay','aformat','volume','pan','adelay','amix','atrim','asetpts');videoEncoder=$Cell.Route.videoEncoder;audioEncoder=$Cell.Route.audioEncoder;muxer=$Cell.Route.muxer;demuxer=(Get-G05SmokeDemuxer $Cell.Route.muxer);videoDecoder=$Cell.Route.outputDecoders[0];audioDecoder=$Cell.Route.outputDecoders[1];threads=$Cell.Threads;runtimeProfile='P2.BtbnLgplShared.WindowsX64.20260820'};commands=[ordered]@{};validations=[ordered]@{encode=$false;probe=$false;timing=$false;visual=$false;audio=$false;cleanup=$false};hashes=[ordered]@{outputSha256=$null;frameProbeSha256=$null;packetProbeSha256=$null;decodedVideoIdentitySha256=$null;decodedAudioRawSha256=$null;decodedAudioContentNormalizedSha256=$null};encodedByteEqualityClaim=$false;measurement=$null;cleanup=[ordered]@{processStarted=$false;processTreeRootExited=$true;processTreeOrphanFree=$true;noUnvalidatedPartialOutput=$true;partialOutput='none'};failures=@()}
  $v2Root=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/v2/root-index.json') -Raw|ConvertFrom-Json -Depth 32;$localCellBytes=[int64](@(Get-ChildItem -LiteralPath $CellRoot -File -Recurse -ErrorAction SilentlyContinue|ForEach-Object Length|Measure-Object -Sum).Sum);$worstCaseBytes=[int64]78538843+[int64]$v2Root.totals.logicalArtifactBytes+$localCellBytes+[int64]$RetentionContract.Contract.exceptionalClosureBytes
  if($worstCaseBytes-gt805306368){$summary.reason='blocked-before-media-insufficient-global-headroom-for-worst-case-full-closure'}elseif($SuspendedRoutes.Contains([string]$row.routeId)){$summary.reason='route-suspended-after-deterministic-integrity-failure'}else{$output=$null;$measurement=$null;$disposition='blocked';try{
   Assert-G05Stage2AContinuationNoActiveMedia "attempt $($row.globalOrdinal) start";$disposition='failed';$output=Join-Path $attemptRoot ("output.$($Cell.Route.container)");$tokens=New-G05Stage2AEncodeTokens $Cell $WorkloadContract $ArtifactRoot $output;$summary.commands.encode=[ordered]@{executable='{runtimeRoot}/bin/ffmpeg.exe';tokens=(ConvertTo-G05SmokePortableTokens $tokens $portableRoots)};$measurement=Invoke-G05SmokeObservedProcess $ffmpeg $tokens $attemptRoot (Join-Path $attemptRoot 'encode.stdout.txt') (Join-Path $attemptRoot 'encode.stderr.txt');Protect-G05Stage2AContinuationLog (Join-Path $attemptRoot 'encode.stdout.txt') $portableRoots;Protect-G05Stage2AContinuationLog (Join-Path $attemptRoot 'encode.stderr.txt') $portableRoots;$summary.measurement=[ordered]@{startedUtc=$measurement.startedUtc.ToString('O');completedUtc=$measurement.completedUtc.ToString('O');exitCode=[int]$measurement.exitCode;rootPid=[int]$measurement.rootPid;summary=$measurement.summary;samples=@($measurement.samples);processTree=$measurement.processTree};$summary.cleanup.processStarted=$true;$summary.cleanup.processTreeRootExited=[bool]$measurement.processTree.rootExited;$summary.cleanup.processTreeOrphanFree=[bool]$measurement.processTree.orphanFree;[IO.File]::WriteAllLines((Join-Path $attemptRoot 'process-samples.ndjson'),@($measurement.samples|ForEach-Object{$_|ConvertTo-Json -Compress}),[Text.UTF8Encoding]::new($false));if($measurement.exitCode-ne0-or-not$measurement.processTree.orphanFree-or-not(Test-Path $output -PathType Leaf)){throw 'Observed encode did not close successfully and orphan-free.'};$summary.validations.encode=$true
   $demuxer=Get-G05SmokeDemuxer $Cell.Route.muxer;$frames=Join-Path $attemptRoot 'probe-frames.json';$packets=Join-Path $attemptRoot 'probe-packets.json';$frameTokens=@('-v','error','-f',$demuxer,'-show_streams','-show_frames','-show_format','-of','json',$output);$packetTokens=@('-v','error','-f',$demuxer,'-show_packets','-of','json',$output);$summary.commands.probeFrames=[ordered]@{executable='{runtimeRoot}/bin/ffprobe.exe';tokens=(ConvertTo-G05SmokePortableTokens $frameTokens $portableRoots)};$summary.commands.probePackets=[ordered]@{executable='{runtimeRoot}/bin/ffprobe.exe';tokens=(ConvertTo-G05SmokePortableTokens $packetTokens $portableRoots)};if((Invoke-G05Stage2AContinuationCapturedCommand $ffprobe $frameTokens $frames (Join-Path $attemptRoot 'probe-frames.stderr.txt') $portableRoots).exitCode-ne0){throw 'Forced frame probe failed.'};if((Invoke-G05Stage2AContinuationCapturedCommand $ffprobe $packetTokens $packets (Join-Path $attemptRoot 'probe-packets.stderr.txt') $portableRoots).exitCode-ne0){throw 'Forced packet probe failed.'};$disposition='structurally-divergent';$probe=Get-Content $frames -Raw|ConvertFrom-Json -Depth 100;$packetProbe=Get-Content $packets -Raw|ConvertFrom-Json -Depth 100;$actual=Get-G05Stage2AContinuationDescriptor $WorkloadContract $Cell.Route $Cell.Variant $probe;$summary.descriptor=$actual;$summary.videoTiming=Get-G05Stage2AExactVideoTiming $actual.videoStream @($probe.frames|Where-Object media_type -eq 'video');$summary.validations.probe=$true;$summary.validations.timing=$true;$summary.hashes.frameProbeSha256=Get-G05SmokeHash $frames;$summary.hashes.packetProbeSha256=Get-G05SmokeHash $packets
   # The visual helper contains a strict decoder.  Its process/decode failure remains failed;
   # only a successfully returned visual oracle may produce semantic divergence.
   $disposition='failed';$visualLog=Join-Path $attemptRoot 'visual.stderr.txt';$visual=Test-G05Stage2AVisual $ffmpeg $demuxer $Cell.Route.outputDecoders[0] $output $fixture $visualLog (Join-Path $attemptRoot 'visual-mae.ndjson') $Cell.Workload $Cell.Variant;Protect-G05Stage2AContinuationLog $visualLog $portableRoots;$summary.visual=$visual;$disposition='semantically-divergent';if(-not$visual.passed){throw 'Visual oracle failed.'};$summary.validations.visual=$true;$summary.hashes.decodedVideoIdentitySha256=$visual.decodedVideoIdentitySha256
   # A strict decoder invocation is an execution failure until it succeeds; only its semantic oracle can diverge.
   $disposition='failed'
   $pcm=Join-Path $attemptRoot 'decoded-audio.s16le';$audioTokens=@('-v','error','-xerror','-err_detect','explode','-f',$demuxer,'-c:a',$Cell.Route.outputDecoders[1],'-threads:a',[string]$Cell.Threads,'-i',$output,'-map','0:a:0','-vn','-c:a','pcm_s16le','-f','s16le','-y',$pcm);$summary.commands.audioDecode=[ordered]@{executable='{runtimeRoot}/bin/ffmpeg.exe';tokens=(ConvertTo-G05SmokePortableTokens $audioTokens $portableRoots)};if((Invoke-G05Stage2AContinuationCapturedCommand $ffmpeg $audioTokens (Join-Path $attemptRoot 'audio.stdout.txt') (Join-Path $attemptRoot 'audio.stderr.txt') $portableRoots).exitCode-ne0){throw 'Strict native audio decode failed.'};$disposition='semantically-divergent';$quality=if($Cell.Workload.id-eq'stress-4v8a'){Test-G05Stage2AV5StressAudio $truth $pcm $AudioContract.qualityThresholds $descriptor[0] $overlay 1024}else{Test-G05SmokeAudio $truth $pcm $AudioContract.qualityThresholds $descriptor[0] 1024 $overlay};$timing=Get-G05DecodedAudioTiming (Get-Item $pcm).Length $actual.audioStream @($probe.frames|Where-Object media_type -eq 'audio') @($packetProbe.packets|Where-Object codec_type -eq 'audio') 1440000 1024;if(-not$timing.passed-or-not$quality.passed){throw 'Audio timing or quality oracle failed.'};$summary.validations.audio=$true;$summary.audio=[ordered]@{truth=$truthRecord;timing=$timing;quality=$quality};$summary.hashes.decodedAudioRawSha256=Get-G05SmokeHash $pcm;$summary.hashes.decodedAudioContentNormalizedSha256=Get-G05Stage2AContentNormalizedAudioHash $pcm ([int64]$descriptor[0].referencePcmSize) 1024;if($summary.hashes.decodedAudioContentNormalizedSha256-ne[string]$summary.audio.quality.contentNormalized.sha256){throw 'Independent normalized decoded-audio identity hashes differ.'};$summary.hashes.outputSha256=Get-G05SmokeHash $output;$summary.outputBytes=(Get-Item $output).Length
   $disposition='cleanup-failed';Assert-G05Stage2AContinuationNoActiveMedia "attempt $($row.globalOrdinal) completion";$summary.cleanup.noUnvalidatedPartialOutput=$true;$summary.cleanup.partialOutput='not-applicable-complete-output-retained';$summary.validations.cleanup=$true;$summary.disposition='passed'
  }catch{$summary.disposition=$disposition;$summary.failures+=,(ConvertTo-G05Stage2AContinuationSanitizedText $_.Exception.Message $portableRoots);foreach($log in @(Get-ChildItem -LiteralPath $attemptRoot -File -Filter '*.txt' -ErrorAction SilentlyContinue)){Protect-G05Stage2AContinuationLog $log.FullName $portableRoots};if($null-ne$measurement){$summary.cleanup.processTreeRootExited=[bool]$measurement.processTree.rootExited;$summary.cleanup.processTreeOrphanFree=[bool]$measurement.processTree.orphanFree;if(-not$measurement.processTree.orphanFree){$summary.disposition='orphan-producing'}elseif(-not$measurement.processTree.rootExited){$summary.disposition='cleanup-failed'}};if($null-ne$output-and(Test-Path $output -PathType Leaf)){if($null-ne$measurement-and$measurement.exitCode-eq0-and$measurement.processTree.orphanFree){$summary.cleanup.partialOutput='complete-output-retained-for-exceptional-oracle-or-structure-evidence';$summary.cleanup.noUnvalidatedPartialOutput=$true}else{$summary.cleanup.partialOutput=[ordered]@{action='removed';bytes=(Get-Item $output).Length;sha256=(Get-G05SmokeHash $output)};Remove-Item -LiteralPath $output -Force;$summary.cleanup.noUnvalidatedPartialOutput=-not(Test-Path $output)}};try{Assert-G05Stage2AContinuationNoActiveMedia "attempt $($row.globalOrdinal) failure cleanup"}catch{$summary.disposition='orphan-producing';$summary.cleanup.processTreeOrphanFree=$false;$summary.failures+=,(ConvertTo-G05Stage2AContinuationSanitizedText $_.Exception.Message $portableRoots)};$summary.validations.cleanup=[bool]($summary.cleanup.processTreeRootExited-and$summary.cleanup.processTreeOrphanFree-and$summary.cleanup.noUnvalidatedPartialOutput)}}
  $summary.completedUtc=[DateTimeOffset]::UtcNow.ToString('O');Assert-G05Stage2AAttemptSummary $summary;$summaries+=,$summary;if(Test-G05Stage2ADeterministicIntegrityFailure $summary){[void]$SuspendedRoutes.Add([string]$row.routeId)}
 }
 $plan=Resolve-G05Stage2ACellRetentionPlan @($summaries);$mapped=@();foreach($summary in @($plan.attempts)){$attemptRoot=Join-Path $CellRoot ("attempt-$([int]$summary.globalOrdinal-[int]$Rows[0].globalOrdinal+1)");$path=Join-Path $attemptRoot 'summary.json';Write-G05Stage2ASemanticJson $path $summary;$binding=New-G05Stage2AAttemptBinding ($Rows|Where-Object globalOrdinal -eq $summary.globalOrdinal) $summary $path $CellRoot $summary.retentionClass $summary.completeClosureReference;if($summary.retentionClass-eq'compact'){Assert-G05Stage2ACompactBinding $binding $CellRoot ([int64]$RetentionContract.Contract.compactPassingRepeatMaximumBytes);Get-ChildItem -LiteralPath $attemptRoot -Force|Where-Object Name -ne 'summary.json'|Remove-Item -Recurse -Force};$row=@($Rows|Where-Object{[int]$_.globalOrdinal-eq[int]$binding.ordinal});$reference=if([string]::IsNullOrWhiteSpace([string]$binding.completeClosureReference)){$null}else{([string]$binding.completeClosureReference).Replace('stage2a-','stage2a-continuation-')};$mapped+=,[ordered]@{attemptId="stage2a-continuation-$($row[0].globalOrdinal)";originalAttemptId="stage2a-$($row[0].originalScheduleOrdinal)";phase=[string]$row[0].phase;ordinal=[int]$row[0].globalOrdinal;retentionClass=[string]$binding.retentionClass;recordPath="future/stage2/v2/$($row[0].proofRunId)/$($binding.recordPath)";recordSha256=[string]$binding.recordSha256;disposition=[string]$binding.disposition;completeClosureReference=$reference}}
 $attemptsPath=Join-Path $CellRoot 'continuation-attempt-bindings.json'
 Write-G05Stage2ASemanticJson $attemptsPath @($mapped)
 [pscustomobject]@{AttemptsPath=$attemptsPath;Summaries=@($summaries);RetentionPlan=$plan;AttemptBindings=@($mapped)}
}

$schedule=Read-G05Stage2AContinuationBootstrapSchedule $schedulePath
$contract=[ordered]@{schemaVersion=1;runnerId='Gate0.G05.Stage2A.ContinuationRunner.V1';status=if($ExecuteMedia){'live-execution-requested'}else{'contract-only'};noMediaExecuted=-not[bool]$ExecuteMedia;schedule=[ordered]@{path='eng/gate0/g0.5-stage2a-continuation-schedule.json';sha256=$schedule.Sha256;attemptCount=72;cellCount=12;evidenceGroupId='g05-stage2a-continuation-20260827'};execution=[ordered]@{concurrency=1;freshWarmupsAlwaysExecute=$true;measuredValuesPerCell=5;warmupExcludedFromStatistics=$true;deterministicRouteSuspension=$true;stopAfterStage2A=$true};oracle=[ordered]@{baseline='V3 only';typical='V3 plus exact V4 overlay only';stress='V3 plus exact V5 overlay only';v5FreezePath='eng/gate0/g0.5-lossy-audio-oracle-amendment-v5-freeze.json';reevaluationSummaryPath='eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json'};retention=[ordered]@{writer='Add-Gate0EvidenceV2Shard.ps1';oneShardPerCell=$true;localAndRemoteValidationAfterEachAppend=$true;incrementalCombinedV1V2Headroom=$true;approvedSourceRoot='StagingRoot'};limitations=@('Proof-only P2 runtime-route continuation. No Stage 2B, concurrency comparison, long-form, product, WPF, shipping-runtime, distribution, or legal claim.')}
if(-not$ExecuteMedia){[pscustomobject]$contract;return}
if([string]::IsNullOrWhiteSpace($RuntimeRoot)-or[string]::IsNullOrWhiteSpace($ArtifactRoot)-or[string]::IsNullOrWhiteSpace($StagingRoot)){throw 'Live continuation requires explicit RuntimeRoot, ArtifactRoot, and StagingRoot. No media was started.'}
$bootstrapAuthorization=Read-G05Stage2AContinuationBootstrapAuthorization $authorizationPath $schedule

# Only hash-bound repository modules may execute beyond this point.
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AContinuationHelpers.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2SmokeHelpers.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2ASemanticHelpers.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2ASemanticExecutor.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AV5AudioOracle.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05Stage2AV5FreezeValidation.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'G05MarkerSurvivabilityHelpers.psm1') -Force
$authorization=Read-G05Stage2AContinuationAuthorization $authorizationPath $repositoryRoot $schedulePath
if([string]$authorization.Sha256-ne[string]$bootstrapAuthorization.Sha256){throw 'Full continuation authorization bytes changed after bootstrap validation. No media was started.'}
$freeze=Get-Content -LiteralPath $v5FreezePath -Raw|ConvertFrom-Json -Depth 64
$v5Amendment=Get-Content -LiteralPath $v5AmendmentPath -Raw|ConvertFrom-Json -Depth 64
$reevaluation=Get-Content -LiteralPath $v5ResultPath -Raw|ConvertFrom-Json -Depth 64
$v5FreezeBinding=@($freeze.frozenInputs|Where-Object{[string]$_.path-eq'eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json'})
if($freeze.freezeId-ne'Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.Frozen.20260827'-or$reevaluation.status-ne'passed-no-media-continuation-prerequisite'-or$v5FreezeBinding.Count-ne1-or[string]$v5FreezeBinding[0].sha256-ne(Get-G05Stage2AContinuationSha256 $v5AmendmentPath)){throw 'V5 prerequisite is not effective. No media was started.'}
$audio=Get-Content -LiteralPath $audioContractPath -Raw|ConvertFrom-Json -Depth 100
$workload=Get-Content -LiteralPath $workloadPath -Raw|ConvertFrom-Json -Depth 100
$retention=Read-G05Stage2ARetentionContract $retentionContractPath
$initialPreflightOutput=Join-Path $StagingRoot ("g05-stage2a-continuation-initial-preflight-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))")
& $preflightPath -RuntimeRoot $RuntimeRoot -ArtifactRoot $ArtifactRoot -StagingRoot $StagingRoot -OutputDirectory $initialPreflightOutput -RequireRemoteVerification -AllowCompletedContinuationAudit:$AllowCompletedContinuationAudit|Out-Null
$initialPreflightEvidencePath=Join-Path $initialPreflightOutput 'g0.5-stage2a-continuation-preflight-evidence.json'
if(-not(Test-Path -LiteralPath $initialPreflightEvidencePath -PathType Leaf)){throw 'Initial continuation preflight did not retain its evidence JSON.'}
$initialPreflightEvidenceSha256=Get-G05Stage2AContinuationSha256 $initialPreflightEvidencePath
$v4Closure=Assert-G05Stage2AContinuationV4Closure $audioContractPath
$v5Closure=Assert-G05Stage2AContinuationV5Closure $ArtifactRoot $StagingRoot $reevaluation
$completed=Assert-G05Stage2AContinuationResumePrefix $schedule.Schedule $ArtifactRoot
$cells=@($schedule.Schedule.attempts|Select-Object -ExpandProperty cellId -Unique)
$results=@()
$suspendedRoutes=Restore-G05Stage2AContinuationSuspendedRoutes $schedule.Schedule $ArtifactRoot
 $pendingCells=@($cells|Select-Object -Skip $completed)
$firstPendingCell=$true
foreach($cellId in $pendingCells){
    if($firstPendingCell){$preflightOutput=$initialPreflightOutput;$preflightEvidencePath=$initialPreflightEvidencePath;$firstPendingCell=$false}else{$preflightOutput=Join-Path $StagingRoot ("g05-stage2a-continuation-preflight-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))-$cellId");& $preflightPath -RuntimeRoot $RuntimeRoot -ArtifactRoot $ArtifactRoot -StagingRoot $StagingRoot -OutputDirectory $preflightOutput -RequireRemoteVerification|Out-Null;$preflightEvidencePath=Join-Path $preflightOutput 'g0.5-stage2a-continuation-preflight-evidence.json'}
    $rows=@($schedule.Schedule.attempts|Where-Object cellId -eq $cellId|Sort-Object continuationOrdinal)
    $proof=[string]$rows[0].proofRunId;$cellRoot=Join-Path $StagingRoot $proof
    if(Test-Path $cellRoot){throw 'Continuation never reuses a partial cell root.'}
    [IO.Directory]::CreateDirectory($cellRoot)|Out-Null
    if(-not(Test-Path -LiteralPath $preflightEvidencePath -PathType Leaf)){throw 'Continuation preflight did not retain its evidence JSON.'}
    $retainedPreflightPath=Join-Path $cellRoot 'cell-preflight.json'
    if(Test-Path -LiteralPath $retainedPreflightPath){throw 'Continuation preflight evidence destination already exists.'}
    Copy-Item -LiteralPath $preflightEvidencePath -Destination $retainedPreflightPath
    $retainedPreflightSha256=Get-G05Stage2AContinuationSha256 $retainedPreflightPath
    $cell=Get-G05Stage2ACellRows $schedule.Schedule $workload $cellId
    $outcome=Test-G05Stage2AContinuationSemanticCell $cell $rows $cellRoot $RuntimeRoot $ArtifactRoot $workload $audio $v5Amendment $retention $suspendedRoutes
    $measured=@($outcome.Summaries|Where-Object{$_.phase-eq'measured'-and$_.disposition-eq'passed'})
    $statistics=if($measured.Count-eq5){[ordered]@{wallClockMilliseconds=(Get-G05Stage2AStatistics @($measured|ForEach-Object{[double]$_.measurement.summary.wallClockMilliseconds}));peakWorkingSetBytes=(Get-G05Stage2AStatistics @($measured|ForEach-Object{[double]$_.measurement.summary.peakWorkingSetBytes}));peakPrivateMemoryBytes=(Get-G05Stage2AStatistics @($measured|ForEach-Object{[double]$_.measurement.summary.peakPrivateMemoryBytes}));meanNormalizedCpuPercent=(Get-G05Stage2AStatistics @($measured|ForEach-Object{[double]$_.measurement.summary.meanNormalizedCpuPercent}));readTransferBytes=(Get-G05Stage2AStatistics @($measured|ForEach-Object{[double]$_.measurement.summary.readTransferBytes}));writeTransferBytes=(Get-G05Stage2AStatistics @($measured|ForEach-Object{[double]$_.measurement.summary.writeTransferBytes}));outputBytes=(Get-G05Stage2AStatistics @($measured|ForEach-Object{[double]$_.outputBytes}));maximumFrameMeanAbsoluteError=(Get-G05Stage2AStatistics @($measured|ForEach-Object{[double]$_.visual.maximumFrameMeanAbsoluteError}))}}else{$null}
    $cellSummary=[ordered]@{schemaVersion=1;proofRunId=$proof;cellId=$cellId;preflight=[ordered]@{path='cell-preflight.json';sha256=$retainedPreflightSha256;remoteVerificationRequired=$true};attempts=@($outcome.Summaries);attemptBindings='continuation-attempt-bindings.json';retention=[ordered]@{ordinaryCompleteClosureAttemptId=$outcome.RetentionPlan.ordinaryCompleteClosureAttemptId;hasOrdinaryMeasuredClosure=[bool]$outcome.RetentionPlan.hasOrdinaryMeasuredClosure};measuredPassedCount=$measured.Count;statistics=$statistics;receipt=[ordered]@{proofRunId=$proof;cellId=$cellId;destinationName="future/stage2/v2/$proof";shardPath="stage2/$proof.manifest.json"}}
    Write-G05Stage2ASemanticJson (Join-Path $cellRoot 'cell-summary.json') $cellSummary
    $writerReceipt=& $v2WriterPath -ArtifactRoot $ArtifactRoot -SourceRoot $cellRoot -ApprovedSourceRoot $StagingRoot -ProofRunId $proof -EvidenceGroupId 'g05-stage2a-continuation-20260827' -CellId $cellId -EvidenceBoundary p2-runtime-route -ContractIdentity @('repository:eng/gate0/g0.5-stage2a-continuation-authorization.json',"sha256:$($authorization.Sha256)") -Provenance 'Owner-authorized Gate 0 G0.5 Stage 2A continuation runtime-route cell evidence.' -ProducerRuntimeIdentity @('P2.BtbnLgplShared.WindowsX64.20260820') -LicenseRecords @('repository:eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json') -ContinuationAuthorizationPath $v2WriterAuthorizationPath -AttemptsPath $outcome.AttemptsPath
    $validationReceipt=Invoke-G05Stage2AContinuationPostAppendValidation $ArtifactRoot
    # The immutable writer returns no receipt object; retain the independently validated root/shard identity in the run result.
    $rootAfterAppend=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/v2/root-index.json') -Raw|ConvertFrom-Json -Depth 32
    $runAfterAppend=@($rootAfterAppend.runs|Where-Object { [string]$_.proofRunId -eq $proof })
    if($runAfterAppend.Count-ne1){throw 'The V2 append did not retain exactly one continuation root entry.'}
    $results+=,[ordered]@{proofRunId=$proof;cellId=$cellId;attemptCount=6;cellSummary='cell-summary.json';measuredPassedCount=$measured.Count;dispositions=@($outcome.Summaries|ForEach-Object{[string]$_.disposition});retentionReceipt=[ordered]@{writerReturnedNoReceipt=($null-eq$writerReceipt);localValidation=$validationReceipt.local;remoteValidation=$validationReceipt.remote;shardSha256=[string]$runAfterAppend[0].shardSha256;logicalArtifactBytes=[int64]$runAfterAppend[0].logicalArtifactBytes;rootLogicalArtifactBytes=[int64]$rootAfterAppend.totals.logicalArtifactBytes}}
}
$allAttemptSummaries=@(Get-G05Stage2AContinuationRetainedSummaries $ArtifactRoot)
$allExecuted=@($allAttemptSummaries|ForEach-Object{[string]$_.disposition})
$v2Root=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/v2/root-index.json') -Raw|ConvertFrom-Json -Depth 32
Assert-G05Stage2AContinuationNoActiveMedia 'final continuation accounting'
[pscustomobject]@{status=if(@($allExecuted|Where-Object{$_-notin @('passed','blocked')}).Count){'completed-with-failures'}elseif(@($allExecuted|Where-Object{$_-eq'blocked'}).Count){'completed-blocked'}else{'completed'};resumedCompletedCellCount=$completed;scheduledAttemptCount=72;resumedAttemptCount=$completed*6;newlyProcessedAttemptCount=$results.Count*6;totalRetainedAttemptCount=$allAttemptSummaries.Count;physicallyExecutedAttemptCount=@($allAttemptSummaries|Where-Object{[bool]$_.cleanup.processStarted}).Count;passedAttemptCount=@($allExecuted|Where-Object{$_-eq'passed'}).Count;failedAttemptCount=@($allExecuted|Where-Object{$_-eq'failed'}).Count;blockedAttemptCount=@($allExecuted|Where-Object{$_-eq'blocked'}).Count;divergentAttemptCount=@($allExecuted|Where-Object{$_-in @('structurally-divergent','semantically-divergent','byte-divergent')}).Count;initialPreflight=[ordered]@{path=[IO.Path]::GetFileName($initialPreflightOutput)+ '/g0.5-stage2a-continuation-preflight-evidence.json';sha256=$initialPreflightEvidenceSha256;consumedByFirstPendingCell=($results.Count-gt0);remoteVerificationRequired=$true};cells=@($results);suspendedRouteIds=@($suspendedRoutes);retention=[ordered]@{v2RootIndexSha256=(Get-G05Stage2AContinuationSha256 (Join-Path $PSScriptRoot 'evidence/v2/root-index.json'));v2LogicalArtifactBytes=[int64]$v2Root.totals.logicalArtifactBytes;globalCeilingBytes=805306368;remainingHeadroomBytes=805306368-78538843-[int64]$v2Root.totals.logicalArtifactBytes}}
