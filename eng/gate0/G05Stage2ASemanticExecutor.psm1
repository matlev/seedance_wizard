Set-StrictMode -Version Latest

# The executor is a proof-only seam. Its exact schedule/hash helpers and the
# shared frozen graph/audio/visual mechanisms are declared in the pending
# authorization and imported by the runner before this module is loaded.

function Get-G05Stage2ASemanticProperty([object] $Value, [string] $Name, $Default = $null) {
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    $property.Value
}

function Write-G05Stage2ASemanticJson([string] $Path, [object] $Value) {
    $partial = "$Path.partial"
    if (Test-Path -LiteralPath $Path -PathType Leaf) { throw 'Stage 2A semantic evidence is immutable and already exists.' }
    if (Test-Path -LiteralPath $partial) { throw 'A prior partial Stage 2A semantic evidence write must be dispositioned before retry.' }
    $parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Stage 2A semantic evidence parent is absent.' }
    try {
        [IO.File]::WriteAllText($partial, (($Value | ConvertTo-Json -Depth 100) + "`n"), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $partial -Destination $Path
    }
    finally { if (Test-Path -LiteralPath $partial -PathType Leaf) { Remove-Item -LiteralPath $partial -Force } }
}

function Read-G05Stage2ARetentionContract([string] $Path) {
    $value = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 16
    $expected = @('schemaVersion','contractId','status','evidenceBoundary','cellCount','attemptsPerCell','measuredAttemptsPerCell','ordinaryClosureBytes','compactPassingRepeatMaximumBytes','exceptionalClosureBytes','compactPassingRepeatsPerCell','requiredReservationPerCellBytes','stage2ARetentionCeilingBytes','reservationRule','ordinaryRule','compactRule','exceptionalRule','limitations')
    Assert-G05Stage2AExactProperties $value $expected 'Stage 2A retention contract'
    if ($value.schemaVersion -ne 1 -or $value.contractId -ne 'Gate0.G05.Stage2A.Retention.V1' -or $value.status -ne 'owner-approved-projection-frozen-for-executor-review' -or
        $value.evidenceBoundary -ne 'p2-runtime-route' -or [int]$value.cellCount -ne 18 -or [int]$value.attemptsPerCell -ne 6 -or [int]$value.measuredAttemptsPerCell -ne 5 -or
        [int64]$value.ordinaryClosureBytes -ne 18784084 -or [int64]$value.compactPassingRepeatMaximumBytes -ne 262144 -or [int64]$value.exceptionalClosureBytes -ne 18784084 -or
        [int]$value.compactPassingRepeatsPerCell -ne 5 -or [int64]$value.requiredReservationPerCellBytes -ne 38878888 -or [int64]$value.stage2ARetentionCeilingBytes -ne 805306368) {
        throw 'Stage 2A retention contract differs from the approved fixed projection.'
    }
    [pscustomobject]@{ Contract=$value; Sha256=(Get-G05Stage2ASha256 $Path) }
}

function Get-G05Stage2ACellRows([object] $Schedule, [object] $WorkloadContract, [string] $CellId) {
    $rows = @($Schedule.attempts | Where-Object cellId -eq $CellId | Sort-Object globalOrdinal)
    if ($rows.Count -ne 6 -or $rows[0].phase -ne 'warmup' -or @($rows | Where-Object phase -eq 'measured').Count -ne 5) { throw "Cell $CellId does not contain the exact warmup plus five measured sequence." }
    $first = $rows[0]
    for ($index = 0; $index -lt $rows.Count; $index++) {
        $row = $rows[$index]
        $expectedPhase = if ($index -eq 0) { 'warmup' } else { 'measured' }
        $expectedPhaseOrdinal = if ($index -eq 0) { 1 } else { $index }
        if ([string]$row.cellId -ne $CellId -or [string]$row.groupId -ne [string]$first.groupId -or [int]$row.groupOrdinal -ne [int]$first.groupOrdinal -or
            [string]$row.workloadId -ne [string]$first.workloadId -or [string]$row.resolutionId -ne [string]$first.resolutionId -or
            [string]$row.candidateId -ne [string]$first.candidateId -or [string]$row.routeId -ne [string]$first.routeId -or
            [string]$row.threadPolicyId -ne [string]$first.threadPolicyId -or [int]$row.globalOrdinal -ne ([int]$first.globalOrdinal + $index) -or
            [int]$row.cellAttemptOrdinal -ne ($index + 1) -or [string]$row.phase -ne $expectedPhase -or [int]$row.phaseOrdinal -ne $expectedPhaseOrdinal) {
            throw "Cell $CellId contains mixed or noncontiguous frozen schedule rows."
        }
    }
    $workload = @($WorkloadContract.workloads | Where-Object id -eq $rows[0].workloadId)
    if ($workload.Count -ne 1) { throw "Cell $CellId cannot resolve its frozen workload." }
    $variant = @($workload[0].resolutionVariants | Where-Object id -eq $rows[0].resolutionId)
    $route = @($WorkloadContract.routes | Where-Object id -eq $rows[0].routeId)
    $policy = @($WorkloadContract.threadPolicies | Where-Object id -eq $rows[0].threadPolicyId)
    if ($variant.Count -ne 1 -or $route.Count -ne 1 -or $policy.Count -ne 1 -or $workload[0].evidenceBoundary -ne 'runtime-route' -or $rows[0].threadPolicyId -notin @($route[0].threadPolicies)) { throw "Cell $CellId cannot resolve its frozen resolution, route, or thread policy." }
    $threads = if ($null -ne $policy[0].PSObject.Properties['resolvedValue']) { [int]$policy[0].resolvedValue } elseif ($policy[0].resolvedValueExpression -eq 'ceil(observedLogicalProcessors/2)') { [int][Math]::Ceiling([Environment]::ProcessorCount / 2) } else { throw "Cell $CellId uses an unknown thread policy." }
    [pscustomobject]@{ CellId=$CellId; Attempts=$rows; Workload=$workload[0]; Variant=$variant[0]; Route=$route[0]; Policy=$policy[0]; Threads=$threads }
}

function New-G05Stage2AEncodeTokens([object] $Cell, [object] $Contract, [string] $ArtifactRoot, [string] $Output) {
    $tokens = [Collections.Generic.List[string]]::new()
    foreach ($token in @('-hide_banner','-nostdin','-progress','pipe:1','-stats_period','0.5')) { $tokens.Add($token) }
    foreach ($input in @($Cell.Workload.inputs | Sort-Object inputIndex)) {
        $profile = @($Contract.inputProfiles | Where-Object id -eq $input.profile)
        if ($profile.Count -ne 1) { throw "Frozen input profile is absent: $($input.profile)" }
        $audio = ([string]$profile[0].stream) -match ':a:'
        foreach ($raw in @($profile[0].tokens)) {
            $value = ([string]$raw).Replace('{artifactRoot}', $ArtifactRoot)
            if ($value -eq '-i') { $tokens.Add($(if ($audio) { '-threads:a' } else { '-threads:v' })); $tokens.Add([string]$Cell.Threads) }
            $tokens.Add($value)
        }
    }
    $tokens.Add('-filter_threads'); $tokens.Add([string]$Cell.Threads)
    $tokens.Add('-filter_complex_threads'); $tokens.Add([string]$Cell.Threads)
    $tokens.Add('-filter_complex'); $tokens.Add((Get-G05Stage2ACombinedGraph $Cell.Workload $Cell.Variant))
    foreach ($map in @($Cell.Route.maps)) { $tokens.Add('-map'); $tokens.Add([string]$map) }
    foreach ($token in @('-c:v',[string]$Cell.Route.videoEncoder,'-threads:v:0',[string]$Cell.Threads) + @($Cell.Route.videoOptions) + @('-c:a',[string]$Cell.Route.audioEncoder,'-threads:a:0',[string]$Cell.Threads) + @($Cell.Route.audioOptions) + @($Cell.Route.muxerOptions) + @($Cell.Route.outputDurationTokens | ForEach-Object { ([string]$_).Replace('{durationSeconds}', [string]$Cell.Workload.durationSeconds) }) + @('-f',[string]$Cell.Route.muxer,'-y',$Output)) { $tokens.Add([string]$token) }
    $tokens.ToArray()
}

function Get-G05Stage2ASemanticFile([string] $Path, [string] $Root) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $pathFull = Assert-G05SmokePath $rootFull $Path 'Stage 2A semantic artifact'
    $relative = [IO.Path]::GetRelativePath($rootFull, $pathFull).Replace('\','/')
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains('\') -or $relative -match '(^|/)\.\.(/|$)') { throw 'Stage 2A semantic artifact path is not portable and contained.' }
    $item = Get-Item -LiteralPath $pathFull
    [ordered]@{ path=$relative; byteSize=[int64]$item.Length; sha256=(Get-G05SmokeHash $pathFull) }
}

function Get-G05Stage2ASemanticSummaryHash([object] $Value, [string] $Path) {
    Write-G05Stage2ASemanticJson $Path $Value
    Get-G05SmokeHash $Path
}

function New-G05Stage2AAttemptBinding([object] $Attempt, [object] $SemanticSummary, [string] $SummaryPath, [string] $SourceRoot, [string] $RetentionClass, [string] $CompleteClosureReference = $null) {
    $rootFull = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $summaryFull = [IO.Path]::GetFullPath($SummaryPath)
    if (-not $summaryFull.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Stage 2A attempt summary escaped its cell source root.' }
    $summarySha = Get-G05Stage2ASemanticSummaryHash $SemanticSummary $SummaryPath
    $relative = [IO.Path]::GetRelativePath($rootFull, $summaryFull).Replace('\','/')
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|/)\.\.(/|$)' -or $relative.Contains('\')) { throw 'Stage 2A attempt summary path is not a portable contained relative path.' }
    [pscustomobject][ordered]@{
        attemptId = "stage2a-$($Attempt.globalOrdinal)"
        phase = [string]$Attempt.phase
        ordinal = [int]$Attempt.globalOrdinal
        retentionClass = $RetentionClass
        recordPath = $relative
        recordSha256 = $summarySha
        disposition = [string]$SemanticSummary.disposition
        completeClosureReference = $CompleteClosureReference
    }
}

function Assert-G05Stage2ACompactBinding([object] $Binding, [string] $SourceRoot, [int64] $MaximumBytes) {
    $required = @('attemptId','phase','ordinal','retentionClass','recordPath','recordSha256','disposition','completeClosureReference')
    Assert-G05Stage2AExactProperties $Binding $required 'Stage 2A compact attempt binding'
    if ($Binding.retentionClass -ne 'compact' -or $Binding.disposition -ne 'passed' -or [string]::IsNullOrWhiteSpace($Binding.completeClosureReference)) { throw 'A compact Stage 2A record lacks its passed disposition or complete closure reference.' }
    $relative = [string]$Binding.recordPath
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains('\') -or $relative -match '(^|/)\.\.(/|$)') { throw 'A compact Stage 2A record path is not portable and contained.' }
    $rootFull = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $path = [IO.Path]::GetFullPath((Join-Path $rootFull $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
    if (-not $path.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'A compact Stage 2A record escaped its source root.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -gt $MaximumBytes) { throw 'A compact Stage 2A semantic summary exceeds its approved cap or is absent.' }
    if ((Get-G05SmokeHash $path) -ne [string]$Binding.recordSha256) { throw 'A compact Stage 2A semantic summary hash differs from its attempt binding.' }
    $summary = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 32
    $validationNames = @('encode','probe','timing','visual','audio','cleanup')
    if ($summary.encodedByteEqualityClaim -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.outputSha256) -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.frameProbeSha256) -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.packetProbeSha256) -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.decodedVideoIdentitySha256) -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.decodedAudioRawSha256) -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.decodedAudioContentNormalizedSha256) -or
        $null -eq $summary.PSObject.Properties['validations'] -or @($summary.validations.PSObject.Properties).Count -ne $validationNames.Count -or
        @($validationNames | Where-Object { $null -eq $summary.validations.PSObject.Properties[$_] -or -not [bool]$summary.validations.$_ }).Count -ne 0) {
        throw 'A compact Stage 2A record lacks required passing validations or output, probe, and decoded semantic identity hashes.'
    }
}

function New-G05Stage2ABlockedAttempt([object] $Attempt, [string] $Reason) {
    [ordered]@{ globalOrdinal=[int]$Attempt.globalOrdinal; cellAttemptOrdinal=[int]$Attempt.cellAttemptOrdinal; phase=[string]$Attempt.phase; disposition='blocked'; retentionKind='exceptional-full-closure'; reason=$Reason; command=$null; hashes=[ordered]@{outputSha256=$null;frameProbeSha256=$null;packetProbeSha256=$null;decodedVideoIdentitySha256=$null;decodedAudioRawSha256=$null;decodedAudioContentNormalizedSha256=$null}; encodedByteEqualityClaim=$false; completedUtc=[DateTimeOffset]::UtcNow.ToString('O') }
}

function Test-G05Stage2ADeterministicIntegrityFailure([object] $SemanticSummary) {
    # The taxonomy deliberately distinguishes deterministic route defects from
    # environmental/retention/cleanup outcomes. Only these three labels suspend
    # later rows for the affected route.
    [string]$SemanticSummary.disposition -in @('structurally-divergent','semantically-divergent','byte-divergent')
}

function Resolve-G05Stage2ACellRetentionPlan([object[]] $AttemptSummaries) {
    if ($AttemptSummaries.Count -ne 6) { throw 'A Stage 2A cell retention plan requires exactly six attempts.' }
    $ordered = @($AttemptSummaries | Sort-Object globalOrdinal)
    $ordinary = @($ordered | Where-Object { $_.phase -eq 'measured' -and $_.disposition -eq 'passed' } | Select-Object -First 1)
    $ordinaryId = if ($ordinary.Count -eq 1) { [string]$ordinary[0].attemptId } else { $null }
    foreach ($summary in $ordered) {
        if ($null -eq $summary.PSObject.Properties['retentionClass']) { $summary | Add-Member -NotePropertyName retentionClass -NotePropertyValue $null }
        if ($null -eq $summary.PSObject.Properties['completeClosureReference']) { $summary | Add-Member -NotePropertyName completeClosureReference -NotePropertyValue $null }
        if ([string]$summary.disposition -eq 'passed' -and $null -ne $ordinaryId -and [string]$summary.attemptId -ne $ordinaryId) {
            $null = ($summary.retentionClass = 'compact'); $null = ($summary.completeClosureReference = $ordinaryId)
        } else {
            $null = ($summary.retentionClass = 'complete'); $null = ($summary.completeClosureReference = $null)
        }
    }
    [pscustomobject]@{ordinaryCompleteClosureAttemptId=$ordinaryId;hasOrdinaryMeasuredClosure=($null -ne $ordinaryId);attempts=$ordered}
}

function Get-G05Stage2AAttemptRetentionClass([object[]] $PriorBindings, [object] $AttemptSummary) {
    # Compatibility seam for contract-only callers. Live retention is resolved
    # only after the complete six-attempt cell is independently validated.
    if ([string]$AttemptSummary.disposition -ne 'passed') { return 'complete' }
    if ([string]$AttemptSummary.phase -eq 'measured' -and @($PriorBindings | Where-Object { $_.phase -eq 'measured' -and $_.retentionClass -eq 'complete' -and $_.disposition -eq 'passed' }).Count -eq 0) { return 'complete' }
    if (@($PriorBindings | Where-Object { $_.phase -eq 'measured' -and $_.retentionClass -eq 'complete' -and $_.disposition -eq 'passed' }).Count -eq 0) { return 'complete' }
    'compact'
}

function Get-G05Stage2ACompleteClosureReference([object[]] $Bindings) {
    $ordinary = @($Bindings | Where-Object { $_.phase -eq 'measured' -and $_.retentionClass -eq 'complete' -and $_.disposition -eq 'passed' })
    if ($ordinary.Count -gt 1) { throw 'A Stage 2A cell has more than one ordinary measured complete closure.' }
    if ($ordinary.Count -eq 1) { return [string]$ordinary[0].attemptId }
    $null
}

function Assert-G05Stage2AAttemptSummary([object] $Summary) {
    $required = @('attemptId','globalOrdinal','phase','disposition','selectedComponents','commands','validations','hashes','encodedByteEqualityClaim','cleanup')
    foreach ($name in $required) { if ($null -eq $Summary.PSObject.Properties[$name]) { throw "Stage 2A attempt summary lacks $name." } }
    $approvedDispositions = @('passed','failed','blocked','cleanup-failed','orphan-producing','byte-divergent','semantically-divergent','structurally-divergent')
    if ([string]$Summary.disposition -notin $approvedDispositions) { throw 'Stage 2A attempt summary has an unapproved disposition.' }
    if ([bool]$Summary.encodedByteEqualityClaim) { throw 'Stage 2A evidence may not claim encoded-byte equality.' }
    if ([string]$Summary.disposition -eq 'passed') {
        $names = @('encode','probe','timing','visual','audio','cleanup')
        foreach ($name in $names) { if ($null -eq $Summary.validations.PSObject.Properties[$name] -or -not [bool]$Summary.validations.$name) { throw "A passed Stage 2A summary lacks $name validation." } }
        foreach ($name in @('outputSha256','frameProbeSha256','packetProbeSha256','decodedVideoIdentitySha256','decodedAudioRawSha256','decodedAudioContentNormalizedSha256')) { if ([string]::IsNullOrWhiteSpace([string]$Summary.hashes.$name)) { throw "A passed Stage 2A summary lacks $name." } }
        if (-not [bool]$Summary.cleanup.processTreeRootExited -or -not [bool]$Summary.cleanup.processTreeOrphanFree -or -not [bool]$Summary.cleanup.noUnvalidatedPartialOutput) { throw 'A passed Stage 2A summary lacks process-tree or partial-output cleanup evidence.' }
    }
}

function Get-G05Stage2AContentNormalizedAudioHash([string] $Path, [int64] $ExpectedContentBytes = 5760000, [int] $MaximumRawTailSamples = 1024) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Decoded audio is absent.' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($ExpectedContentBytes -le 0 -or $ExpectedContentBytes % 4 -ne 0 -or $bytes.Length -lt $ExpectedContentBytes -or
        $bytes.Length -gt $ExpectedContentBytes + ([int64]$MaximumRawTailSamples * 4) -or $bytes.Length % 4 -ne 0) {
        throw 'Decoded audio is outside the frozen stereo s16le content/tail envelope.'
    }
    $content = [byte[]]::new([int]$ExpectedContentBytes)
    [Array]::Copy($bytes, $content, [int]$ExpectedContentBytes)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($content))
}

