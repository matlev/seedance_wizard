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
Import-Module (Join-Path $PSScriptRoot 'G05MarkerSurvivabilityHelpers.psm1') -Force

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

function Invoke-G05Stage2ACapturedCommand([string] $Executable, [string[]] $Tokens, [string] $StdoutPath, [string] $StderrPath) {
    & $Executable @Tokens 1> $StdoutPath 2> $StderrPath
    foreach($path in @($StdoutPath,$StderrPath)){if(Test-Path -LiteralPath $path -PathType Leaf){$text=[IO.File]::ReadAllText($path);foreach($root in @($RuntimeRoot,$ArtifactRoot,$StagingRoot)){if(-not[string]::IsNullOrWhiteSpace($root)){$text=$text.Replace($root,'{redacted-root}',[StringComparison]::OrdinalIgnoreCase)}};[IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))}}
    [ordered]@{exitCode=$LASTEXITCODE;stdoutSha256=(Get-G05SmokeHash $StdoutPath);stderrSha256=(Get-G05SmokeHash $StderrPath)}
}

function ConvertTo-G05Stage2ASanitizedText([string] $Value, [hashtable] $Roots) {
    $result = if ($null -eq $Value) { '' } else { $Value }
    foreach ($entry in $Roots.GetEnumerator()) {
        $root = [string]$entry.Value
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        $replacement = "{$($entry.Key)}"
        $result = $result.Replace($root, $replacement, [StringComparison]::OrdinalIgnoreCase)
        $result = $result.Replace($root.Replace('\','/'), $replacement, [StringComparison]::OrdinalIgnoreCase)
    }
    $result
}

function Protect-G05Stage2ALog([string] $Path, [hashtable] $Roots) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        [IO.File]::WriteAllText($Path, (ConvertTo-G05Stage2ASanitizedText ([IO.File]::ReadAllText($Path)) $Roots), [Text.UTF8Encoding]::new($false))
    }
}

function Assert-G05Stage2ANoActiveMedia([string] $Scope) {
    $active = @(Get-Process -Name ffmpeg,ffprobe -ErrorAction SilentlyContinue)
    if ($active.Count -ne 0) { throw "Active ffmpeg or ffprobe process blocks Stage 2A $Scope." }
}

function Test-G05Stage2ADeterministicRouteIntegrityDisposition([string] $Disposition) {
    $Disposition -in @('byte-divergent','semantically-divergent','structurally-divergent')
}

function Get-G05Stage2ADescriptor([object] $Contract, [object] $Route, [object] $Variant, [object] $Probe) {
    $expected = @($Contract.markerQualification.requiredRouteQualityProfiles | Where-Object { $_.routeId -eq $Route.id -and $_.qualityProfileId -eq $Route.qualityProfileId })
    $video = @($Probe.streams | Where-Object codec_type -eq 'video'); $audio = @($Probe.streams | Where-Object codec_type -eq 'audio')
    if ($expected.Count -ne 1 -or $video.Count -ne 1 -or $audio.Count -ne 1) { throw 'Frozen output descriptor has an unexpected stream shape.' }
    $actual = [ordered]@{formatName=[string]$Probe.format.format_name;videoCodec=[string]$video[0].codec_name;videoProfile=[string]$video[0].profile;pixelFormat=[string]$video[0].pix_fmt;width=[int]$video[0].width;height=[int]$video[0].height;rFrameRate=[string]$video[0].r_frame_rate;avgFrameRate=[string]$video[0].avg_frame_rate;audioCodec=[string]$audio[0].codec_name;audioProfile=if($null -eq $audio[0].PSObject.Properties['profile']){$null}else{[string]$audio[0].profile};audioSampleRate=[int]$audio[0].sample_rate;audioChannels=[int]$audio[0].channels;audioChannelLayout=[string]$audio[0].channel_layout}
    $wanted=$expected[0].observedDescriptor
    $checks=[ordered]@{formatName=$actual.formatName -eq $wanted.formatName;videoCodec=$actual.videoCodec -eq $wanted.videoCodec;videoProfile=$actual.videoProfile -eq $wanted.videoProfile;pixelFormat=$actual.pixelFormat -eq $wanted.pixelFormat;width=$actual.width -eq [int]$Variant.width;height=$actual.height -eq [int]$Variant.height;rFrameRate=$actual.rFrameRate -eq $wanted.frameRate;avgFrameRate=$actual.avgFrameRate -eq $wanted.frameRate;audioCodec=$actual.audioCodec -eq $wanted.audioCodec;audioProfile=if($null -eq $wanted.audioProfile){$true}else{$actual.audioProfile -eq $wanted.audioProfile};audioSampleRate=$actual.audioSampleRate -eq [int]$wanted.audioSampleRate;audioChannels=$actual.audioChannels -eq [int]$wanted.audioChannels;audioChannelLayout=$actual.audioChannelLayout -eq $wanted.audioChannelLayout}
    if (@($checks.Values | Where-Object { -not $_ }).Count) { throw ('Frozen output descriptor mismatch: '+(@($checks.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object Key) -join ',')) }
    $effectiveExpected=[ordered]@{formatName=$wanted.formatName;videoCodec=$wanted.videoCodec;videoProfile=$wanted.videoProfile;pixelFormat=$wanted.pixelFormat;width=[int]$Variant.width;height=[int]$Variant.height;frameRate=$wanted.frameRate;audioCodec=$wanted.audioCodec;audioProfile=$wanted.audioProfile;audioSampleRate=[int]$wanted.audioSampleRate;audioChannels=[int]$wanted.audioChannels;audioChannelLayout=$wanted.audioChannelLayout}
    [ordered]@{passed=$true;expected=$effectiveExpected;observed=$actual;criteria=$checks;videoStream=$video[0];audioStream=$audio[0]}
}

