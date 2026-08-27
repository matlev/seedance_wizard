Set-StrictMode -Version Latest

# Proof-only, no-media diagnostic for the two retained Stage 2A stress warm-ups.
# This module deliberately consumes already-decoded PCM only.  It does not start
# a process, inspect a container, or modify Stage 2A evidence.

function Get-G05Stage2AAudioDiagnosticHash([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Read-G05Stage2AAudioDiagnosticJson([string] $Path, [string] $Label, [string] $ExpectedSha256) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label must be an existing absolute file." }
    $actual = Get-G05Stage2AAudioDiagnosticHash $Path
    if ($actual -ne $ExpectedSha256.ToUpperInvariant()) { throw "$Label SHA-256 mismatch." }
    try { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100 } catch { throw "$Label is not valid JSON." }
}

function Get-G05Stage2AAudioDiagnosticExpectedPcmHash([object] $Summary) {
    foreach ($candidate in @(
        $Summary.hashes.decodedAudioRawSha256,
        $Summary.audio.quality.rawPcm.sha256,
        $Summary.audio.quality.contentNormalized.sha256
    )) {
        if (-not [string]::IsNullOrWhiteSpace([string] $candidate)) { return [string] $candidate }
    }
    throw 'Retained attempt summary does not bind a decoded PCM SHA-256.'
}

function Get-G05Stage2AAudioDiagnosticFailureLabels([object] $Summary, [string] $Label) {
    $labels = @($Summary.audio.quality.failures | ForEach-Object { [string] $_ })
    if ($labels.Count -eq 0) { throw "$Label does not contain retained frozen audio finding labels." }
    $labels
}

function Assert-G05Stage2AAudioDiagnosticSuccessfulObservations([object] $Summary, [string] $Label) {
    if ([string]$Summary.disposition -ne 'semantically-divergent' -or -not [bool]$Summary.validations.encode -or -not [bool]$Summary.validations.probe -or -not [bool]$Summary.validations.timing -or -not [bool]$Summary.validations.visual -or -not [bool]$Summary.validations.cleanup -or -not [bool]$Summary.audio.timing.passed -or -not [bool]$Summary.audio.quality.onsetTimingPassed -or [bool]$Summary.audio.quality.qualityPassed) { throw "$Label does not preserve the retained semantically-divergent successful-observation state." }
    [ordered]@{
        disposition = [string]$Summary.disposition
        encode = [bool]$Summary.validations.encode; probe = [bool]$Summary.validations.probe; exactVideoTiming = [bool]$Summary.validations.timing; visualIdentity = [bool]$Summary.validations.visual; cleanup = [bool]$Summary.validations.cleanup
        audioTiming = [bool]$Summary.audio.timing.passed; onsetTiming = [bool]$Summary.audio.quality.onsetTimingPassed; audioQuality = [bool]$Summary.audio.quality.qualityPassed
        selectedComponents = $Summary.selectedComponents
    }
}

function Assert-G05Stage2AAudioDiagnosticExactLabels([string[]] $ReferenceLabels, [string[]] $RetainedLabels, [string] $Label) {
    if ($ReferenceLabels.Count -ne $RetainedLabels.Count) { throw "$Label retained finding-label count differs from the reference self-check." }
    for ($index = 0; $index -lt $ReferenceLabels.Count; $index++) { if ($ReferenceLabels[$index] -ne $RetainedLabels[$index]) { throw "$Label retained finding label differs at index $index." } }
}

function Get-G05Stage2AAudioDiagnosticRms([int16[]] $Samples, [int] $Channel, [int] $Start, [int] $End) {
    if ($Channel -notin 0,1 -or $Start -lt 0 -or $End -le $Start -or $End -gt ($Samples.Length / 2)) { throw 'Invalid diagnostic PCM window.' }
    [double] $sum = 0
    for ($sample = $Start; $sample -lt $End; $sample++) { $value = [double] $Samples[$sample * 2 + $Channel]; $sum += $value * $value }
    [Math]::Sqrt($sum / ($End - $Start)) / 32768.0
}

function Get-G05Stage2AAudioDiagnosticMinimumWindow([int16[]] $Samples, [int] $Channel, [int] $Start, [int] $End, [int] $WindowSamples) {
    if ($WindowSamples -le 0 -or $Start -lt 0 -or $End - $Start -lt $WindowSamples) { throw 'Invalid diagnostic rolling window.' }
    [double] $sum = 0
    for ($sample = $Start; $sample -lt $Start + $WindowSamples; $sample++) { $value = [double]$Samples[$sample * 2 + $Channel]; $sum += $value * $value }
    $bestStart = $Start; $bestSum = $sum
    for ($offset = $Start + 1; $offset + $WindowSamples -le $End; $offset++) {
        $removed = [double]$Samples[($offset - 1) * 2 + $Channel]; $added = [double]$Samples[($offset + $WindowSamples - 1) * 2 + $Channel]
        $sum += $added * $added - $removed * $removed
        # Strictly less preserves the exact oracle's deterministic earliest minimum.
        if ($sum -lt $bestSum) { $bestSum = $sum; $bestStart = $offset }
    }
    [ordered] @{ startSample = $bestStart; endSampleExclusive = $bestStart + $WindowSamples; rmsFullScale = [Math]::Sqrt([Math]::Max([double]0, $bestSum) / $WindowSamples) / 32768.0 }
}

function Get-G05Stage2AAudioDiagnosticWindowMetrics([int16[]] $Reference, [int16[]] $Output, [int] $Channel, [int] $Start, [int] $End) {
    [double] $sumReference = 0; [double] $sumOutput = 0; $count = $End - $Start
    for ($sample = $Start; $sample -lt $End; $sample++) { $sumReference += $Reference[$sample * 2 + $Channel]; $sumOutput += $Output[$sample * 2 + $Channel] }
    $meanReference = $sumReference / $count; $meanOutput = $sumOutput / $count
    [double] $covariance = 0; [double] $referenceEnergy = 0; [double] $outputEnergy = 0; [double] $errorEnergy = 0
    for ($sample = $Start; $sample -lt $End; $sample++) {
        $rawReference = [double]$Reference[$sample * 2 + $Channel]; $rawOutput = [double]$Output[$sample * 2 + $Channel]
        $x = $rawReference - $meanReference; $y = $rawOutput - $meanOutput; $error = $rawOutput - $rawReference
        $covariance += $x * $y; $referenceEnergy += $x * $x; $outputEnergy += $y * $y; $errorEnergy += $error * $error
    }
    [ordered]@{
        signedCorrelation = if ($referenceEnergy -eq 0 -or $outputEnergy -eq 0) { $null } else { $covariance / [Math]::Sqrt($referenceEnergy * $outputEnergy) }
        normalizedRmsError = if ($referenceEnergy -eq 0) { $null } else { [Math]::Sqrt($errorEnergy / $count) / [Math]::Sqrt($referenceEnergy / $count) }
        snrDb = if ($errorEnergy -eq 0) { 'Infinity' } elseif ($referenceEnergy -eq 0) { $null } else { 10 * [Math]::Log10($referenceEnergy / $errorEnergy) }
    }
}

function ConvertTo-G05Stage2AAudioDiagnosticSamples([string] $Path, [int] $ExpectedBytes, [int] $MaximumRawTailSamples = 0) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt $ExpectedBytes -or $bytes.Length -gt $ExpectedBytes + (4 * $MaximumRawTailSamples) -or $bytes.Length % 4 -ne 0) { throw 'Decoded PCM does not have the approved frozen stereo s16le length or tail.' }
    $content = [byte[]]::new($ExpectedBytes); [Array]::Copy($bytes, $content, $ExpectedBytes)
    $samples = [int16[]]::new($content.Length / 2); [Buffer]::BlockCopy($content, 0, $samples, 0, $content.Length); $samples
}