function Get-G05Stage2AExactVideoTiming([object] $VideoStream, [object[]] $Frames) {
    if ($Frames.Count -ne 750) { throw "Expected 750 video frames, observed $($Frames.Count)." }
    $timeBase = [string]$VideoStream.time_base
    $ticks = [Collections.Generic.List[int64]]::new()
    for ($index = 0; $index -lt 750; $index++) {
        $raw = Get-G05Stage2ASemanticProperty $Frames[$index] 'best_effort_timestamp' (Get-G05Stage2ASemanticProperty $Frames[$index] 'pts')
        if ($null -eq $raw) { throw "Video frame $index lacks a presentation timestamp." }
        $tick = Convert-G05SmokeTicks ([int64]$raw) $timeBase
        if ($tick -ne [int64]($index * 40)) { throw "Video frame $index normalized to $tick instead of $($index * 40)." }
        $ticks.Add($tick)
    }
    $last = $Frames[-1]
    $durationRaw = Get-G05Stage2ASemanticProperty $last 'pkt_duration' (Get-G05Stage2ASemanticProperty $last 'duration')
    if ($null -ne $durationRaw) { $end = $ticks[-1] + (Convert-G05SmokeTicks ([int64]$durationRaw) $timeBase); $source = 'final-frame-duration' }
    elseif ($null -ne $VideoStream.PSObject.Properties['duration_ts']) { $end = Convert-G05SmokeTicks ([int64]$VideoStream.duration_ts) $timeBase; $source = 'stream-duration-ts' }
    else { throw 'Video presentation-end evidence is unavailable.' }
    if ($end -ne 30000) { throw "Video presentation end normalized to $end instead of 30000." }
    [ordered]@{passed=$true;frameCount=750;comparisonTimeBase='1/1000';firstTick=$ticks[0];finalTick=$ticks[-1];presentationEndTick=$end;presentationEndSource=$source;allFrameTicksExact=$true}
}

Export-ModuleMember -Function Read-G05Stage2ARetentionContract,Get-G05Stage2ACellRows,New-G05Stage2AEncodeTokens,Get-G05Stage2ASemanticFile,Get-G05Stage2ASemanticSummaryHash,New-G05Stage2AAttemptBinding,Assert-G05Stage2ACompactBinding,New-G05Stage2ABlockedAttempt,Test-G05Stage2ADeterministicIntegrityFailure,Get-G05Stage2AAttemptRetentionClass,Resolve-G05Stage2ACellRetentionPlan,Get-G05Stage2ACompleteClosureReference,Assert-G05Stage2AAttemptSummary,Get-G05Stage2AExactVideoTiming,Get-G05Stage2AContentNormalizedAudioHash,Write-G05Stage2ASemanticJson
