[CmdletBinding()]
param(
    [string] $RuntimeRoot,
    [string] $FixtureRoot,
    [string] $ArtifactRoot,
    [switch] $ManualExecution,
    [switch] $AppendRetention,
    [switch] $ContractOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Proof-only direct runtime-route smoke. It never opens ReelForge, invokes WPF,
# selects a shipping runtime, or makes a product-performance claim.
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$projectParent = [IO.Path]::GetDirectoryName($repositoryRoot)
$contractPath = Join-Path $PSScriptRoot 'g0.5-stage2-workload-contract.json'
$audioContractPath = Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-contract.json'
$retentionAppender = Join-Path $PSScriptRoot 'Add-Gate0RetainedProof.ps1'
$retentionValidator = Join-Path $PSScriptRoot 'Test-Gate0ArtifactRetention.ps1'
$runtimeValidator = Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1'
$helperPath = Join-Path $PSScriptRoot 'G05Stage2SmokeHelpers.psm1'
$audioTimingHelperPath = Join-Path $PSScriptRoot 'G05MarkerSurvivabilityHelpers.psm1'
Import-Module $helperPath -Force
Import-Module $audioTimingHelperPath -Force

function Write-G05AtomicJson([string] $Path, [object] $Value) {
    $partial = "$Path.partial"
    try { [IO.File]::WriteAllText($partial, ($Value | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false)); Move-Item -LiteralPath $partial -Destination $Path -Force }
    finally { if (Test-Path -LiteralPath $partial -PathType Leaf) { Remove-Item -LiteralPath $partial -Force } }
}

function Get-G05Property([object] $Value, [string] $Name, $Default = $null) {
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { $Default } else { $property.Value }
}

function Assert-G05ExactSibling([string] $Path, [string] $Name) {
    $full = Assert-G05SmokeRoot $Path $projectParent $Name
    $expected = Join-Path $projectParent $Name
    if (-not $full.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) { throw "$Name must be the exact approved repository sibling." }
    $full
}

function Assert-G05NoActiveMedia {
    $active = @(Get-Process -Name ffmpeg,ffprobe -ErrorAction SilentlyContinue)
    if ($active.Count) { throw "Active ffmpeg or ffprobe process blocks smoke evidence: $($active.Id -join ',')." }
}

function Get-G05Candidates([object] $Contract) {
    $workload = @($Contract.workloads | Where-Object id -eq 'typical-2v4a')
    $variant = @($workload[0].resolutionVariants | Where-Object id -eq '1080p')
    if ($workload.Count -ne 1 -or $variant.Count -ne 1 -or $workload[0].evidenceBoundary -ne 'runtime-route' -or $variant[0].width -ne 1920 -or $variant[0].height -ne 1080) { throw 'The exact typical-2v4a 1080p runtime-route workload is unavailable.' }
    $keys = @('mp4-openh264-aac|one','webm-vp9-opus|one','webm-vp9-opus|half-logical')
    $rows = foreach ($key in $keys) {
        $parts = $key.Split('|'); $route = @($Contract.routes | Where-Object id -eq $parts[0]); $policy = @($Contract.threadPolicies | Where-Object id -eq $parts[1])
        if ($route.Count -ne 1 -or $policy.Count -ne 1 -or $parts[1] -notin @($route[0].threadPolicies)) { throw "Missing exact smoke candidate: $key" }
        $threads = if ($null -ne $policy[0].PSObject.Properties['resolvedValue']) { [int]$policy[0].resolvedValue } elseif ($policy[0].resolvedValueExpression -eq 'ceil(observedLogicalProcessors/2)') { [int][Math]::Ceiling([Environment]::ProcessorCount / 2) } else { throw "Unknown thread policy: $($policy[0].id)" }
        [pscustomobject]@{ candidateId=$key; route=$route[0]; policy=$policy[0]; threads=$threads; workload=$workload[0]; variant=$variant[0] }
    }
    if (@($rows).Count -ne 3) { throw 'The smoke did not expand to exactly three candidates.' }
    @($rows)
}

function New-G05EncodeTokens([object] $Row, [object] $Contract, [string] $FixtureDirectory, [string] $Output) {
    $tokens = [Collections.Generic.List[string]]::new()
    foreach ($token in @('-hide_banner','-nostdin','-progress','pipe:1','-stats_period','0.5')) { $tokens.Add($token) }
    $artifactDirectory = [IO.Path]::GetDirectoryName($FixtureDirectory)
    foreach ($input in @($Row.workload.inputs | Sort-Object inputIndex)) {
        $profile = @($Contract.inputProfiles | Where-Object id -eq $input.profile)
        if ($profile.Count -ne 1) { throw "Missing frozen input profile: $($input.profile)" }
        $audio = ([string]$profile[0].stream) -match ':a:'
        foreach ($raw in @($profile[0].tokens)) {
            $value = ([string]$raw).Replace('{artifactRoot}', $artifactDirectory)
            if ($value -eq '-i') { $tokens.Add($(if($audio){'-threads:a'}else{'-threads:v'})); $tokens.Add([string]$Row.threads) }
            $tokens.Add($value)
        }
    }
    foreach ($token in @('-filter_threads',[string]$Row.threads,'-filter_complex_threads',[string]$Row.threads,'-filter_complex',(Get-G05SmokeCombinedGraph $Row.workload $Row.variant))) { $tokens.Add($token) }
    foreach ($map in @($Row.route.maps)) { $tokens.Add('-map'); $tokens.Add([string]$map) }
    foreach ($token in @('-c:v',[string]$Row.route.videoEncoder,'-threads:v:0',[string]$Row.threads) + @($Row.route.videoOptions) + @('-c:a',[string]$Row.route.audioEncoder,'-threads:a:0',[string]$Row.threads) + @($Row.route.audioOptions) + @($Row.route.muxerOptions) + @($Row.route.outputDurationTokens | ForEach-Object { ([string]$_).Replace('{durationSeconds}','30') }) + @('-f',[string]$Row.route.muxer,'-y',$Output)) { $tokens.Add([string]$token) }
    $tokens.ToArray()
}

function Invoke-G05CapturedCommand([string] $Executable, [string[]] $Tokens, [string] $StdoutPath, [string] $StderrPath, [hashtable] $Roots) {
    & $Executable @Tokens 1> $StdoutPath 2> $StderrPath
    foreach ($path in @($StdoutPath, $StderrPath)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $sanitized = Sanitize-G05Text ([IO.File]::ReadAllText($path)) $Roots
            [IO.File]::WriteAllText($path, $sanitized, [Text.UTF8Encoding]::new($false))
        }
    }
    [ordered]@{ exitCode=$LASTEXITCODE; stdout=[ordered]@{name=[IO.Path]::GetFileName($StdoutPath);size=(Get-Item -LiteralPath $StdoutPath).Length;sha256=(Get-G05SmokeHash $StdoutPath)}; stderr=[ordered]@{name=[IO.Path]::GetFileName($StderrPath);size=(Get-Item -LiteralPath $StderrPath).Length;sha256=(Get-G05SmokeHash $StderrPath)} }
}