function Get-G05Stage2AAudioDiagnosticTracks([object] $Workload, [int] $StartSample, [int] $EndSampleExclusive) {
    @($Workload.audioTracks | Where-Object { (48 * [int] $_.startMs) -lt $EndSampleExclusive } | ForEach-Object {
        $trackStart = 48 * [int] $_.startMs
        [ordered]@{
            id = [string] $_.id; source = [string] $_.source; gain = [double] $_.gain; pan = [string] $_.pan
            startMs = [int] $_.startMs; startSample = $trackStart; loop = [bool] $_.loop
        }
    })
}

function Get-G05Stage2AAudioDiagnosticFindingDetails([object] $Quality, [int16[]] $Reference, [object] $Workload, [object] $Thresholds, [object] $Descriptor) {
    $details = [Collections.Generic.List[object]]::new()
    foreach ($region in @($Quality.regions)) {
        foreach ($failure in @($region.failures)) {
            if ($failure -notmatch '^(?<region>[^:]+):channel-(?<channel>[01]):(?<kind>active-rms|active-window-silence)$') { continue }
            $channel = [int] $Matches.channel; $kind = [string] $Matches.kind
            $window = if ($kind -eq 'active-window-silence') {
                Get-G05Stage2AAudioDiagnosticMinimumWindow $Reference $channel ([int] $region.startSample) ([int] $region.endSampleExclusive) ([int] $Thresholds.silenceWindowSamples)
            } else {
                [ordered] @{ startSample = [int] $region.startSample; endSampleExclusive = [int] $region.endSampleExclusive; rmsFullScale = (Get-G05Stage2AAudioDiagnosticRms $Reference $channel ([int] $region.startSample) ([int] $region.endSampleExclusive)) }
            }
            $expected = if ($kind -eq 'active-rms') { 'active' } else { 'active' }
            $threshold = if ($kind -eq 'active-rms') { [double] $Thresholds.minimumActiveChannelRmsFullScale } else { [double] $Thresholds.minimumActiveReferenceWindowOutputRmsFullScale }
            if ([string]$region.id -eq 'full-quality-region') {
                $declared = @($Descriptor.activeWindows | Where-Object { [int]$_.startSample -le [int]$region.startSample -and [int]$_.endSampleExclusive -ge [int]$region.endSampleExclusive })
                $classificationSource = [ordered]@{ kind='activeWindows'; declaredWindows=@($declared) }
            } else {
                $declared = @($Descriptor.trackOnsetWindows | Where-Object { [string]$_.id -eq [string]$region.id -and ([int]$_.startSample + 512) -eq [int]$region.startSample -and ([int]$_.endSampleExclusive - 512) -eq [int]$region.endSampleExclusive })
                $classificationSource = [ordered]@{ kind='trackOnsetWindows'; declaredWindows=@($declared) }
            }
            if ($declared.Count -ne 1) { throw "Finding region $($region.id) is not an exact declared active region." }
            $details.Add([ordered]@{
                region = [string] $region.id; regionStartSample = [int] $region.startSample; regionEndSampleExclusive = [int] $region.endSampleExclusive; channel = $channel; startSample = [int] $window.startSample; endSampleExclusive = [int] $window.endSampleExclusive
                startSeconds = [double] $window.startSample / [int] $Descriptor.sampleRate; endSeconds = [double] $window.endSampleExclusive / [int] $Descriptor.sampleRate
                findingType = $kind; expectedClassification = $expected; referenceRmsFullScale = [double] $window.rmsFullScale
                threshold = $threshold
                thresholdOrRule = if ($kind -eq 'active-rms') { "reference-independent active-channel RMS must be >= $threshold" } else { "reference-independent active 960-sample RMS must be >= $threshold" }
                classificationSource = $classificationSource
                contributingTracks = @(Get-G05Stage2AAudioDiagnosticTracks $Workload ([int] $window.startSample) ([int] $window.endSampleExclusive))
            })
        }
    }
    @($details)
}