function Assert-G05Stage2AAudioOracleClosure([string] $AudioContractPath, [object] $AudioContract, [object] $Amendment) {
    $freezePath=Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v4-freeze.json';$controlPath=Join-Path $PSScriptRoot 'g0.5-structured-audio-control-result-summary.json';$retentionPath=Join-Path $PSScriptRoot 'g0.5-structured-audio-control-retention-result-summary.json'
    foreach($path in @($freezePath,$controlPath,$retentionPath)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw'Frozen V4 audio-oracle closure is incomplete before route inspection.'}}
    $freeze=Get-Content -LiteralPath $freezePath -Raw|ConvertFrom-Json -Depth 64;$control=Get-Content -LiteralPath $controlPath -Raw|ConvertFrom-Json -Depth 64;$retention=Get-Content -LiteralPath $retentionPath -Raw|ConvertFrom-Json -Depth 64
    if([string]$freeze.v3Contract.sha256 -ne (Get-G05SmokeHash $AudioContractPath) -or [string]$freeze.v4Amendment.sha256 -ne (Get-G05SmokeHash (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v4.json')) -or [string]$control.freezeId -ne [string]$freeze.freezeId -or [string]$retention.freeze.retainedSnapshotSha256 -ne (Get-G05SmokeHash $freezePath) -or [string]$control.status -ne 'passed-controls-only-retention-pending' -or [string]$retention.status -ne 'complete' -or -not [bool]$freeze.controlEvidence.allDeclaredDispositionsPassed -or -not [bool]$freeze.controlEvidence.legacyHashesAndDispositionsPreserved -or [bool]$freeze.controlEvidence.routeOutputsEvaluated -or [int]$freeze.controlEvidence.structuredControlCount -ne 5 -or [int]$freeze.controlEvidence.legacyV3ControlCount -ne 12 -or -not [bool]$control.legacyV3Controls.allFrozenHashesAndDispositionsPreserved -or -not [bool]$control.legacyV3Controls.allNoOverlayEffectiveOracleDispositionsPreserved -or [bool]$control.executionBoundaries.routeOutputsEvaluated -or [string]$control.controlReport.sha256 -ne [string]$freeze.controlEvidence.sha256 -or [string]$retention.controlGroup.controlReportSha256 -ne [string]$freeze.controlEvidence.sha256){throw'Frozen V4 audio-oracle amendment/control/retention chain does not bind before route inspection.'}
    [ordered]@{audioContractSha256=(Get-G05SmokeHash $AudioContractPath);amendmentSha256=(Get-G05SmokeHash (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v4.json'));freezeSha256=(Get-G05SmokeHash $freezePath);controlSha256=(Get-G05SmokeHash $controlPath);retentionSha256=(Get-G05SmokeHash $retentionPath)}
}

function Invoke-G05Stage2ALiveMatrix([string] $RuntimeRoot, [string] $ArtifactRoot, [string] $StagingRoot, [object] $Schedule, [object] $WorkloadContract, [object] $RetentionContract, [object] $AuthorizationRecord) {
    # This entry point is reachable only after the hash-bound authorization is effective.
    $replacementSmokeClosure = Assert-G05Stage2AReplacementSmokeClosure $repositoryRoot
    $matrixPreflight = & (Join-Path $PSScriptRoot 'Test-G05Stage2AMatrixPreflight.ps1') -RuntimeRoot $RuntimeRoot -ArtifactRoot $ArtifactRoot -StagingRoot $StagingRoot
    Assert-G05Stage2ANoActiveMedia 'matrix start'
    $fixture = Join-Path $ArtifactRoot 'fixtures'; $ffmpeg = Join-Path $RuntimeRoot 'bin/ffmpeg.exe'; $ffprobe = Join-Path $RuntimeRoot 'bin/ffprobe.exe'
    $audioContractPath = Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-contract.json'; $audioContract = Get-Content -LiteralPath $audioContractPath -Raw | ConvertFrom-Json -Depth 100
    $amendment = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v4.json') -Raw | ConvertFrom-Json -Depth 100
    $audioOracleClosure = Assert-G05Stage2AAudioOracleClosure $audioContractPath $audioContract $amendment
    $runId = "g05-stage2a-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
    $runRoot = Join-Path $StagingRoot $runId; [IO.Directory]::CreateDirectory($runRoot) | Out-Null
    $portableRoots = @{ runtimeRoot=$RuntimeRoot; artifactRoot=$ArtifactRoot; stagingRoot=$StagingRoot; stage=$runRoot; repositoryRoot=$repositoryRoot }
    $startingRootIndex = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/root-index.json') -Raw | ConvertFrom-Json -Depth 16
    $truthRoot = Join-Path $runRoot 'run-audio-truth'; [IO.Directory]::CreateDirectory($truthRoot) | Out-Null
    $truthByWorkload = @{}
    foreach ($workload in @($WorkloadContract.workloads | Where-Object { $_.id -in @('baseline-1v1a','typical-2v4a','stress-4v8a') })) {
        $truthDescriptor = @($audioContract.referenceDescriptors | Where-Object id -eq $workload.audioReferenceDescriptor)
        if ($truthDescriptor.Count -ne 1) { throw "Frozen audio descriptor is absent for $($workload.id)." }
        $truth = Join-Path $truthRoot "$($workload.id).s16le"
        New-G05Stage2AAudioTruth $fixture $workload $truth $truthDescriptor[0] | Out-Null
        $truthByWorkload[[string]$workload.id] = [ordered]@{ path=$truth; descriptor=$truthDescriptor[0]; rawSha256=(Get-G05SmokeHash $truth); contentNormalizedSha256=(Get-G05Stage2AContentNormalizedAudioHash $truth) }
    }
    $suspended = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); $cells = @(); $allAttempts = @()
    foreach ($cellId in @($Schedule.Schedule.attempts | Select-Object -ExpandProperty cellId -Unique)) {
        $cell = Get-G05Stage2ACellRows $Schedule.Schedule $WorkloadContract $cellId
        $cellPreflight = & (Join-Path $PSScriptRoot 'Test-G05Stage2AMatrixPreflight.ps1') -RuntimeRoot $RuntimeRoot -ArtifactRoot $ArtifactRoot -StagingRoot $StagingRoot -PerCell -ExpectedOrdinaryClosureBytes $RetentionContract.Contract.ordinaryClosureBytes -ExpectedCompactRepeatBytes $RetentionContract.Contract.compactPassingRepeatMaximumBytes -ExpectedExceptionalClosureBytes $RetentionContract.Contract.exceptionalClosureBytes
        $cellRoot = Join-Path $runRoot $cellId; [IO.Directory]::CreateDirectory($cellRoot) | Out-Null
        Write-G05Stage2ASemanticJson (Join-Path $cellRoot 'matrix-preflight.json') ([ordered]@{preflight=$matrixPreflight;replacementSmokeClosure=$replacementSmokeClosure})
        Write-G05Stage2ASemanticJson (Join-Path $cellRoot 'cell-preflight.json') $cellPreflight
        $bindings = @(); $cellAttempts = @(); $truthRecord = $truthByWorkload[[string]$cell.Workload.id]; if($null -eq $truthRecord){throw 'Run-scoped frozen audio truth is absent.'}; $truthPath=[string]$truthRecord.path; $descriptor=@($truthRecord.descriptor)
        $overlay = if ($cell.Workload.id -eq 'typical-2v4a') { @($amendment.descriptorOverlays | Where-Object referenceDescriptorId -eq $descriptor[0].id)[0] } else { $null }
        if($null -ne $overlay){Assert-G05SmokeReferenceRelativeOverlay $descriptor[0] $overlay}
        foreach($row in $cell.Attempts) {
            $attemptRoot = Join-Path $cellRoot ("attempt-$($row.cellAttemptOrdinal)"); [IO.Directory]::CreateDirectory($attemptRoot) | Out-Null
            $summary=[ordered]@{attemptId="stage2a-$($row.globalOrdinal)";globalOrdinal=$row.globalOrdinal;phase=$row.phase;disposition='blocked';startedUtc=[DateTimeOffset]::UtcNow.ToString('O');completedUtc=$null;selectedComponents=[ordered]@{inputProfiles=@($cell.Workload.inputs.profile);filters=@('scale','format','setpts','crop','overlay','aformat','volume','pan','adelay','amix','atrim','asetpts');videoEncoder=$cell.Route.videoEncoder;audioEncoder=$cell.Route.audioEncoder;muxer=$cell.Route.muxer;demuxer=(Get-G05SmokeDemuxer $cell.Route.muxer);videoDecoder=$cell.Route.outputDecoders[0];audioDecoder=$cell.Route.outputDecoders[1];threads=$cell.Threads;runtimeProfile='P2.BtbnLgplShared.WindowsX64.20260820'};commands=[ordered]@{};validations=[ordered]@{encode=$false;probe=$false;timing=$false;visual=$false;audio=$false;cleanup=$false};hashes=[ordered]@{outputSha256=$null;frameProbeSha256=$null;packetProbeSha256=$null;decodedVideoIdentitySha256=$null;decodedAudioRawSha256=$null;decodedAudioContentNormalizedSha256=$null};encodedByteEqualityClaim=$false;measurement=$null;cleanup=[ordered]@{processStarted=$false;processTreeRootExited=$true;processTreeOrphanFree=$true;noUnvalidatedPartialOutput=$true;partialOutput='none'};failures=@();boundaryLimitations=@('Runtime-route proof only; no product, WPF, cache, shipping-runtime, distribution, or legal conclusion.')}
            $rootIndex = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/root-index.json') -Raw | ConvertFrom-Json -Depth 16
            $localCellBytes = [int64](@(Get-ChildItem -LiteralPath $cellRoot -File -Recurse | ForEach-Object Length | Measure-Object -Sum).Sum)
            $worstCaseBytes = [int64]$rootIndex.totals.logicalArtifactBytes + $localCellBytes + [int64]$RetentionContract.Contract.exceptionalClosureBytes
            if($worstCaseBytes -gt [int64]$RetentionContract.Contract.stage2ARetentionCeilingBytes){$summary.reason='blocked-before-media-insufficient-global-headroom-for-worst-case-full-closure'}elseif($suspended.Contains([string]$row.routeId)){$summary.reason='route-suspended-after-deterministic-integrity-failure'}else{
              $output=$null;$measurement=$null;$failureDisposition='blocked'
              try {
                Assert-G05Stage2ANoActiveMedia "attempt $($row.globalOrdinal) start"
                $failureDisposition='failed';$output=Join-Path $attemptRoot ("output.$($cell.Route.container)");$tokens=New-G05Stage2AEncodeTokens $cell $WorkloadContract $ArtifactRoot $output
                $summary.commands.encode=[ordered]@{executable='{runtimeRoot}/bin/ffmpeg.exe';tokens=(ConvertTo-G05SmokePortableTokens $tokens $portableRoots)}
                $encodeStdout=Join-Path $attemptRoot 'encode.stdout.txt';$encodeStderr=Join-Path $attemptRoot 'encode.stderr.txt';$measurement=Invoke-G05SmokeObservedProcess $ffmpeg $tokens $attemptRoot $encodeStdout $encodeStderr
                Protect-G05Stage2ALog $encodeStdout $portableRoots;Protect-G05Stage2ALog $encodeStderr $portableRoots
                $summary.measurement=[ordered]@{startedUtc=$measurement.startedUtc.ToString('O');completedUtc=$measurement.completedUtc.ToString('O');exitCode=[int]$measurement.exitCode;rootPid=[int]$measurement.rootPid;summary=$measurement.summary;samples=@($measurement.samples);processTree=$measurement.processTree}
                $summary.cleanup.processStarted=$true;$summary.cleanup.processTreeRootExited=[bool]$measurement.processTree.rootExited;$summary.cleanup.processTreeOrphanFree=[bool]$measurement.processTree.orphanFree
                [IO.File]::WriteAllLines((Join-Path $attemptRoot 'process-samples.ndjson'),@($measurement.samples|ForEach-Object{$_|ConvertTo-Json -Compress}),[Text.UTF8Encoding]::new($false))
                if($measurement.exitCode -ne 0 -or -not $measurement.processTree.orphanFree -or -not(Test-Path $output -PathType Leaf)){throw 'Observed encode did not close successfully and orphan-free.'};$summary.validations.encode=$true
                $failureDisposition='failed';$demuxer=Get-G05SmokeDemuxer $cell.Route.muxer;$framePath=Join-Path $attemptRoot 'probe-frames.json';$packetPath=Join-Path $attemptRoot 'probe-packets.json';$frameTokens=@('-v','error','-f',$demuxer,'-show_streams','-show_frames','-show_format','-of','json',$output);$packetTokens=@('-v','error','-f',$demuxer,'-show_packets','-of','json',$output)
                $summary.commands.probeFrames=[ordered]@{executable='{runtimeRoot}/bin/ffprobe.exe';tokens=(ConvertTo-G05SmokePortableTokens $frameTokens $portableRoots)};$summary.commands.probePackets=[ordered]@{executable='{runtimeRoot}/bin/ffprobe.exe';tokens=(ConvertTo-G05SmokePortableTokens $packetTokens $portableRoots)}
                if((Invoke-G05Stage2ACapturedCommand $ffprobe $frameTokens $framePath (Join-Path $attemptRoot 'probe-frames.stderr.txt')).exitCode-ne0){throw'Forced-demuxer frame probe failed.'};if((Invoke-G05Stage2ACapturedCommand $ffprobe $packetTokens $packetPath (Join-Path $attemptRoot 'probe-packets.stderr.txt')).exitCode-ne0){throw'Forced-demuxer packet probe failed.'}
                $failureDisposition='structurally-divergent';$probe=Get-Content $framePath -Raw|ConvertFrom-Json -Depth 100;$packets=Get-Content $packetPath -Raw|ConvertFrom-Json -Depth 100;$summary.descriptor=Get-G05Stage2ADescriptor $WorkloadContract $cell.Route $cell.Variant $probe;$summary.videoTiming=Get-G05Stage2AExactVideoTiming $summary.descriptor.videoStream @($probe.frames|Where-Object media_type -eq 'video');$summary.hashes.frameProbeSha256=Get-G05SmokeHash $framePath;$summary.hashes.packetProbeSha256=Get-G05SmokeHash $packetPath;$summary.validations.probe=$true;$summary.validations.timing=$true
                $failureDisposition='semantically-divergent';$visualLog=Join-Path $attemptRoot 'visual.stderr.txt';$visual=Test-G05Stage2AVisual $ffmpeg $demuxer $cell.Route.outputDecoders[0] $output $fixture $visualLog (Join-Path $attemptRoot 'visual-mae.ndjson') $cell.Workload $cell.Variant;Protect-G05Stage2ALog $visualLog $portableRoots;if(-not$visual.passed){throw'Visual oracle failed.'};$summary.visual=$visual;$summary.hashes.decodedVideoIdentitySha256=$visual.decodedVideoIdentitySha256;$summary.validations.visual=$true
                $failureDisposition='failed';$pcm=Join-Path $attemptRoot 'decoded-audio.s16le';$audioTokens=@('-v','error','-xerror','-err_detect','explode','-f',$demuxer,'-c:a',$cell.Route.outputDecoders[1],'-threads:a',[string]$cell.Threads,'-i',$output,'-map','0:a:0','-vn','-c:a','pcm_s16le','-f','s16le','-y',$pcm);$summary.commands.audioDecode=[ordered]@{executable='{runtimeRoot}/bin/ffmpeg.exe';tokens=(ConvertTo-G05SmokePortableTokens $audioTokens $portableRoots)}
                if((Invoke-G05Stage2ACapturedCommand $ffmpeg $audioTokens (Join-Path $attemptRoot 'audio.stdout.txt') (Join-Path $attemptRoot 'audio.stderr.txt')).exitCode-ne0){throw'Strict native audio decode failed.'}
                $failureDisposition='semantically-divergent';$summary.audio=[ordered]@{truth=[ordered]@{rawSha256=$truthRecord.rawSha256;contentNormalizedSha256=$truthRecord.contentNormalizedSha256};timing=(Get-G05DecodedAudioTiming (Get-Item $pcm).Length $summary.descriptor.audioStream @($probe.frames|Where-Object media_type -eq 'audio') @($packets.packets|Where-Object codec_type -eq 'audio') 1440000 1024);quality=(Test-G05SmokeAudio $truthPath $pcm $audioContract.qualityThresholds $descriptor[0] 1024 $overlay)}
                if(-not$summary.audio.timing.passed -or -not$summary.audio.quality.passed){throw'Audio timing or quality oracle failed.'};$summary.hashes.decodedAudioRawSha256=Get-G05SmokeHash $pcm;$normalizedAudioHash=Get-G05Stage2AContentNormalizedAudioHash $pcm ([int64]$descriptor[0].referencePcmSize) 1024;if($normalizedAudioHash -ne [string]$summary.audio.quality.contentNormalized.sha256){throw'Independent normalized decoded-audio identity hashes differ.'};$summary.hashes.decodedAudioContentNormalizedSha256=$normalizedAudioHash;$summary.validations.audio=$true;$summary.hashes.outputSha256=Get-G05SmokeHash $output;$summary.outputBytes=(Get-Item $output).Length
                $failureDisposition='cleanup-failed';Assert-G05Stage2ANoActiveMedia "attempt $($row.globalOrdinal) completion";$summary.cleanup.noUnvalidatedPartialOutput=$true;$summary.cleanup.partialOutput='not-applicable-complete-output-retained';$summary.validations.cleanup=$true;$summary.disposition='passed'
              } catch {
                $summary.disposition=$failureDisposition;$summary.failures+=,(ConvertTo-G05Stage2ASanitizedText $_.Exception.Message $portableRoots)
                foreach($log in @(Get-ChildItem -LiteralPath $attemptRoot -File -Filter '*.txt' -ErrorAction SilentlyContinue)){Protect-G05Stage2ALog $log.FullName $portableRoots}
                if($null-ne$measurement){$summary.cleanup.processTreeRootExited=[bool]$measurement.processTree.rootExited;$summary.cleanup.processTreeOrphanFree=[bool]$measurement.processTree.orphanFree;if(-not$measurement.processTree.orphanFree){$summary.disposition='orphan-producing'}elseif(-not$measurement.processTree.rootExited){$summary.disposition='cleanup-failed'}}
                if($null-ne$output-and(Test-Path -LiteralPath $output -PathType Leaf)){
                    if($null-ne$measurement-and$measurement.exitCode-eq0-and$measurement.processTree.orphanFree){$summary.cleanup.partialOutput='complete-output-retained-for-exceptional-oracle-or-structure-evidence';$summary.cleanup.noUnvalidatedPartialOutput=$true}
                    else{$summary.cleanup.partialOutput=[ordered]@{action='removed';bytes=(Get-Item $output).Length;sha256=(Get-G05SmokeHash $output)};Remove-Item -LiteralPath $output -Force;$summary.cleanup.noUnvalidatedPartialOutput=-not(Test-Path -LiteralPath $output)}
                }
                try{Assert-G05Stage2ANoActiveMedia "attempt $($row.globalOrdinal) failure cleanup"}catch{$summary.disposition='orphan-producing';$summary.cleanup.processTreeOrphanFree=$false;$summary.failures+=,(ConvertTo-G05Stage2ASanitizedText $_.Exception.Message $portableRoots)}
                $summary.validations.cleanup=[bool]($summary.cleanup.processTreeRootExited-and$summary.cleanup.processTreeOrphanFree-and$summary.cleanup.noUnvalidatedPartialOutput)
              }
            }
            $summary.completedUtc=[DateTimeOffset]::UtcNow.ToString('O');Assert-G05Stage2AAttemptSummary $summary
            $cellAttempts+=,$summary;$allAttempts+=,$summary;if(Test-G05Stage2ADeterministicIntegrityFailure $summary){[void]$suspended.Add([string]$row.routeId)}
        }
        $retentionPlan=Resolve-G05Stage2ACellRetentionPlan @($cellAttempts)
        foreach($summary in @($retentionPlan.attempts)){$attemptRoot=Join-Path $cellRoot ("attempt-$([int]$summary.globalOrdinal - [int]$cell.Attempts[0].globalOrdinal + 1)");$summaryPath=Join-Path $attemptRoot 'summary.json';$binding=New-G05Stage2AAttemptBinding ($cell.Attempts|Where-Object globalOrdinal -eq $summary.globalOrdinal) $summary $summaryPath $cellRoot $summary.retentionClass $summary.completeClosureReference;if($summary.retentionClass-eq'compact'){Assert-G05Stage2ACompactBinding $binding $cellRoot $RetentionContract.Contract.compactPassingRepeatMaximumBytes;Get-ChildItem -LiteralPath $attemptRoot -Force|Where-Object Name -ne 'summary.json'|Remove-Item -Recurse -Force};$bindings+=,$binding}
        $bindingsPath=Join-Path $cellRoot 'attempt-bindings.json';Write-G05Stage2ASemanticJson $bindingsPath @($bindings)
        $measured=@($cellAttempts|Where-Object{$_.phase-eq'measured'-and$_.disposition-eq'passed'});$cellMetrics=if($measured.Count-eq5){[ordered]@{wallClockMilliseconds=(Get-G05Stage2AStatistics @($measured|%{[double]$_.measurement.summary.wallClockMilliseconds}));peakWorkingSetBytes=(Get-G05Stage2AStatistics @($measured|%{[double]$_.measurement.summary.peakWorkingSetBytes}));peakPrivateMemoryBytes=(Get-G05Stage2AStatistics @($measured|%{[double]$_.measurement.summary.peakPrivateMemoryBytes}));meanNormalizedCpuPercent=(Get-G05Stage2AStatistics @($measured|%{[double]$_.measurement.summary.meanNormalizedCpuPercent}));readTransferBytes=(Get-G05Stage2AStatistics @($measured|%{[double]$_.measurement.summary.readTransferBytes}));writeTransferBytes=(Get-G05Stage2AStatistics @($measured|%{[double]$_.measurement.summary.writeTransferBytes}));outputBytes=(Get-G05Stage2AStatistics @($measured|%{[double]$_.outputBytes}));maximumFrameMeanAbsoluteError=(Get-G05Stage2AStatistics @($measured|%{[double]$_.visual.maximumFrameMeanAbsoluteError}))}}else{$null}
        $proofRunId="$runId-$($cellId -replace '[^A-Za-z0-9._-]','-')";$destinationName="future/stage2/$proofRunId";$receipt=[ordered]@{proofRunId=$proofRunId;cellId=($cellId -replace '[^A-Za-z0-9._-]','-');destinationName=$destinationName;shardPath="stage2/$proofRunId.manifest.json";rootEntryIdentity=[ordered]@{rootIndexPath='eng/gate0/evidence/root-index.json';proofRunId=$proofRunId;entryIdentity='The enclosing immutable root-index entry is identified by proofRunId and records the final shard hash after commit; this precommit receipt intentionally contains no circular shard hash.'}}
        $cellSummary=[ordered]@{cellId=$cellId;attempts=@($cellAttempts);attemptBindings='attempt-bindings.json';completeClosureReference=(Get-G05Stage2ACompleteClosureReference $bindings);individualMeasuredAttemptIds=@($measured|ForEach-Object attemptId);statistics=$cellMetrics;retainedBytes=[int64](@(Get-ChildItem $cellRoot -File -Recurse|ForEach-Object Length|Measure-Object -Sum).Sum);receipt=$receipt}
        $isFinalCell=([string]$cellId -eq [string]$Schedule.Schedule.attempts[-1].cellId)
        if($isFinalCell){$precommit=[ordered]@{schemaVersion=1;runId=$runId;stage='precommit-final-cell';priorReceipts=@($cells|ForEach-Object receipt);finalReceipt=$receipt;attemptCount=$allAttempts.Count;cellCountBeforeFinal=$cells.Count;limitations=@('This bounded precommit aggregate is retained inside the eighteenth cell shard. It is not an additional shard and contains no circular shard hash.')};Write-G05Stage2ASemanticJson (Join-Path $cellRoot 'aggregate-precommit-run-result.json') $precommit}
        Write-G05Stage2ASemanticJson (Join-Path $cellRoot 'cell-summary.json') $cellSummary
        # The writer is the single remote-retention boundary; it validates the effective authorization and every copied byte.
        $disposition=if(@($cellAttempts|Where-Object{$_.disposition -notin @('passed','blocked')}).Count){'failed'}elseif(@($cellAttempts|Where-Object disposition -eq 'blocked').Count){'blocked'}else{'passed'};$identities=@('repository:eng/gate0/g0.5-stage2a-execution-authorization.json',"sha256:$($AuthorizationRecord.Sha256)",'repository:eng/gate0/g0.5-stage2-workload-contract.json',"sha256:$(Get-G05SmokeHash $workloadContractPath)",'repository:eng/gate0/g0.5-stage2a-retention-contract.json',"sha256:$($RetentionContract.Sha256)",'repository:eng/gate0/g0.5-lossy-audio-oracle-contract.json',"sha256:$($audioOracleClosure.audioContractSha256)",'repository:eng/gate0/g0.5-lossy-audio-oracle-amendment-v4.json',"sha256:$($audioOracleClosure.amendmentSha256)",'repository:eng/gate0/g0.5-lossy-audio-oracle-amendment-v4-freeze.json',"sha256:$($audioOracleClosure.freezeSha256)",'repository:eng/gate0/g0.5-structured-audio-control-result-summary.json',"sha256:$($audioOracleClosure.controlSha256)",'repository:eng/gate0/g0.5-structured-audio-control-retention-result-summary.json',"sha256:$($audioOracleClosure.retentionSha256)",'repository:eng/gate0/g0.5-stage2-replacement-smoke-authorization-summary.json',"sha256:$($replacementSmokeClosure.AuthorizationSha256)",'repository:eng/gate0/g0.5-stage2-replacement-smoke-result-summary.json',"sha256:$($replacementSmokeClosure.ResultSha256)",'repository:docs/gate-0-g0.5-stage2a-execution-approval.md',"sha256:$(Get-G05SmokeHash (Join-Path $repositoryRoot 'docs/gate-0-g0.5-stage2a-execution-approval.md'))");$runtimeIdentities=@('repository:eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json',"sha256:$(Get-G05SmokeHash (Join-Path $PSScriptRoot 'manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json'))","sha256:$($matrixPreflight.runtimeIdentity.primaryTool.sha256)","sha256:$($matrixPreflight.runtimeIdentity.inspectionTool.sha256)");$shard=& (Join-Path $PSScriptRoot 'Add-Gate0EvidenceShard.ps1') -ArtifactRoot $ArtifactRoot -SourceRoot $cellRoot -ProofRunId $proofRunId -EvidenceGroupId $runId -CellId ($cellId -replace '[^A-Za-z0-9._-]','-') -DestinationName $destinationName -EvidenceBoundary p2-runtime-route -Disposition $disposition -ContractIdentity $identities -Provenance 'Owner-authorized Gate 0 G0.5 Stage 2A runtime-route cell evidence.' -ProducerRuntimeIdentity $runtimeIdentities -LicenseRecords @('repository:eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json') -AttemptBindingsPath $bindingsPath
        $shardRecord=Get-Content -LiteralPath (Join-Path $PSScriptRoot "evidence/$($shard.shardPath)") -Raw|ConvertFrom-Json -Depth 32;$cellSummary['shard']=$shard;$cellSummary['distinctR2BytesAdded']=[int64](@($shardRecord.artifacts|Where-Object transferDisposition -eq 'uploaded-and-verified'|ForEach-Object byteSize|Measure-Object -Sum).Sum);$cells+=,$cellSummary
    }
    $rootIndex=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence/root-index.json') -Raw|ConvertFrom-Json -Depth 16
    $failedCount=@($allAttempts|Where-Object{$_.disposition -notin @('passed','blocked')}).Count;$blockedCount=@($allAttempts|Where-Object disposition -eq 'blocked').Count
    [ordered]@{schemaVersion=1;runId=$runId;status=if($failedCount){'completed-with-failures'}elseif($blockedCount){'completed-blocked'}else{'completed'};evidenceBoundary='p2-runtime-route';replacementSmokeClosure=$replacementSmokeClosure;matrixPreflight=$matrixPreflight;cells=@($cells);shards=@($cells|ForEach-Object shard);finalShardReceipt=$cells[-1].shard;finalRootIndexSha256=(Get-G05SmokeHash (Join-Path $PSScriptRoot 'evidence/root-index.json'));attemptCount=$allAttempts.Count;cellCount=$cells.Count;passedAttempts=@($allAttempts|Where-Object disposition -eq 'passed').Count;failedAttempts=$failedCount;blockedAttempts=$blockedCount;suspendedRouteIds=@($suspended);retention=[ordered]@{startingLogicalArtifactBytes=[int64]$startingRootIndex.totals.logicalArtifactBytes;endingLogicalArtifactBytes=[int64]$rootIndex.totals.logicalArtifactBytes;logicalBytesAdded=[int64]$rootIndex.totals.logicalArtifactBytes-[int64]$startingRootIndex.totals.logicalArtifactBytes;distinctR2BytesAdded=[int64](@($cells|ForEach-Object distinctR2BytesAdded|Measure-Object -Sum).Sum);retentionCeilingBytes=[int64]$RetentionContract.Contract.stage2ARetentionCeilingBytes;remainingHeadroomBytes=[int64]$RetentionContract.Contract.stage2ARetentionCeilingBytes-[int64]$rootIndex.totals.logicalArtifactBytes;compactAssumptions=$RetentionContract.Contract.compactRule};limitations=@('Stops after Stage 2A. No product, WPF, concurrency, long-form, shipping-runtime, distribution, or legal claim.')}
}

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

if ([string]$authorization.status -ne 'owner-authorized-and-prerequisites-verified') { throw 'Stage 2A execution remains fail-closed while execution implementation is pending effective authorization; no media was started.' }
if ([string]::IsNullOrWhiteSpace($RuntimeRoot) -or [string]::IsNullOrWhiteSpace($ArtifactRoot) -or [string]::IsNullOrWhiteSpace($StagingRoot)) { throw 'Live execution requires explicit RuntimeRoot, ArtifactRoot, and StagingRoot.' }
$authorizationRecord = Read-G05Stage2AExecutionAuthorization $authorizationPath $repositoryRoot
$liveResult = Invoke-G05Stage2ALiveMatrix $RuntimeRoot $ArtifactRoot $StagingRoot $schedule $workloadContract $retentionContract $authorizationRecord
$liveResult