function Get-G05VideoTiming([object] $VideoStream, [object[]] $Frames) {
    if ($Frames.Count -ne 750) { throw "Expected 750 video frames, observed $($Frames.Count)." }
    $timeBase = [string]$VideoStream.time_base; $ticks = [Collections.Generic.List[int64]]::new()
    for ($index=0; $index -lt $Frames.Count; $index++) {
        $raw = Get-G05Property $Frames[$index] 'best_effort_timestamp' (Get-G05Property $Frames[$index] 'pts')
        if ($null -eq $raw) { throw "Video frame $index lacks an integer presentation timestamp." }
        $tick = Convert-G05SmokeTicks ([int64]$raw) $timeBase
        if ($tick -ne [int64]$index*40) { throw "Video frame $index normalized to $tick instead of $([int64]$index*40)." }
        $ticks.Add($tick)
    }
    $last = $Frames[-1]; $durationRaw = Get-G05Property $last 'pkt_duration' (Get-G05Property $last 'duration')
    if ($null -ne $durationRaw) { $end = $ticks[-1] + (Convert-G05SmokeTicks ([int64]$durationRaw) $timeBase); $source='final-frame-duration' }
    elseif ($null -ne (Get-G05Property $VideoStream 'duration_ts')) { $end = Convert-G05SmokeTicks ([int64]$VideoStream.duration_ts) $timeBase; $source='stream-duration-ts' }
    else { throw 'Video presentation-end evidence is unavailable.' }
    if ($end -ne 30000) { throw "Video presentation end normalized to $end instead of 30000." }
    [ordered]@{frameCount=750;comparisonTimeBase='1/1000';firstTick=$ticks[0];finalTick=$ticks[-1];presentationEndTick=$end;presentationEndSource=$source;allFrameTicksExact=$true}
}

function Get-G05Descriptor([object] $Contract, [object] $Route) {
    $profile = @($Contract.markerQualification.requiredRouteQualityProfiles | Where-Object routeId -eq $Route.id)
    if ($profile.Count -ne 1 -or $profile[0].qualityProfileId -ne $Route.qualityProfileId) { throw "Missing approved descriptor for $($Route.id)." }
    $profile[0].observedDescriptor
}