function ConvertTo-G05Stage2AAudioDiagnosticOutputComparison([string] $RouteId, [int16[]] $Reference, [int16[]] $Output, [object[]] $Findings, [int] $SilenceWindowSamples) {
    @($Findings | ForEach-Object {
        $referenceRms = [double] $_.referenceRmsFullScale
        $outputRms = Get-G05Stage2AAudioDiagnosticRms $Output ([int] $_.channel) ([int] $_.startSample) ([int] $_.endSampleExclusive)
        $metrics = Get-G05Stage2AAudioDiagnosticWindowMetrics $Reference $Output ([int] $_.channel) ([int] $_.startSample) ([int] $_.endSampleExclusive)
        $routeMinimum = if ($_.findingType -eq 'active-window-silence') { Get-G05Stage2AAudioDiagnosticMinimumWindow $Output ([int]$_.channel) ([int]$_.regionStartSample) ([int]$_.regionEndSampleExclusive) $SilenceWindowSamples } else { $null }
        $referenceAtRouteMinimum = if ($null -ne $routeMinimum) { Get-G05Stage2AAudioDiagnosticRms $Reference ([int]$_.channel) ([int]$routeMinimum.startSample) ([int]$routeMinimum.endSampleExclusive) } else { $null }
        $classificationRms = if ($null -ne $routeMinimum) { [double]$routeMinimum.rmsFullScale } else { $outputRms }
        [ordered]@{
            routeId = $RouteId; region = $_.region; channel = $_.channel; startSample = $_.startSample; endSampleExclusive = $_.endSampleExclusive
            findingType = $_.findingType; expectedClassification = $_.expectedClassification; observedClassification = if ($classificationRms -ge [double]$_.threshold) { 'active' } else { 'below-v3-active-floor' }
            referenceRmsFullScale = $referenceRms; outputRmsFullScale = $outputRms; outputToReferenceRmsRatio = if ($referenceRms -eq 0) { $null } else { $outputRms / $referenceRms }
            signedCorrelation = $metrics.signedCorrelation; normalizedRmsError = $metrics.normalizedRmsError; snrDb = $metrics.snrDb
            referenceMinimumWindow = if ($_.findingType -eq 'active-window-silence') { [ordered]@{ startSample=$_.startSample; endSampleExclusive=$_.endSampleExclusive; rmsFullScale=$referenceRms } } else { $null }
            routeMinimumWindow = if ($null -ne $routeMinimum) { [ordered]@{ startSample=$routeMinimum.startSample; endSampleExclusive=$routeMinimum.endSampleExclusive; startSeconds=[double]$routeMinimum.startSample/48000; endSeconds=[double]$routeMinimum.endSampleExclusive/48000; referenceRmsAtRouteMinimum=$referenceAtRouteMinimum; outputRmsFullScale=$routeMinimum.rmsFullScale; outputToReferenceRmsRatio=if($referenceAtRouteMinimum-eq0){$null}else{$routeMinimum.rmsFullScale/$referenceAtRouteMinimum} } } else { $null }
        }
    })
}

