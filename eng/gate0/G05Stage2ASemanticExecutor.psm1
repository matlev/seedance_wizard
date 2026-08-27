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
    if ($summary.encodedByteEqualityClaim -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.outputSha256) -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.probeSha256) -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.decodedVideoIdentitySha256) -or [string]::IsNullOrWhiteSpace([string]$summary.hashes.decodedAudioIdentitySha256) -or
        $null -eq $summary.PSObject.Properties['validations'] -or @($summary.validations.PSObject.Properties).Count -ne $validationNames.Count -or
        @($validationNames | Where-Object { $null -eq $summary.validations.PSObject.Properties[$_] -or -not [bool]$summary.validations.$_ }).Count -ne 0) {
        throw 'A compact Stage 2A record lacks required passing validations or output, probe, and decoded semantic identity hashes.'
    }
}

function New-G05Stage2ABlockedAttempt([object] $Attempt, [string] $Reason) {
    [ordered]@{ globalOrdinal=[int]$Attempt.globalOrdinal; cellAttemptOrdinal=[int]$Attempt.cellAttemptOrdinal; phase=[string]$Attempt.phase; disposition='blocked'; retentionKind='exceptional-full-closure'; reason=$Reason; command=$null; hashes=[ordered]@{outputSha256=$null;probeSha256=$null;decodedVideoIdentitySha256=$null;decodedAudioIdentitySha256=$null}; encodedByteEqualityClaim=$false; completedUtc=[DateTimeOffset]::UtcNow.ToString('O') }
}

function Test-G05Stage2ADeterministicIntegrityFailure([object] $SemanticSummary) {
    # Strict encode/decode/probe/oracle identity or cleanup failures suspend a route. Timing alone is not a performance failure.
    [string]$SemanticSummary.disposition -in @('failed-integrity','failed-oracle','failed-cleanup','failed-command')
}

Export-ModuleMember -Function Read-G05Stage2ARetentionContract,Get-G05Stage2ACellRows,New-G05Stage2AEncodeTokens,Get-G05Stage2ASemanticFile,Get-G05Stage2ASemanticSummaryHash,New-G05Stage2AAttemptBinding,Assert-G05Stage2ACompactBinding,New-G05Stage2ABlockedAttempt,Test-G05Stage2ADeterministicIntegrityFailure,Write-G05Stage2ASemanticJson