function Test-G05Descriptor([object] $Probe, [object] $Route, [object] $Expected) {
    $video=@($Probe.streams|Where-Object codec_type -eq 'video');$audio=@($Probe.streams|Where-Object codec_type -eq 'audio')
    if ($video.Count-ne1 -or $audio.Count-ne1) { throw 'Output must contain exactly one video and one audio stream.' }
    $observed=[ordered]@{formatName=$Probe.format.format_name;videoIndex=$video[0].index;audioIndex=$audio[0].index;videoCodec=$video[0].codec_name;videoProfile=$video[0].profile;pixelFormat=$video[0].pix_fmt;width=[int]$video[0].width;height=[int]$video[0].height;rFrameRate=$video[0].r_frame_rate;avgFrameRate=$video[0].avg_frame_rate;audioCodec=$audio[0].codec_name;audioProfile=(Get-G05Property $audio[0] 'profile');audioSampleRate=[int]$audio[0].sample_rate;audioChannels=[int]$audio[0].channels;audioChannelLayout=$audio[0].channel_layout}
    $criteria=[ordered]@{formatName=$observed.formatName-eq$Expected.formatName;videoCodec=$observed.videoCodec-eq$Expected.videoCodec;videoProfile=$observed.videoProfile-eq$Expected.videoProfile;pixelFormat=$observed.pixelFormat-eq$Expected.pixelFormat;width=$observed.width-eq$Expected.width;height=$observed.height-eq$Expected.height;rFrameRate=$observed.rFrameRate-eq$Expected.frameRate;avgFrameRate=$observed.avgFrameRate-eq$Expected.frameRate;audioCodec=$observed.audioCodec-eq$Expected.audioCodec;audioProfile=if($null-eq$Expected.audioProfile){$true}else{$observed.audioProfile-eq$Expected.audioProfile};audioSampleRate=$observed.audioSampleRate-eq$Expected.audioSampleRate;audioChannels=$observed.audioChannels-eq$Expected.audioChannels;audioChannelLayout=$observed.audioChannelLayout-eq$Expected.audioChannelLayout}
    if (@($criteria.Values|Where-Object{$_-eq$false}).Count) { throw "Frozen output descriptor mismatch: $((@($criteria.GetEnumerator()|Where-Object{-not$_.Value}|ForEach-Object Key))-join ',')." }
    [ordered]@{expected=$Expected;observed=$observed;criteria=$criteria;passed=$true;videoStream=$video[0];audioStream=$audio[0]}
}

function Get-G05FileEvidence([string] $Path, [string] $Stage) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $item=Get-Item -LiteralPath $Path;[ordered]@{path=([IO.Path]::GetRelativePath($Stage,$Path).Replace('\','/'));size=$item.Length;sha256=(Get-G05SmokeHash $Path)}
}

function Remove-G05PartialOutput([string] $Path, [string] $Stage) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return [ordered]@{disposition='not-created'} }
    $before=Get-G05FileEvidence $Path $Stage
    try { Remove-Item -LiteralPath $Path -Force;[ordered]@{disposition='removed-unvalidated-partial';preDelete=$before;absentAfterCleanup=-not(Test-Path -LiteralPath $Path)} }
    catch { [ordered]@{disposition='cleanup-failed';preDelete=$before;error=$_.Exception.GetType().Name;absentAfterCleanup=$false} }
}

function Sanitize-G05Text([string] $Value, [hashtable] $Roots) {
    $result=$Value
    foreach($entry in $Roots.GetEnumerator()){$result=$result.Replace([string]$entry.Value,"{$($entry.Key)}",[StringComparison]::OrdinalIgnoreCase)}
    $result
}