function Invoke-G05Stage2AAudioDiagnostic {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $FixtureRoot,
        [Parameter(Mandatory)] [string] $WorkloadContractPath,
        [Parameter(Mandatory)] [string] $OracleContractPath,
        [Parameter(Mandatory)] [string] $AmendmentPath,
        [Parameter(Mandatory)] [string] $FreezePath,
        [Parameter(Mandatory)] [string] $WebmDecodedPcmPath,
        [Parameter(Mandatory)] [string] $WebmAttemptSummaryPath,
        [Parameter(Mandatory)] [string] $WebmAttemptSummarySha256,
        [Parameter(Mandatory)] [string] $Mp4DecodedPcmPath,
        [Parameter(Mandatory)] [string] $Mp4AttemptSummaryPath,
        [Parameter(Mandatory)] [string] $Mp4AttemptSummarySha256,
        [Parameter(Mandatory)] [string] $OutputPath,
        [int] $ExpectedRetainedFindingCount = 25
    )

    if (Test-Path -LiteralPath $OutputPath) { throw 'Diagnostic output path must be new.' }
    $workloadContract = Read-G05Stage2AAudioDiagnosticJson $WorkloadContractPath 'Workload contract' 'CBB93CC1483FECD65489485CB1BBF03CD3BF24C2419D28C587C62758C3EAD7EC'
    $oracleContract = Read-G05Stage2AAudioDiagnosticJson $OracleContractPath 'Frozen oracle contract' '119A4C179BFA010F3202DBF6AA368E42EDE5FD0FC23EF2781AA9C7F63540CBE4'
    $amendment = Read-G05Stage2AAudioDiagnosticJson $AmendmentPath 'V4 amendment' '21ECAFCD94F71E58AA43955079EF9959C135DB12530D015E8380CFD09B5E9FBC'
    $freeze = Read-G05Stage2AAudioDiagnosticJson $FreezePath 'Frozen oracle closure' 'E2EFFD683FFE21BE902D77D7564F81C550F555C0989871C5D98B2DBE580D4CB2'
    if ($workloadContract.contractId -ne 'Gate0.G05.Stage2.Workloads.V1.OwnerApproved.20260826' -or $oracleContract.contractId -ne 'Gate0.G05.LossyAudioOracle.V3.Frozen.20260826' -or $amendment.amendmentId -ne 'Gate0.G05.LossyAudioOracle.V4.ReferenceRelativeTypical.20260827' -or $freeze.freezeId -ne 'Gate0.G05.LossyAudioOracle.V4.ReferenceRelativeTypical.Frozen.20260827' -or @($amendment.scope.referenceDescriptorIds | Where-Object { $_ -eq 'stress-4v8a-30s' }).Count -ne 0 -or -not [bool]$amendment.scope.otherReferenceDescriptorsRemainExactV3) { throw 'Diagnostic inputs are not the exact frozen Stage 2A stress contracts.' }
    $webmSummary = Read-G05Stage2AAudioDiagnosticJson $WebmAttemptSummaryPath 'WebM retained attempt summary' $WebmAttemptSummarySha256
    $mp4Summary = Read-G05Stage2AAudioDiagnosticJson $Mp4AttemptSummaryPath 'MP4 retained attempt summary' $Mp4AttemptSummarySha256
    $workload = @($workloadContract.workloads | Where-Object id -eq 'stress-4v8a')
    $descriptor = @($oracleContract.referenceDescriptors | Where-Object id -eq 'stress-4v8a-30s')
    if ($workload.Count -ne 1 -or $descriptor.Count -ne 1 -or [string]$workload[0].audioReferenceDescriptor -ne [string]$descriptor[0].id) { throw 'Frozen stress workload/descriptor binding is invalid.' }
    $expectedBytes = [int]$descriptor[0].samplesPerChannel * [int]$descriptor[0].channels * 2
    foreach ($input in @(@($WebmDecodedPcmPath, (Get-G05Stage2AAudioDiagnosticExpectedPcmHash $webmSummary), 'WebM', 0), @($Mp4DecodedPcmPath, (Get-G05Stage2AAudioDiagnosticExpectedPcmHash $mp4Summary), 'MP4', 1024))) {
        $length = if (Test-Path -LiteralPath $input[0] -PathType Leaf) { (Get-Item -LiteralPath $input[0]).Length } else { -1 }
        if (-not [IO.Path]::IsPathRooted([string]$input[0]) -or $length -lt $expectedBytes -or $length -gt $expectedBytes + (4 * [int]$input[3]) -or $length % 4 -ne 0 -or (Get-G05Stage2AAudioDiagnosticHash $input[0]) -ne [string]$input[1]) { throw "$($input[2]) retained decoded PCM binding is invalid." }
    }
    $parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath)); if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Diagnostic output parent is absent.' }
    # The immutable semantic helper resolves its hash primitive by command name,
    # so expose the already-reviewed proof helper for this diagnostic invocation.
    Import-Module (Join-Path $PSScriptRoot 'G05Stage2SmokeHelpers.psm1') -Force -Global
    Import-Module (Join-Path $PSScriptRoot 'G05Stage2ASemanticHelpers.psm1') -Force
    $truthPath = Join-Path $parent ('.g05-stage2a-audio-diagnostic-' + [Guid]::NewGuid().ToString('N') + '.s16le')
    try {
        New-G05Stage2AAudioTruth $FixtureRoot $workload[0] $truthPath $descriptor[0] | Out-Null
        $self = Test-G05SmokeAudio $truthPath $truthPath $oracleContract.qualityThresholds $descriptor[0] 1024 $null
        $reference = ConvertTo-G05Stage2AAudioDiagnosticSamples $truthPath $expectedBytes
        $findings = Get-G05Stage2AAudioDiagnosticFindingDetails $self $reference $workload[0] $oracleContract.qualityThresholds $descriptor[0]
        if ($findings.Count -ne $ExpectedRetainedFindingCount) { throw "Reference self-check produced $($findings.Count) active/silence findings; expected $ExpectedRetainedFindingCount." }
        $referenceLabels = @($self.failures | ForEach-Object { [string]$_ }); $webmLabels = Get-G05Stage2AAudioDiagnosticFailureLabels $webmSummary 'WebM'; $mp4Labels = Get-G05Stage2AAudioDiagnosticFailureLabels $mp4Summary 'MP4'
        Assert-G05Stage2AAudioDiagnosticExactLabels $referenceLabels $webmLabels 'WebM'; Assert-G05Stage2AAudioDiagnosticExactLabels $referenceLabels $mp4Labels 'MP4'
        $webmObservations = Assert-G05Stage2AAudioDiagnosticSuccessfulObservations $webmSummary 'WebM'; $mp4Observations = Assert-G05Stage2AAudioDiagnosticSuccessfulObservations $mp4Summary 'MP4'
        $webm = ConvertTo-G05Stage2AAudioDiagnosticSamples $WebmDecodedPcmPath $expectedBytes 0
        $mp4 = ConvertTo-G05Stage2AAudioDiagnosticSamples $Mp4DecodedPcmPath $expectedBytes 1024
        $webmFindings = ConvertTo-G05Stage2AAudioDiagnosticOutputComparison 'webm-vp9-opus' $reference $webm $findings ([int]$oracleContract.qualityThresholds.silenceWindowSamples)
        $mp4Findings = ConvertTo-G05Stage2AAudioDiagnosticOutputComparison 'mp4-openh264-aac' $reference $mp4 $findings ([int]$oracleContract.qualityThresholds.silenceWindowSamples)
        $result = [ordered]@{
            schemaVersion = 1; diagnosticId = 'Gate0.G05.Stage2A.AudioDiagnostic.V1'; status = 'completed-no-media'; noMediaInvoked = $true; retainedDispositionChanged = $false
            inputs = [ordered]@{ workload = [ordered]@{ logicalId = 'repository:eng/gate0/g0.5-stage2-workload-contract.json'; sha256 = Get-G05Stage2AAudioDiagnosticHash $WorkloadContractPath }; frozenOracle = [ordered]@{ logicalId = 'repository:eng/gate0/g0.5-lossy-audio-oracle-contract.json'; sha256 = Get-G05Stage2AAudioDiagnosticHash $OracleContractPath }; amendment = [ordered]@{ logicalId = 'repository:eng/gate0/g0.5-lossy-audio-oracle-amendment-v4.json'; sha256 = Get-G05Stage2AAudioDiagnosticHash $AmendmentPath; stressOverlayApplied = $false }; freeze = [ordered]@{ logicalId = 'repository:eng/gate0/g0.5-lossy-audio-oracle-amendment-v4-freeze.json'; sha256 = Get-G05Stage2AAudioDiagnosticHash $FreezePath }; webm = [ordered]@{ logicalId = 'retained:stress-720p-webm-eight/warmup/decoded-audio.s16le'; summarySha256 = $WebmAttemptSummarySha256; decodedPcmRawSha256 = Get-G05Stage2AAudioDiagnosticHash $WebmDecodedPcmPath; decodedPcmContentBytes = $expectedBytes }; mp4 = [ordered]@{ logicalId = 'retained:stress-720p-mp4-one/warmup/decoded-audio.s16le'; summarySha256 = $Mp4AttemptSummarySha256; decodedPcmRawSha256 = Get-G05Stage2AAudioDiagnosticHash $Mp4DecodedPcmPath; decodedPcmContentBytes = $expectedBytes; maximumRawTailSamples = 1024 } }
            referenceSelfCheck = [ordered]@{ passed = [bool]$self.passed; qualityPassed = [bool]$self.qualityPassed; findingCount = $findings.Count; findings = @($findings) }
            outputs = @([ordered]@{ routeId = 'webm-vp9-opus'; successfulObservations = $webmObservations; findings = @($webmFindings) }, [ordered]@{ routeId = 'mp4-openh264-aac'; successfulObservations = $mp4Observations; findings = @($mp4Findings) })
            crossRouteMateriality = [ordered]@{ findingKeys = @($findings | ForEach-Object { "$($_.region):channel-$($_.channel):$($_.findingType)" }); sameExactFindingKeys = $true; maximumAbsoluteOutputToReferenceRmsRatioDifference = (@(for($index=0;$index-lt$webmFindings.Count;$index++){if($null-ne$webmFindings[$index].outputToReferenceRmsRatio-and$null-ne$mp4Findings[$index].outputToReferenceRmsRatio){[Math]::Abs($webmFindings[$index].outputToReferenceRmsRatio-$mp4Findings[$index].outputToReferenceRmsRatio)}})|Measure-Object -Maximum).Maximum; routeDefectInferred = $false; disposition = 'not-inferred-while-reference-fails-frozen-active-model' }
            classification = if (-not $self.qualityPassed) { 'A-oracle-descriptor-self-inconsistency' } else { 'unresolved-no-route-inference' }
            proposedAmendment = [ordered]@{ status='proposal-only-not-applied'; scope='stress-4v8a-30s only'; mode='reference-relative-active-windows-v1'; replacesOnly=@('qualityThresholds.minimumActiveChannelRmsFullScale','qualityThresholds.minimumActiveReferenceWindowOutputRmsFullScale'); retains='All other V3 structure, timing, correlation, NRMSE, SNR, aggregate RMS ratio, DC, tone, clipping, and onset checks remain required.'; ownerApprovalRequired=$true }
            throwTokenization = [ordered]@{ correction = "throw 'Audio timing or quality oracle failed.'"; retainedDispositionChanged = $false }
        }
        [IO.File]::WriteAllText($OutputPath, ($result | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false)); [pscustomobject]$result
    } finally { if (Test-Path -LiteralPath $truthPath) { Remove-Item -LiteralPath $truthPath -Force } }
}

Export-ModuleMember -Function Get-G05Stage2AAudioDiagnosticHash,Get-G05Stage2AAudioDiagnosticRms,Get-G05Stage2AAudioDiagnosticMinimumWindow,Get-G05Stage2AAudioDiagnosticWindowMetrics,Get-G05Stage2AAudioDiagnosticFindingDetails,Invoke-G05Stage2AAudioDiagnostic
