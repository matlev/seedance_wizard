Set-StrictMode -Version Latest

function Get-G05V5FreezeSha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }
function Assert-G05V5FreezeExistingAbsoluteFile([string] $Path, [string] $Label) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label must be an existing absolute file." }
    [IO.Path]::GetFullPath($Path)
}
function Assert-G05V5FreezeExactKeys([object] $Value, [string[]] $Expected, [string] $Label) {
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    if ($actual.Count -ne $wanted.Count -or @($actual | Where-Object { $_ -notin $wanted }).Count -or @($wanted | Where-Object { $_ -notin $actual }).Count) { throw "$Label schema is not closed." }
}
function Assert-G05V5FreezeHash([object] $Value, [string] $Label) {
    if ($Value -isnot [string] -or $Value -notmatch '^[A-F0-9]{64}$') { throw "$Label must be an uppercase SHA-256." }
}
function Assert-G05V5FreezeBoolean([object] $Value, [string] $Label) {
    if ($Value -isnot [bool]) { throw "$Label must be a Boolean." }
}
function Assert-G05V5FreezeInputSet([object[]] $Inputs, [string] $ScriptRoot) {
    $expected = @(
        'eng/gate0/g0.5-lossy-audio-oracle-contract.json','eng/gate0/g0.5-lossy-audio-oracle-amendment-v4.json','eng/gate0/g0.5-lossy-audio-oracle-amendment-v4-freeze.json','eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json','eng/gate0/g0.5-stage2-workload-contract.json','eng/gate0/G05Stage2SmokeHelpers.psm1','eng/gate0/G05Stage2ASemanticHelpers.psm1','eng/gate0/G05Stage2AV5AudioOracle.psm1','eng/gate0/Invoke-G05LossyAudioOracleControls.ps1','eng/gate0/Invoke-G05StructuredAudioOracleControls.ps1','eng/gate0/g0.5-structured-audio-control-result-summary.json','eng/gate0/Invoke-G05Stage2AV5AudioOracleControls.ps1','eng/gate0/New-G05Stage2AV5AudioOracleFreeze.ps1','eng/gate0/Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1','eng/gate0/New-G05Stage2AV5RetainedOutputReevaluationAuthorization.ps1','eng/gate0/G05Stage2AV5FreezeValidation.psm1','eng/gate0/artifact-retention-manifest.json'
    )
    $seen = @{}
    foreach ($input in @($Inputs)) {
        Assert-G05V5FreezeExactKeys $input @('path','sha256') 'Final freeze frozen input'
        if ($seen.ContainsKey([string]$input.path)) { throw 'Final freeze contains duplicate frozen input paths.' }
        $seen[[string]$input.path] = $input
        Assert-G05V5FreezeHash $input.sha256 "Final freeze frozen input $($input.path) hash"
    }
    if ($seen.Count -ne $expected.Count -or @($expected | Where-Object { -not $seen.ContainsKey($_) }).Count -or @($seen.Keys | Where-Object { $_ -notin $expected }).Count) { throw 'Final freeze frozen input set is not exact.' }
    foreach ($path in $expected) {
        $local = Join-Path $ScriptRoot ([IO.Path]::GetFileName($path))
        if (-not (Test-Path -LiteralPath $local -PathType Leaf) -or $seen[$path].sha256 -ne (Get-G05V5FreezeSha256 $local)) { throw "Final freeze frozen input byte binding changed: $path" }
    }
}
function Assert-G05Stage2AV5FinalFreezeClosure([string] $FinalFreezePath, [string] $ControlReportPath, [string] $FreezeCandidatePath, [string] $V4AuthoritativeControlReportPath, [string] $ScriptRoot) {
    $FinalFreezePath = Assert-G05V5FreezeExistingAbsoluteFile $FinalFreezePath 'FinalFreezePath'
    $ControlReportPath = Assert-G05V5FreezeExistingAbsoluteFile $ControlReportPath 'ControlReportPath'
    $FreezeCandidatePath = Assert-G05V5FreezeExistingAbsoluteFile $FreezeCandidatePath 'FreezeCandidatePath'
    $V4AuthoritativeControlReportPath = Assert-G05V5FreezeExistingAbsoluteFile $V4AuthoritativeControlReportPath 'V4AuthoritativeControlReportPath'
    $freeze = Get-Content -LiteralPath $FinalFreezePath -Raw | ConvertFrom-Json -Depth 64
    Assert-G05V5FreezeExactKeys $freeze @('schemaVersion','freezeId','status','frozenUtc','controlReport','freezeCandidate','frozenInputs','authoritativeV4ControlReport','retainedOutputEvaluationAuthorized','routeReencodeAuthorized') 'Final freeze'
    if ($freeze.schemaVersion -ne 1 -or $freeze.freezeId -ne 'Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.Frozen.20260827' -or $freeze.status -ne 'frozen-after-controls-passed-before-retained-output-reevaluation') { throw 'Final freeze identity is invalid.' }
    Assert-G05V5FreezeBoolean $freeze.retainedOutputEvaluationAuthorized 'Final freeze retainedOutputEvaluationAuthorized'; Assert-G05V5FreezeBoolean $freeze.routeReencodeAuthorized 'Final freeze routeReencodeAuthorized'
    if ($freeze.retainedOutputEvaluationAuthorized -or $freeze.routeReencodeAuthorized) { throw 'Final freeze has widened its execution authorization.' }
    Assert-G05V5FreezeExactKeys $freeze.controlReport @('path','sha256','size') 'Final freeze control report'; Assert-G05V5FreezeExactKeys $freeze.freezeCandidate @('path','sha256','size') 'Final freeze candidate'
    Assert-G05V5FreezeHash $freeze.controlReport.sha256 'Final freeze control report hash'; Assert-G05V5FreezeHash $freeze.freezeCandidate.sha256 'Final freeze candidate hash'
    if ($freeze.controlReport.sha256 -ne (Get-G05V5FreezeSha256 $ControlReportPath) -or $freeze.freezeCandidate.sha256 -ne (Get-G05V5FreezeSha256 $FreezeCandidatePath) -or [int64]$freeze.controlReport.size -ne (Get-Item -LiteralPath $ControlReportPath).Length -or [int64]$freeze.freezeCandidate.size -ne (Get-Item -LiteralPath $FreezeCandidatePath).Length) { throw 'Final freeze control/candidate byte binding changed.' }
    Assert-G05V5FreezeInputSet @($freeze.frozenInputs) $ScriptRoot
    $report = Get-Content -LiteralPath $ControlReportPath -Raw | ConvertFrom-Json -Depth 64
    Assert-G05V5FreezeExactKeys $report @('schemaVersion','controlSetId','status','amendmentId','v3Contract','routeOutputsEvaluated','routeReencodePerformed','retainedMp4OrWebmOutputsRead','stressTruth','structuredControls','legacyV3Controls','legacyV4Controls','executionBoundary') 'V5 control report'
    if ($report.schemaVersion -ne 1 -or $report.controlSetId -ne 'Gate0.G05.LossyAudioOracle.Controls.V5.ReferenceRelativeStress' -or $report.status -ne 'passed-controls-only-freeze-candidate-pending' -or $report.amendmentId -ne 'Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.20260827') { throw 'V5 control report identity is invalid.' }
    Assert-G05V5FreezeExactKeys $report.v3Contract @('path','sha256','contractId') 'V5 control report V3 contract'; Assert-G05V5FreezeHash $report.v3Contract.sha256 'V5 control report V3 contract hash'
    if($report.v3Contract.path -ne 'eng/gate0/g0.5-lossy-audio-oracle-contract.json' -or $report.v3Contract.contractId -ne 'Gate0.G05.LossyAudioOracle.V3.Frozen.20260826' -or $report.v3Contract.sha256 -ne '119A4C179BFA010F3202DBF6AA368E42EDE5FD0FC23EF2781AA9C7F63540CBE4'){throw'V5 control report V3 identity changed.'}
    Assert-G05V5FreezeExactKeys $report.stressTruth @('sha256','size') 'V5 control report stress truth'; Assert-G05V5FreezeHash $report.stressTruth.sha256 'V5 control report stress truth hash'; if($report.stressTruth.sha256 -ne '299846E21A0AF6F1416CCA7BF1BF8ACAC4A5EDDA78EFF9BEB392CC7B992B8CF5' -or [int64]$report.stressTruth.size -ne 5760000){throw'V5 control report stress truth identity changed.'}
    foreach ($property in @('routeOutputsEvaluated','routeReencodePerformed','retainedMp4OrWebmOutputsRead')) { Assert-G05V5FreezeBoolean $report.$property "V5 control report $property"; if ($report.$property) { throw 'V5 controls read or processed retained output.' } }
    Assert-G05V5FreezeExactKeys $report.executionBoundary @('ffmpegInvoked','ffprobeInvoked','mediaProcessesStarted','retainedCodecOutputsRead') 'V5 control execution boundary'
    foreach ($property in $report.executionBoundary.PSObject.Properties) { Assert-G05V5FreezeBoolean $property.Value "V5 control execution boundary $($property.Name)"; if ($property.Value) { throw 'V5 control boundary is widened.' } }
    Assert-G05V5FreezeExactKeys $report.legacyV3Controls @('count','exactBidirectionalIdSetPreserved','allFrozenDispositionsAndHashesPreserved') 'V5 legacy V3 closure'; Assert-G05V5FreezeExactKeys $report.legacyV4Controls @('authoritativeRetentionGroupId','retentionManifestSha256','count','exactBidirectionalIdSetPreserved','allFrozenDispositionsAndHashesPreserved') 'V5 legacy V4 closure'; Assert-G05V5FreezeHash $report.legacyV4Controls.retentionManifestSha256 'V5 legacy V4 retention manifest hash'
    if ($report.legacyV3Controls.count -ne 12 -or $report.legacyV4Controls.count -ne 5 -or $report.legacyV4Controls.authoritativeRetentionGroupId -ne 'G05-V4-Structured-Audio-Controls-20260827-001' -or -not $report.legacyV3Controls.exactBidirectionalIdSetPreserved -or -not $report.legacyV3Controls.allFrozenDispositionsAndHashesPreserved -or -not $report.legacyV4Controls.exactBidirectionalIdSetPreserved -or -not $report.legacyV4Controls.allFrozenDispositionsAndHashesPreserved) { throw 'V5 inherited control closure is incomplete.' }
    $expected = @{'stress-identity'=$true;'stress-gain-95-percent'=$true;'stress-right-low-level-960-sample-dropout'=$false;'stress-gain-75-percent'=$false;'stress-gain-125-percent'=$false}; $actual=@{}
    foreach($entry in @($report.structuredControls)){ if($actual.ContainsKey([string]$entry.id)){throw'Duplicate V5 control id.'};$actual[[string]$entry.id]=$entry }
    if($actual.Count -ne $expected.Count -or @($expected.Keys|Where-Object{-not $actual.ContainsKey($_)}).Count){throw'V5 structured control set is not exact.'}; foreach($id in $expected.Keys){if($actual[$id].expectedPass -ne $expected[$id] -or $actual[$id].actualPass -ne $expected[$id]){throw'V5 structured control disposition changed.'}}
    if(-not(@($actual['stress-right-low-level-960-sample-dropout'].result.failures)-match'reference-relative-window-rms-ratio')){throw'V5 dropout control did not fail through the exact V5 gate.'}
    $v3Contract = Get-Content -LiteralPath (Join-Path $ScriptRoot 'g0.5-lossy-audio-oracle-contract.json') -Raw | ConvertFrom-Json -Depth 64; $v3Expected=@{'identity'=$true;'gain-95-percent'=$true;'noise-24db-snr'=$true;'one-percent-crosstalk'=$true;'midstream-960-sample-dropout'=$false;'gain-75-percent'=$false;'noise-15db-snr'=$false;'polarity-inversion'=$false;'channel-swap'=$false;'clipping'=$false;'silence'=$false;'frequency-offset'=$false};$v3Actual=@{};foreach($entry in @($v3Contract.syntheticControlEvidence.vectors)){if($v3Actual.ContainsKey([string]$entry.id)){throw'Duplicate frozen V3 control id.'};Assert-G05V5FreezeHash $entry.sha256 "Frozen V3 control $($entry.id) hash";$v3Actual[[string]$entry.id]=$entry};if($v3Actual.Count-ne$v3Expected.Count-or@($v3Expected.Keys|Where-Object{-not$v3Actual.ContainsKey($_)}).Count){throw'Frozen V3 control identity set changed.'};foreach($id in $v3Expected.Keys){if($v3Actual[$id].expectedPass-ne$v3Expected[$id]){throw'Frozen V3 control disposition changed.'}}
    $v4SummaryPath=Join-Path $ScriptRoot 'g0.5-structured-audio-control-result-summary.json';if((Get-G05V5FreezeSha256 $v4SummaryPath)-ne'83097DA638B80D048F84CC86B29BB24463C0B2BF9C2D3ED78CAF658FC73BF5EE'){throw'Frozen V4 summary hash changed.'};$v4Summary=Get-Content -LiteralPath $v4SummaryPath -Raw|ConvertFrom-Json -Depth 64;$v4Expected=@{'typical-identity'=$true;'typical-panned-gain-95-percent'=$true;'typical-right-low-level-960-sample-dropout'=$false;'typical-gain-75-percent'=$false;'typical-gain-125-percent'=$false};$v4Actual=@{};foreach($entry in @($v4Summary.structuredControls)){if($v4Actual.ContainsKey([string]$entry.id)){throw'Duplicate frozen V4 control id.'};$v4Actual[[string]$entry.id]=$entry};if($v4Actual.Count-ne$v4Expected.Count-or@($v4Expected.Keys|Where-Object{-not$v4Actual.ContainsKey($_)}).Count){throw'Frozen V4 control identity set changed.'};foreach($id in $v4Expected.Keys){if($v4Actual[$id].expectedPass-ne$v4Expected[$id]-or$v4Actual[$id].actualPass-ne$v4Expected[$id]){throw'Frozen V4 control disposition changed.'}}
    $candidate=Get-Content -LiteralPath $FreezeCandidatePath -Raw|ConvertFrom-Json -Depth 64
    Assert-G05V5FreezeExactKeys $candidate @('schemaVersion','candidateId','status','amendment','authoritativeV4ControlReport','controlReport','frozenInputs','requiredControlVerdicts','retainedOutputEvaluationAuthorized') 'V5 freeze candidate'
    if($candidate.schemaVersion -ne 1 -or $candidate.candidateId -ne 'Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.FreezeCandidate.20260827' -or $candidate.status -ne 'controls-passed-freeze-candidate-not-frozen' -or $candidate.controlReport.sha256 -ne (Get-G05V5FreezeSha256 $ControlReportPath) -or $candidate.retainedOutputEvaluationAuthorized){throw'V5 freeze candidate does not bind passed controls.'}
    Assert-G05V5FreezeInputSet @($candidate.frozenInputs) $ScriptRoot
    $candidateInputs=@{}; foreach($entry in @($candidate.frozenInputs)){$candidateInputs[[string]$entry.path]=[string]$entry.sha256}; foreach($entry in @($freeze.frozenInputs)){if($candidateInputs[[string]$entry.path] -ne [string]$entry.sha256){throw'Final freeze inputs do not exactly preserve the candidate inputs.'}}
    Assert-G05V5FreezeExactKeys $candidate.amendment @('amendmentId','status','referenceDescriptorIds','overlay') 'V5 freeze candidate amendment'; Assert-G05V5FreezeExactKeys $candidate.authoritativeV4ControlReport @('groupId','artifactId','filename','sha256','size') 'V5 freeze candidate V4 report'; Assert-G05V5FreezeExactKeys $candidate.controlReport @('path','sha256','size') 'V5 freeze candidate control report'; Assert-G05V5FreezeExactKeys $candidate.requiredControlVerdicts @('v5','v3','v4','allPassed') 'V5 freeze candidate required verdicts'; Assert-G05V5FreezeHash $candidate.authoritativeV4ControlReport.sha256 'V5 candidate V4 report hash'; Assert-G05V5FreezeHash $candidate.controlReport.sha256 'V5 candidate control report hash'; Assert-G05V5FreezeBoolean $candidate.requiredControlVerdicts.allPassed 'V5 candidate allPassed'; Assert-G05V5FreezeBoolean $candidate.retainedOutputEvaluationAuthorized 'V5 candidate retainedOutputEvaluationAuthorized'
    if($candidate.amendment.amendmentId-ne'Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.20260827'-or$candidate.amendment.status-ne'owner-approved-controls-required-before-retained-output-reevaluation'-or@($candidate.amendment.referenceDescriptorIds).Count-ne1-or$candidate.amendment.referenceDescriptorIds[0]-ne'stress-4v8a-30s'-or$candidate.requiredControlVerdicts.v5-ne5-or$candidate.requiredControlVerdicts.v3-ne12-or$candidate.requiredControlVerdicts.v4-ne5-or-not$candidate.requiredControlVerdicts.allPassed){throw'V5 freeze candidate evidence identity or verdict closure changed.'}
    if($candidate.authoritativeV4ControlReport.sha256 -ne (Get-G05V5FreezeSha256 $V4AuthoritativeControlReportPath) -or $candidate.authoritativeV4ControlReport.sha256 -ne '2CAEE1C652F292BBF7E9DB6E1DAA0DD7C5E68788C3E5D74F63997DC3775F2AF6'){throw'V5 authoritative V4 report binding changed.'}
    [ordered]@{ FinalFreezePath=$FinalFreezePath; ControlReportPath=$ControlReportPath; FreezeCandidatePath=$FreezeCandidatePath; V4AuthoritativeControlReportPath=$V4AuthoritativeControlReportPath; Freeze=$freeze; Candidate=$candidate }
}

Export-ModuleMember -Function Get-G05V5FreezeSha256, Assert-G05V5FreezeExistingAbsoluteFile, Assert-G05Stage2AV5FinalFreezeClosure