$state=[ordered]@{schemaVersion=1;schemaId='Gate0.G05.Stage2.PreMatrixSmoke.V1';status='started';startedUtc=[DateTimeOffset]::UtcNow;completedUtc=$null;contractOnly=[bool]$ContractOnly;manualExecution=[bool]$ManualExecution;appendRetention=[bool]$AppendRetention;evidenceBoundary='runtime-route';noMediaExecuted=$true;preflight=[ordered]@{};contracts=[ordered]@{};candidates=@();attempts=@();retention=$null;error=$null;nonClaims=@('No current ReelForge product render, WPF, preview, cache, cancellation, or project behavior claim.','No shipping-runtime, distribution, patent, legal, public hardware-floor, full-matrix, or long-form conclusion.')}
$stage=$null;$artifact=$null;$retentionAvailable=$false;$exitCode=1
try {
    if ((Get-G05SmokeHash $contractPath) -ne 'CBB93CC1483FECD65489485CB1BBF03CD3BF24C2419D28C587C62758C3EAD7EC') { throw 'Frozen workload contract SHA-256 mismatch.' }
    $contract=Get-Content -LiteralPath $contractPath -Raw|ConvertFrom-Json -Depth 100;$rows=Get-G05Candidates $contract
    $state.contracts.workload=[ordered]@{path='eng/gate0/g0.5-stage2-workload-contract.json';sha256=(Get-G05SmokeHash $contractPath);contractId=$contract.contractId}
    $state.candidates=@($rows|ForEach-Object{[ordered]@{candidateId=$_.candidateId;routeId=$_.route.id;threadPolicyId=$_.policy.id;resolvedThreads=$_.threads}})
    if($ContractOnly){$state.status='contract-only';$state.completedUtc=[DateTimeOffset]::UtcNow;exit 0}
    if(-not$ManualExecution-or-not$AppendRetention){throw'Live smoke requires explicit -ManualExecution and -AppendRetention.'}
    $artifact=Assert-G05ExactSibling $ArtifactRoot 'ReelForge.Gate0Artifacts';$stagingBase=Assert-G05ExactSibling (Join-Path $projectParent 'ReelForge.Gate0Staging') 'ReelForge.Gate0Staging'
    $fixture=[IO.Path]::GetFullPath($FixtureRoot).TrimEnd([IO.Path]::DirectorySeparatorChar);if(-not$fixture.Equals((Join-Path $artifact 'fixtures'),[StringComparison]::OrdinalIgnoreCase)){throw'FixtureRoot must equal the retained artifact fixtures directory.'};[void](Assert-G05SmokeRoot $fixture $artifact 'FixtureRoot')
    $runtime=[IO.Path]::GetFullPath($RuntimeRoot).TrimEnd([IO.Path]::DirectorySeparatorChar);$expectedRuntime=Join-Path $artifact 'p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1';if(-not$runtime.Equals($expectedRuntime,[StringComparison]::OrdinalIgnoreCase)){throw'RuntimeRoot must equal the exact retained P2 runtime.'};[void](Assert-G05SmokeRoot $runtime $artifact 'RuntimeRoot')
    $stage=Join-Path $stagingBase ("g05-stage2-smoke-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))-$([Guid]::NewGuid().ToString('N').Substring(0,8))");[IO.Directory]::CreateDirectory($stage)|Out-Null;[void](Assert-G05SmokeRoot $stage $stagingBase 'Smoke stage');$retentionAvailable=$true
    $snapshots=Join-Path $stage 'snapshots';[IO.Directory]::CreateDirectory($snapshots)|Out-Null
    $r2SummaryPath=Join-Path $PSScriptRoot 'g0.5-r2-retention-result-summary.json';$resourceSummaryPath=Join-Path $PSScriptRoot 'g0.5-stage2-smoke-preflight-result-summary.json';$p2ManifestPath=Join-Path $PSScriptRoot 'manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json';$trackedRetentionManifest=Join-Path $PSScriptRoot 'artifact-retention-manifest.json';$localRetentionManifest=Join-Path $artifact 'artifact-retention-manifest.json'
    $snapshotSources=@{'workload-contract.json'=$contractPath;'audio-oracle-contract.json'=$audioContractPath;'smoke-helper.psm1'=$helperPath;'audio-timing-helper.psm1'=$audioTimingHelperPath;'smoke-runner.ps1'=$PSCommandPath;'retention-appender.ps1'=$retentionAppender;'retention-validator.ps1'=$retentionValidator;'runtime-validator.ps1'=$runtimeValidator;'r2-authorization-summary.json'=$r2SummaryPath;'resource-authorization-summary.json'=$resourceSummaryPath;'p2-runtime-manifest.json'=$p2ManifestPath;'tracked-retention-manifest.json'=$trackedRetentionManifest;'local-retention-manifest.json'=$localRetentionManifest}
    $state.contracts.snapshot=[ordered]@{createdBeforeMedia=$true;bindings=(New-G05SmokeSnapshotBinding $snapshots $snapshotSources)};Assert-G05SmokeSnapshotBinding $state.contracts.snapshot.bindings $snapshotSources
    $snapshotWorkload=Join-Path $snapshots 'workload-contract.json';$snapshotAudioContract=Join-Path $snapshots 'audio-oracle-contract.json';$snapshotHelper=Join-Path $snapshots 'smoke-helper.psm1';$snapshotAudioTimingHelper=Join-Path $snapshots 'audio-timing-helper.psm1';$snapshotR2Summary=Join-Path $snapshots 'r2-authorization-summary.json';$snapshotResourceSummary=Join-Path $snapshots 'resource-authorization-summary.json';$snapshotP2Manifest=Join-Path $snapshots 'p2-runtime-manifest.json'
    Import-Module $snapshotHelper -Force;Import-Module $snapshotAudioTimingHelper -Force
    $contract=Get-Content -LiteralPath $snapshotWorkload -Raw|ConvertFrom-Json -Depth 100;$rows=Get-G05Candidates $contract;$state.candidates=@($rows|ForEach-Object{[ordered]@{candidateId=$_.candidateId;routeId=$_.route.id;threadPolicyId=$_.policy.id;resolvedThreads=$_.threads}});$state.contracts.workload=[ordered]@{path='snapshots/workload-contract.json';sha256=(Get-G05SmokeHash $snapshotWorkload);contractId=$contract.contractId}
    $r2=Get-Content $snapshotR2Summary -Raw|ConvertFrom-Json -Depth 32;$resource=Get-Content $snapshotResourceSummary -Raw|ConvertFrom-Json -Depth 32
    if($r2.status-ne'complete'-or-not$r2.durableManifest.secondPrivateCopyVerified-or-not$r2.gates.completeR2ByteVerificationComplete-or-not$r2.gates.preMatrixSmokeAuthorized){throw'Complete R2 evidence does not authorize the smoke.'};if($resource.status-ne'passed-r2-verification-complete-smoke-authorized'-or-not$resource.disposition.resourcePreflightComplete-or-not$resource.disposition.preMatrixSmokeAuthorized){throw'Resource evidence does not authorize the smoke.'}
    if((Get-G05SmokeHash $retentionValidator)-ne(@($state.contracts.snapshot.bindings|Where-Object name -eq'retention-validator.ps1')[0].sha256)){throw'Retention validator changed after snapshot binding.'};$local=&$retentionValidator -ArtifactRoot $artifact;if((Get-G05SmokeHash $retentionValidator)-ne(@($state.contracts.snapshot.bindings|Where-Object name -eq'retention-validator.ps1')[0].sha256)){throw'Retention validator changed during corpus validation.'};if($local.manifestSha256-ne$r2.sourceInventory.manifestSha256-or$local.fileCount-ne$r2.sourceInventory.logicalArtifactCount-or$local.totalBytes-ne$r2.sourceInventory.logicalArtifactBytes-or$local.manifestSha256-ne$resource.retention.sourceManifestSha256){throw'Current local corpus no longer matches the completed R2/resource authorization binding.'};$state.preflight=[ordered]@{localCorpus=$local;r2SummarySha256=(Get-G05SmokeHash $snapshotR2Summary);resourceSummarySha256=(Get-G05SmokeHash $snapshotResourceSummary);authorization='bounded-pre-matrix-smoke-only'}
    Assert-G05NoActiveMedia
    $runtimeValidatorHash=@($state.contracts.snapshot.bindings|Where-Object name -eq'runtime-validator.ps1')[0].sha256;if((Get-G05SmokeHash $runtimeValidator)-ne$runtimeValidatorHash){throw'Runtime validator changed after snapshot binding.'};$runtimeRaw=Join-Path $stage 'runtime-identity.raw.json';&$runtimeValidator -RuntimeRoot $runtime -ManifestPath $snapshotP2Manifest -EvidencePath $runtimeRaw|Out-Null;if((Get-G05SmokeHash $runtimeValidator)-ne$runtimeValidatorHash){throw'Runtime validator changed during runtime validation.'};$runtimeText=Get-Content $runtimeRaw -Raw;$runtimeText=Sanitize-G05Text $runtimeText @{artifactRoot=$artifact;runtimeRoot=$runtime;repositoryRoot=$repositoryRoot;stage=$stage};[IO.File]::WriteAllText((Join-Path $stage 'runtime-identity.json'),$runtimeText,[Text.UTF8Encoding]::new($false));Remove-Item $runtimeRaw -Force
    if((Get-G05SmokeHash $snapshotAudioContract)-ne'119A4C179BFA010F3202DBF6AA368E42EDE5FD0FC23EF2781AA9C7F63540CBE4'){throw'Frozen audio oracle contract SHA-256 mismatch.'};$audioContract=Get-Content $snapshotAudioContract -Raw|ConvertFrom-Json -Depth 100;$descriptor=@($audioContract.referenceDescriptors|Where-Object id -eq'typical-2v4a-30s');if($descriptor.Count-ne1){throw'Frozen typical audio descriptor is unavailable.'};$state.contracts.audioOracle=[ordered]@{path='snapshots/audio-oracle-contract.json';sha256=(Get-G05SmokeHash $snapshotAudioContract);contractId=$audioContract.contractId};$state.contracts.harness=[ordered]@{path='snapshots/smoke-runner.ps1';sha256=(Get-G05SmokeHash (Join-Path $snapshots 'smoke-runner.ps1'))};$state.contracts.helper=[ordered]@{path='snapshots/smoke-helper.psm1';sha256=(Get-G05SmokeHash $snapshotHelper)};$state.contracts.audioTimingHelper=[ordered]@{path='snapshots/audio-timing-helper.psm1';sha256=(Get-G05SmokeHash $snapshotAudioTimingHelper)}
    $truth=New-G05TypicalAudioTruth $fixture $rows[0].workload (Join-Path $stage 'typical-2v4a-30s.s16le');$ffmpeg=Join-Path $runtime 'bin/ffmpeg.exe';$ffprobe=Join-Path $runtime 'bin/ffprobe.exe';$blockedRoutes=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $portableRoots=@{artifactRoot=$artifact;runtimeRoot=$runtime;fixtureRoot=$fixture;stage=$stage}
    foreach($row in $rows){
        $demuxer=Get-G05SmokeDemuxer $row.route.muxer;$record=[ordered]@{candidateId=$row.candidateId;routeId=$row.route.id;threadPolicyId=$row.policy.id;status='started';startedUtc=[DateTimeOffset]::UtcNow;completedUtc=$null;selectedComponents=[ordered]@{inputProfiles=@($row.workload.inputs.profile);filters=@('scale','format','setpts','crop','overlay','aformat','volume','pan','adelay','amix','atrim','asetpts');videoEncoder=$row.route.videoEncoder;audioEncoder=$row.route.audioEncoder;muxer=$row.route.muxer;demuxer=$demuxer;videoDecoder=$row.route.outputDecoders[0];audioDecoder=$row.route.outputDecoders[1];threads=$row.threads};commands=[ordered]@{};measurement=$null;descriptor=$null;videoTiming=$null;visual=$null;audio=$null;media=$null;cleanup=$null;failures=@()};$state.attempts+=,$record
        if($blockedRoutes.Contains([string]$row.route.id)){$record.status='blocked';$record.failures+=,'route-fail-fast-blocked-after-prior-deterministic-failure';$record.completedUtc=[DateTimeOffset]::UtcNow;continue}
        $output=Join-Path $stage "$($row.route.id)-$($row.policy.id).$($row.route.container)";$encodeResult=$null
        try{
            $encodeTokens=New-G05EncodeTokens $row $contract $fixture $output;$record.commands.encode=[ordered]@{executable='{runtimeRoot}/bin/ffmpeg.exe';tokens=(ConvertTo-G05SmokePortableTokens $encodeTokens $portableRoots)};$state.noMediaExecuted=$false
            $progress=Join-Path $stage "$($row.route.id)-$($row.policy.id).progress.txt";$encodeStderr=Join-Path $stage "$($row.route.id)-$($row.policy.id).encode.stderr.txt";$encodeResult=Invoke-G05SmokeObservedProcess $ffmpeg $encodeTokens $stage $progress $encodeStderr
            foreach($logPath in @($progress,$encodeStderr)){if(Test-Path -LiteralPath $logPath -PathType Leaf){$sanitized=Sanitize-G05Text ([IO.File]::ReadAllText($logPath)) $portableRoots;[IO.File]::WriteAllText($logPath,$sanitized,[Text.UTF8Encoding]::new($false))}}
            $record.measurement=$encodeResult
            if($encodeResult.exitCode-ne0-or-not$encodeResult.processTree.orphanFree-or-not(Test-Path $output -PathType Leaf)){throw'Observed encode did not close successfully and orphan-free.'}
            $samplesPath=Join-Path $stage "$($row.route.id)-$($row.policy.id).process-samples.ndjson";[IO.File]::WriteAllLines($samplesPath,@($encodeResult.samples|ForEach-Object{$_|ConvertTo-Json -Compress}),[Text.UTF8Encoding]::new($false));$record.measurement.samples=[IO.Path]::GetFileName($samplesPath)
            $probeFrames=Join-Path $stage "$($row.route.id)-$($row.policy.id).probe-frames.json";$probeFramesErr=Join-Path $stage "$($row.route.id)-$($row.policy.id).probe-frames.stderr.txt";$probeTokens=@('-v','error','-f',$demuxer,'-show_streams','-show_frames','-show_format','-of','json',$output);$record.commands.probeFrames=[ordered]@{executable='{runtimeRoot}/bin/ffprobe.exe';tokens=(ConvertTo-G05SmokePortableTokens $probeTokens $portableRoots)};$probeRecord=Invoke-G05CapturedCommand $ffprobe $probeTokens $probeFrames $probeFramesErr $portableRoots;if($probeRecord.exitCode-ne0){throw'Forced-demuxer frame probe failed.'};$probe=Get-Content $probeFrames -Raw|ConvertFrom-Json -Depth 100
            $probePackets=Join-Path $stage "$($row.route.id)-$($row.policy.id).probe-packets.json";$probePacketsErr=Join-Path $stage "$($row.route.id)-$($row.policy.id).probe-packets.stderr.txt";$packetTokens=@('-v','error','-f',$demuxer,'-show_packets','-of','json',$output);$record.commands.probePackets=[ordered]@{executable='{runtimeRoot}/bin/ffprobe.exe';tokens=(ConvertTo-G05SmokePortableTokens $packetTokens $portableRoots)};$packetRecord=Invoke-G05CapturedCommand $ffprobe $packetTokens $probePackets $probePacketsErr $portableRoots;if($packetRecord.exitCode-ne0){throw'Forced-demuxer packet probe failed.'};$packets=Get-Content $probePackets -Raw|ConvertFrom-Json -Depth 100
            $record.descriptor=Test-G05Descriptor $probe $row.route (Get-G05Descriptor $contract $row.route);$videoFrames=@($probe.frames|Where-Object media_type -eq'video');$record.videoTiming=Get-G05VideoTiming $record.descriptor.videoStream $videoFrames
            $visualMetrics=Join-Path $stage "$($row.route.id)-$($row.policy.id).visual-mae.ndjson";$visualLog=Join-Path $stage "$($row.route.id)-$($row.policy.id).visual.stderr.txt";$visualTokens=@('-v','error','-xerror','-err_detect','explode','-f',$demuxer,'-c:v',$row.route.outputDecoders[0],'-i',$output,'-map','0:v:0','-an','-fps_mode','passthrough','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo','pipe:1');$record.commands.visualDecode=[ordered]@{executable='{runtimeRoot}/bin/ffmpeg.exe';tokens=(ConvertTo-G05SmokePortableTokens $visualTokens $portableRoots)};$record.visual=Test-G05SmokeVisual $ffmpeg $demuxer $row.route.outputDecoders[0] $output $fixture $visualLog $visualMetrics;if(Test-Path -LiteralPath $visualLog -PathType Leaf){$sanitized=Sanitize-G05Text ([IO.File]::ReadAllText($visualLog)) $portableRoots;[IO.File]::WriteAllText($visualLog,$sanitized,[Text.UTF8Encoding]::new($false))};if(-not$record.visual.passed){throw'At least one decoded frame exceeded the MAE threshold.'}
            $pcm=Join-Path $stage "$($row.route.id)-$($row.policy.id).audio.s16le";$audioStdout=Join-Path $stage "$($row.route.id)-$($row.policy.id).audio-decode.stdout.txt";$audioStderr=Join-Path $stage "$($row.route.id)-$($row.policy.id).audio-decode.stderr.txt";$audioTokens=@('-v','error','-xerror','-err_detect','explode','-f',$demuxer,'-c:a',$row.route.outputDecoders[1],'-threads:a',[string]$row.threads,'-i',$output,'-map','0:a:0','-vn','-c:a','pcm_s16le','-f','s16le','-y',$pcm);$record.commands.audioDecode=[ordered]@{executable='{runtimeRoot}/bin/ffmpeg.exe';tokens=(ConvertTo-G05SmokePortableTokens $audioTokens $portableRoots)};$audioDecode=Invoke-G05CapturedCommand $ffmpeg $audioTokens $audioStdout $audioStderr $portableRoots;if($audioDecode.exitCode-ne0){throw'Strict native audio decode failed.'}
            $audioFrames=@($probe.frames|Where-Object media_type -eq'audio');$audioPackets=@($packets.packets|Where-Object codec_type -eq'audio');$timing=Get-G05DecodedAudioTiming -RawByteLength (Get-Item $pcm).Length -AudioStream $record.descriptor.audioStream -AudioFrames $audioFrames -AudioPackets $audioPackets -ExpectedSamplesPerChannel 1440000 -MaximumRawTailSamples 1024;if(-not$timing.passed){throw("Audio timing failed: "+($timing.failures-join','))};$quality=Test-G05SmokeAudio $truth $pcm $audioContract.qualityThresholds $descriptor[0] 1024;if(-not$quality.passed){throw("Audio quality failed: "+($quality.failures-join','))};$record.audio=[ordered]@{timing=$timing;quality=$quality;rawPcm=(Get-G05FileEvidence $pcm $stage)}
            $record.media=Get-G05FileEvidence $output $stage;$record.cleanup=[ordered]@{encodeRootExited=$encodeResult.processTree.rootExited;observedDescendants=$encodeResult.processTree.observedChildPids;activeObservedDescendantsAtClose=$encodeResult.processTree.activeObservedChildrenAtClose;orphanFree=$encodeResult.processTree.orphanFree;partialOutput='not-applicable-complete-output-retained'};$record.status='passed'
        }catch{
            $record.failures+=,(Sanitize-G05Text $_.Exception.Message $portableRoots);if($null-ne$encodeResult-and$encodeResult.exitCode-eq0-and(Test-Path $output -PathType Leaf)){$record.media=Get-G05FileEvidence $output $stage;$record.cleanup=[ordered]@{partialOutput='not-partial-complete-output-retained-for-failed-oracle';orphanFree=$encodeResult.processTree.orphanFree}}else{$record.cleanup=Remove-G05PartialOutput $output $stage};$record.status='failed';[void]$blockedRoutes.Add([string]$row.route.id)
        }finally{try{Assert-G05NoActiveMedia}catch{$record.failures+=,(Sanitize-G05Text $_.Exception.Message $portableRoots);$record.status='failed';[void]$blockedRoutes.Add([string]$row.route.id)};$record.completedUtc=[DateTimeOffset]::UtcNow}
    }
    $state.status=if(@($state.attempts|Where-Object status -eq'failed').Count){'completed-with-failures'}elseif(@($state.attempts|Where-Object status -eq'passed').Count-eq3){'completed'}else{'infrastructure-failed'}
}catch{$state.status='infrastructure-failed';$state.error=if($null-ne$artifact){Sanitize-G05Text $_.Exception.Message @{artifactRoot=$artifact;repositoryRoot=$repositoryRoot;stage=$stage}}else{$_.Exception.Message}}
finally{
    $state.completedUtc=[DateTimeOffset]::UtcNow
    if($retentionAvailable-and$null-ne$stage-and(Test-Path $stage -PathType Container)){
        $group="Gate0.G05.Stage2PreMatrixSmoke.$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')).$([Guid]::NewGuid().ToString('N').Substring(0,8).ToUpperInvariant())";$destination="proofs/$([IO.Path]::GetFileName($stage))";$state.retention=[ordered]@{status='append-requested';groupId=$group;destinationName=$destination;ceilingBytes=805306368;candidateBytes=0}
        $evidence=Join-Path $stage 'g0.5-stage2-pre-matrix-smoke-evidence.json';Write-G05AtomicJson $evidence $state;$bytes=[int64](@(Get-ChildItem $stage -File -Recurse|ForEach-Object Length|Measure-Object -Sum).Sum);$state.retention.candidateBytes=$bytes;Write-G05AtomicJson $evidence $state;$finalBytes=[int64](@(Get-ChildItem $stage -File -Recurse|ForEach-Object Length|Measure-Object -Sum).Sum);$state.retention.candidateBytes=$finalBytes;Write-G05AtomicJson $evidence $state;$bytes=$finalBytes
        if($bytes-gt805306368){$state.status='retention-failed';$state.retention.status='ceiling-exceeded';Write-G05AtomicJson $evidence $state}
        else{try{$appenderHash=@($state.contracts.snapshot.bindings|Where-Object name -eq'retention-appender.ps1')[0].sha256;$validatorHash=@($state.contracts.snapshot.bindings|Where-Object name -eq'retention-validator.ps1')[0].sha256;if((Get-G05SmokeHash $retentionAppender)-ne$appenderHash-or(Get-G05SmokeHash $retentionValidator)-ne$validatorHash){throw'Retention tooling changed after snapshot binding.'};&$retentionAppender -ArtifactRoot $artifact -SourceRoot $stage -SourceTrustBoundary $projectParent -GroupId $group -DestinationName $destination -Provenance 'Manual Gate 0 G0.5 pre-matrix smoke evidence; pass, failed, and blocked rows retained.' -ProducerRuntimeIdentity @("artifact:$destination/snapshots/smoke-runner.ps1","artifact:$destination/snapshots/smoke-helper.psm1","artifact:$destination/snapshots/p2-runtime-manifest.json") -LicenseRecords @('artifact:p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1/LICENSE.txt') -ProofRunIdentity @("artifact:$destination/g0.5-stage2-pre-matrix-smoke-evidence.json");if((Get-G05SmokeHash $retentionAppender)-ne$appenderHash-or(Get-G05SmokeHash $retentionValidator)-ne$validatorHash){throw'Retention tooling changed during append.'};&$retentionValidator -ArtifactRoot $artifact|Out-Null;$state.retention.status='appended-and-validated';$exitCode=if($state.status-eq'completed'){0}else{1}}catch{$state.status='retention-failed';$state.retention.status='append-or-validation-failed';$state.retention.error=$_.Exception.GetType().Name;Write-G05AtomicJson $evidence $state}}
    }
    $state|ConvertTo-Json -Depth 100
    if($ContractOnly){$exitCode=0}
}
exit $exitCode
