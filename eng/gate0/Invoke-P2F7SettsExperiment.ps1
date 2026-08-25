[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$contractPath = Join-Path $PSScriptRoot 'f7-setts-experiment-contract.json'
$p2ManifestPath = Join-Path $PSScriptRoot 'manifests\p2-btbn-lgplv3-shared-windows-x64-20260820.json'
$retentionValidatorPath = Join-Path $PSScriptRoot 'Test-Gate0ArtifactRetention.ps1'
$p2ValidatorPath = Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$approvedArtifactRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($repositoryRoot)) 'ReelForge.Gate0Artifacts')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $resolvedArtifactRoot.Equals($approvedArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactRoot must be the approved repository sibling: $approvedArtifactRoot"
}

if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be an explicit rooted path.' }
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$outputParentPath = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($outputParentPath) -or -not (Test-Path -LiteralPath $outputParentPath -PathType Container)) {
    throw 'The immediate OutputDirectory parent must already exist so its physical path can be validated.'
}
if ($resolvedOutput.StartsWith("$repositoryRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedOutput.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Experiment output must remain outside the repository.'
}
if ($resolvedOutput.StartsWith("$resolvedArtifactRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedOutput.Equals($resolvedArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Experiment output must remain outside the retained corpus until it is separately manifested.'
}
if (Test-Path -LiteralPath $resolvedOutput) { throw 'Experiment output must be a new directory.' }
$outputAncestor = Get-Item -LiteralPath $outputParentPath -Force
while ($null -ne $outputAncestor) {
    if (($outputAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "OutputDirectory must not traverse a reparse-point ancestor: $($outputAncestor.FullName)"
    }
    $outputAncestor = $outputAncestor.Parent
}

[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$outputItem = Get-Item -LiteralPath $resolvedOutput -Force
if (($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
    -not $outputItem.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Created OutputDirectory did not remain a direct, non-reparse directory at the validated path.'
}
$mediaDirectory = Join-Path $resolvedOutput 'media'
$logsDirectory = Join-Path $resolvedOutput 'logs'
$workDirectory = Join-Path $resolvedOutput 'work'
[IO.Directory]::CreateDirectory($mediaDirectory) | Out-Null
[IO.Directory]::CreateDirectory($logsDirectory) | Out-Null
[IO.Directory]::CreateDirectory($workDirectory) | Out-Null

$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json -Depth 30
$p2Manifest = Get-Content -LiteralPath $p2ManifestPath -Raw | ConvertFrom-Json -Depth 30
$commands = [Collections.Generic.List[object]]::new()
$caseResults = [Collections.Generic.List[object]]::new()
$preflight = [ordered]@{ status = 'started' }
$runStatus = 'started'
$runReason = $null

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-Value($Object, [string] $Name, $Default = $null) {
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Convert-ToForwardSlash([string] $Path) {
    return $Path.Replace([IO.Path]::DirectorySeparatorChar, '/').Replace([IO.Path]::AltDirectorySeparatorChar, '/')
}

function Get-OutputFileEvidence([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    $relative = [IO.Path]::GetRelativePath($resolvedOutput, $item.FullName)
    if ([IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..')) { throw "Output artifact escaped the experiment root: $Path" }
    return [ordered]@{ path = Convert-ToForwardSlash $relative; length = [int64] $item.Length; sha256 = Get-Sha256 $item.FullName }
}

function Resolve-RetainedPath([string] $RelativePath, [string] $PathType = 'Leaf') {
    $nativeRelative = $RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::IsPathRooted($nativeRelative) -or $nativeRelative.StartsWith('..')) { throw "Unsafe retained artifact path: $RelativePath" }
    $path = [IO.Path]::GetFullPath((Join-Path $resolvedArtifactRoot $nativeRelative))
    if (-not $path.StartsWith("$resolvedArtifactRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType $PathType)) {
        throw "Retained artifact is missing or escaped its root: $RelativePath"
    }
    return $path
}

function Invoke-RecordedCommand(
    [string] $Name,
    [string] $Executable,
    [string[]] $Arguments,
    [object] $Components
) {
    if ($Name -notmatch '^[A-Za-z0-9_.-]+$') { throw "Unsafe command record name: $Name" }
    $stdoutPath = Join-Path $logsDirectory "$Name.stdout.txt"
    $stderrPath = Join-Path $logsDirectory "$Name.stderr.txt"
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void] $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = [DateTimeOffset]::UtcNow
    if (-not $process.Start()) { throw "Could not start command: $Name" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($stdoutPath, $stdout, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($stderrPath, $stderr, [Text.UTF8Encoding]::new($false))
    $record = [ordered]@{
        name = $Name
        executable = $Executable
        arguments = $Arguments
        components = $Components
        exitCode = $process.ExitCode
        elapsedMilliseconds = ([DateTimeOffset]::UtcNow - $started).TotalMilliseconds
        stdout = Get-OutputFileEvidence $stdoutPath
        stderr = Get-OutputFileEvidence $stderrPath
    }
    $commands.Add($record)
    return [ordered]@{ record = $record; stdout = $stdout; stderr = $stderr }
}

function Invoke-JsonProbe([string] $Name, [string] $Demuxer, [string] $Path, [string] $Purpose) {
    $result = Invoke-RecordedCommand $Name $ffprobe @(
        '-v', 'error', '-f', $Demuxer,
        '-show_format', '-show_streams', '-show_frames', '-show_packets',
        '-show_data_hash', 'sha256', '-of', 'json', $Path
    ) ([ordered]@{ purpose = $Purpose; demuxer = $Demuxer; allStreams = $true; allFrames = $true; allPackets = $true; packetDataHash = 'sha256' })
    if ($result.record.exitCode -ne 0) { throw "$Purpose failed with exit code $($result.record.exitCode)." }
    try { return [ordered]@{ record = $result.record; data = ($result.stdout | ConvertFrom-Json -Depth 30) } }
    catch { throw "$Purpose did not return valid JSON: $($_.Exception.Message)" }
}

function Get-Stream($Inspection, [string] $CodecType) {
    $matches = @($Inspection.streams | Where-Object codec_type -eq $CodecType)
    if ($matches.Count -ne 1) { throw "Expected exactly one $CodecType stream; observed $($matches.Count)." }
    return $matches[0]
}

function Get-StreamPackets($Inspection, [int] $StreamIndex) {
    return @($Inspection.packets | Where-Object { [int] $_.stream_index -eq $StreamIndex })
}

function Get-Scalar($Object, [string] $Name) {
    $value = Get-Value $Object $Name $null
    if ($null -eq $value) { return '<null>' }
    return [string] $value
}

function Compare-PacketStreams($SourceInspection, $TargetInspection, $SourceStream, $TargetStream, [bool] $IsVideo) {
    if ([string] $SourceStream.codec_name -ne [string] $TargetStream.codec_name) { throw 'Stream codec identity changed during the setts remux.' }
    if ([string] $SourceStream.time_base -ne [string] $TargetStream.time_base) { throw 'Stream time base changed during the setts remux.' }
    $sourcePackets = Get-StreamPackets $SourceInspection ([int] $SourceStream.index)
    $targetPackets = Get-StreamPackets $TargetInspection ([int] $TargetStream.index)
    if ($sourcePackets.Count -ne $targetPackets.Count) { throw 'Packet count changed during the setts remux.' }
    $sourceHashes = @($sourcePackets | ForEach-Object { [string] $_.data_hash })
    $targetHashes = @($targetPackets | ForEach-Object { [string] $_.data_hash })
    if ($sourceHashes -contains '' -or $sourceHashes -contains $null -or ($sourceHashes -join ',') -ne ($targetHashes -join ',')) {
        throw 'Ordered encoded packet payload hashes changed during the setts remux.'
    }
    if (@($sourceHashes | Select-Object -Unique).Count -ne $sourceHashes.Count) { throw 'Packet payload hashes are not unique enough to identify the terminal presentation packet robustly.' }

    $packetEvidence = [Collections.Generic.List[object]]::new()
    $terminalCount = 0
    for ($index = 0; $index -lt $sourcePackets.Count; $index++) {
        $sourcePacket = $sourcePackets[$index]
        $targetPacket = $targetPackets[$index]
        $sourcePts = Get-Scalar $sourcePacket 'pts'
        $targetPts = Get-Scalar $targetPacket 'pts'
        $sourceDts = Get-Scalar $sourcePacket 'dts'
        $targetDts = Get-Scalar $targetPacket 'dts'
        $sourceDuration = Get-Scalar $sourcePacket 'duration'
        $targetDuration = Get-Scalar $targetPacket 'duration'
        if ($sourcePts -ne $targetPts -or $sourceDts -ne $targetDts) { throw 'Packet PTS or DTS changed during the setts remux.' }
        $terminal = $IsVideo -and $sourcePts -eq '1200'
        if ($terminal) { $terminalCount++ }
        $expectedDuration = if ($terminal) { '800' } else { $sourceDuration }
        if ($targetDuration -ne $expectedDuration) { throw "Packet duration did not meet the bounded setts contract at payload $($sourceHashes[$index])." }
        $packetEvidence.Add([ordered]@{
            payloadSha256 = $sourceHashes[$index]
            sourcePts = $sourcePts
            targetPts = $targetPts
            sourceDts = $sourceDts
            targetDts = $targetDts
            sourceDuration = $sourceDuration
            targetDuration = $targetDuration
            terminalPresentationPacket = $terminal
        })
    }
    if ($IsVideo -and $terminalCount -ne 1) { throw "Expected one terminal presentation packet selected by PTS 1200; observed $terminalCount." }
    return [ordered]@{ codec = [string] $SourceStream.codec_name; timeBase = [string] $SourceStream.time_base; packetCount = $sourcePackets.Count; orderedPayloadsPreserved = $true; packets = @($packetEvidence) }
}

function Invoke-StrictDecode([string] $Name, $Case, [string] $InputPath, [string] $Map, [string] $OutputPath, [string[]] $OutputArguments) {
    $arguments = @(
        '-hide_banner', '-xerror', '-err_detect', 'explode',
        '-f', [string] $Case.demuxer,
        '-c:v', [string] $Case.videoDecoder,
        '-c:a', [string] $Case.audioDecoder,
        '-i', $InputPath,
        '-map', $Map, '-map_metadata', '-1'
    ) + $OutputArguments + @('-y', $OutputPath)
    $result = Invoke-RecordedCommand $Name $ffmpeg $arguments ([ordered]@{
        purpose = 'strict complete decode for before/after identity'
        demuxer = [string] $Case.demuxer
        explicitVideoDecoder = [string] $Case.videoDecoder
        explicitAudioDecoder = [string] $Case.audioDecoder
        streamMap = $Map
        strictErrors = @('-xerror', '-err_detect explode')
    })
    if ($result.record.exitCode -ne 0 -or -not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) { throw "Strict decode failed: $Name" }
    return [ordered]@{ command = $result.record; artifact = [ordered]@{ length = [int64] (Get-Item -LiteralPath $OutputPath).Length; sha256 = Get-Sha256 $OutputPath } }
}

function Get-FrameHashes([string] $Path, [int] $FrameSize, [int] $ExpectedFrameCount) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ne $FrameSize * $ExpectedFrameCount) { throw "Decoded video byte length does not contain exactly $ExpectedFrameCount frames." }
    $hashes = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $ExpectedFrameCount; $index++) {
        $frame = [byte[]]::new($FrameSize)
        [Array]::Copy($bytes, $index * $FrameSize, $frame, 0, $FrameSize)
        $hashes.Add([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($frame)))
    }
    return @($hashes)
}

function Compare-Decodes($Case, [string] $SourcePath, [string] $TargetPath) {
    $paths = [ordered]@{
        sourceVideo = Join-Path $workDirectory "$($Case.id).source.rgb24"
        targetVideo = Join-Path $workDirectory "$($Case.id).target.rgb24"
        sourceAudio = Join-Path $workDirectory "$($Case.id).source.s16le"
        targetAudio = Join-Path $workDirectory "$($Case.id).target.s16le"
    }
    try {
        $sourceVideo = Invoke-StrictDecode "decode-source-video-$($Case.id)" $Case $SourcePath '0:v:0' $paths.sourceVideo @('-fps_mode','passthrough','-vf','scale=320:180:flags=bilinear','-f','rawvideo','-pix_fmt','rgb24')
        $targetVideo = Invoke-StrictDecode "decode-target-video-$($Case.id)" $Case $TargetPath '0:v:0' $paths.targetVideo @('-fps_mode','passthrough','-vf','scale=320:180:flags=bilinear','-f','rawvideo','-pix_fmt','rgb24')
        $sourceAudio = Invoke-StrictDecode "decode-source-audio-$($Case.id)" $Case $SourcePath '0:a:0' $paths.sourceAudio @('-f','s16le','-acodec','pcm_s16le')
        $targetAudio = Invoke-StrictDecode "decode-target-audio-$($Case.id)" $Case $TargetPath '0:a:0' $paths.targetAudio @('-f','s16le','-acodec','pcm_s16le')
        $sourceFrameHashes = Get-FrameHashes $paths.sourceVideo (320 * 180 * 3) 5
        $targetFrameHashes = Get-FrameHashes $paths.targetVideo (320 * 180 * 3) 5
        if (($sourceFrameHashes -join ',') -ne ($targetFrameHashes -join ',')) { throw 'Decoded video frame identities changed during the setts remux.' }
        if ($sourceAudio.artifact.length -ne $targetAudio.artifact.length -or $sourceAudio.artifact.sha256 -ne $targetAudio.artifact.sha256) { throw 'Decoded audio identity changed during the setts remux.' }
        return [ordered]@{
            video = [ordered]@{ source = $sourceVideo.artifact; target = $targetVideo.artifact; frameCount = 5; frameIdentities = @('red','green','blue','white','black'); sourceFrameSha256 = $sourceFrameHashes; targetFrameSha256 = $targetFrameHashes; exactIdentity = $true }
            audio = [ordered]@{ source = $sourceAudio.artifact; target = $targetAudio.artifact; exactIdentity = $true }
            commands = @($sourceVideo.command, $targetVideo.command, $sourceAudio.command, $targetAudio.command)
        }
    }
    finally {
        foreach ($path in $paths.Values) { if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force } }
    }
}

function Assert-PresentationFrames($Inspection, [int[]] $ExpectedPts, [int] $ExpectedTerminalDuration) {
    $frames = @($Inspection.frames | Where-Object media_type -eq 'video')
    if ($frames.Count -ne 5) { throw "Expected five presentation frames; observed $($frames.Count)." }
    $pts = @($frames | ForEach-Object { [int64] $_.pts })
    if (($pts -join ',') -ne ($ExpectedPts -join ',')) { throw "Presentation-order PTS changed: $($pts -join ',')." }
    $terminalDuration = [int64] (Get-Value $frames[-1] 'duration' (Get-Value $frames[-1] 'pkt_duration' 0))
    if ($terminalDuration -ne $ExpectedTerminalDuration -or ([int64] $frames[-1].pts + $terminalDuration) -ne 2000) {
        throw "Terminal presentation frame did not end at tick 2000 with duration $ExpectedTerminalDuration."
    }
    return [ordered]@{ frameCount = $frames.Count; presentationPts = $pts; terminalDuration = $terminalDuration; presentationEnd = [int64] $frames[-1].pts + $terminalDuration }
}

$evidencePath = Join-Path $resolvedOutput 'f7-setts-experiment-evidence.json'
try {
    if ($contract.schemaVersion -ne 1 -or $contract.experimentId -ne 'Gate0.G04.F7.Setts.20260825' -or $contract.profileId -ne $p2Manifest.profileId) { throw 'Experiment contract identity does not match exact P2.' }
    if ([string] $contract.componentMapping.setts.ffmpegSourceCommit -ne [string] $p2Manifest.ffmpegSourceCommit) { throw 'The setts source commit mapping does not match exact P2.' }
    if ([string] $p2Manifest.licensePath -ne 'LGPLv3-path' -or [string] $contract.componentMapping.setts.p2BinaryLicensePath -notmatch '^LGPLv3-path') { throw 'The setts experiment requires the exact P2 LGPLv3 license path.' }
    if ([string] $p2Manifest.configuration -notmatch '(?:^| )--enable-version3(?: |$)' -or
        [string] $p2Manifest.configuration -match '(?:^| )--enable-(?:gpl|nonfree)(?: |$)') {
        throw 'The setts experiment requires --enable-version3 without GPL or nonfree configuration flags.'
    }
    $retention = & $retentionValidatorPath -ArtifactRoot $resolvedArtifactRoot
    $runtimeRoot = Resolve-RetainedPath 'p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1' 'Container'
    $ffmpeg = Join-Path $runtimeRoot 'bin\ffmpeg.exe'
    $ffprobe = Join-Path $runtimeRoot 'bin\ffprobe.exe'
    $runtimeEvidencePath = Join-Path $resolvedOutput 'runtime-identity.json'
    & $p2ValidatorPath -RuntimeRoot $runtimeRoot -EvidencePath $runtimeEvidencePath | Out-Null
    if (-not (Test-Path -LiteralPath $runtimeEvidencePath -PathType Leaf)) { throw 'P2 runtime validation did not emit identity evidence.' }

    $bsfList = Invoke-RecordedCommand 'preflight-setts-list' $ffmpeg @('-hide_banner','-bsfs') ([ordered]@{ purpose='exact P2 bitstream-filter presence'; requiredToken='setts' })
    $bsfHelp = Invoke-RecordedCommand 'preflight-setts-help' $ffmpeg @('-hide_banner','-h','bsf=setts') ([ordered]@{ purpose='exact P2 setts option observation'; requiredOptions=@('duration','time_base','prescale') })
    if ($bsfList.record.exitCode -ne 0 -or $bsfHelp.record.exitCode -ne 0) { throw 'setts presence/help preflight failed.' }
    $tokens = @($bsfList.stdout -split "`r?`n" | ForEach-Object Trim | Where-Object { $_ })
    if (@($tokens | Where-Object { $_ -eq 'setts' }).Count -ne 1) { throw 'Exact P2 does not list one setts bitstream filter token.' }
    foreach ($required in 'duration','time_base','prescale') { if ($bsfHelp.stdout -notmatch [regex]::Escape($required)) { throw "setts help lacks required option: $required" } }

    $sourceEvidencePath = Resolve-RetainedPath 'proofs/g0.4-input-corrected/g0.4-input-proof-evidence.json'
    $sourceEvidence = Get-Content -LiteralPath $sourceEvidencePath -Raw | ConvertFrom-Json -Depth 40
    if ((Get-Sha256 $sourceEvidencePath) -ne 'F9D0A742F011BA19D1B7A30B547555D7DE7CC7A64B97F8294DD3CE828FFFD969') { throw 'Corrected source evidence anchor changed.' }

    $preflight = [ordered]@{
        status = 'passed'
        retention = $retention
        runtimeIdentity = Get-OutputFileEvidence $runtimeEvidencePath
        primaryToolSha256 = Get-Sha256 $ffmpeg
        inspectionToolSha256 = Get-Sha256 $ffprobe
        settsPresenceCommand = $bsfList.record
        settsHelpCommand = $bsfHelp.record
        componentDisposition = [ordered]@{
            name = 'setts'
            library = 'libavcodec'
            sourceFile = 'libavcodec/bsf/setts.c'
            ffmpegSourceCommit = [string] $p2Manifest.ffmpegSourceCommit
            sourceLicense = 'LGPL-2.1-or-later'
            p2BinaryLicensePath = 'LGPLv3-path because exact P2 uses --enable-version3'
            externalDependency = $false
            semanticProof = 'Presence is necessary evidence only; each case must pass post-mux packet, timing, payload, and decode oracles.'
        }
        sourceEvidence = [ordered]@{ path = 'proofs/g0.4-input-corrected/g0.4-input-proof-evidence.json'; length = [int64] (Get-Item $sourceEvidencePath).Length; sha256 = Get-Sha256 $sourceEvidencePath }
    }

    $priority = @(
        'V-MP4-H264-MAIN-AAC-MONO-44100-VFR_OFFSET',
        'V-WEBM-VP9-PROFILE0-OPUS-MONO-48000-VFR_OFFSET'
    )
    $orderedCases = @($priority | ForEach-Object { $id=$_; @($contract.cases | Where-Object id -eq $id)[0] }) + @($contract.cases | Where-Object id -notin $priority)
    $stop = $false
    foreach ($case in $orderedCases) {
        if ($stop) {
            $caseResults.Add([ordered]@{ caseId=[string]$case.id; family=[string]$case.muxer; status='not-run'; reason='A prior direct case reached an approved stop condition; no substitution or continuation occurred.' })
            continue
        }
        $started = [DateTimeOffset]::UtcNow
        $partialPath = $null
        try {
            $capability = @($sourceEvidence.capabilities | Where-Object capabilityId -eq ([string] $case.id))
            if ($capability.Count -ne 1) { throw 'Source evidence does not contain exactly one bound direct F7 capability.' }
            $capability = $capability[0]
            if ([string] $capability.status -ne 'failed' -or -not [bool] $capability.executedSemanticProof) { throw 'Source F7 capability is not the expected executed terminal-duration failure.' }
            if (-not [bool] $capability.oracleEvidence.observed.f7Identity.passed) { throw 'Source evidence does not prove the five approved frame identities.' }
            $sourcePath = Resolve-RetainedPath ([string] $case.source)
            if ((Get-Sha256 $sourcePath) -ne [string] $capability.artifact.sha256 -or (Get-Item $sourcePath).Length -ne [int64] $capability.artifact.length) { throw 'Retained source artifact does not match corrected source evidence.' }

            $sourceProbe = Invoke-JsonProbe "inspect-source-$($case.id)" ([string] $case.demuxer) $sourcePath 'fresh pre-setts source inspection'
            $sourceVideo = Get-Stream $sourceProbe.data 'video'
            $sourceAudio = Get-Stream $sourceProbe.data 'audio'
            if ([string] $sourceVideo.time_base -ne '1/1000') { throw 'Source video time base is not the approved 1/1000.' }
            $sourceFrames = @($sourceProbe.data.frames | Where-Object media_type -eq 'video')
            $sourcePts = @($sourceFrames | ForEach-Object { [int64] $_.pts })
            if (($sourcePts -join ',') -ne (@($contract.requiredSemantics.presentationTimestamps) -join ',')) { throw 'Source presentation-order PTS no longer matches the approved F7 contract.' }

            $finalPath = Join-Path $mediaDirectory "$($case.id).setts$($case.extension)"
            $partialPath = "$finalPath.partial"
            $muxArguments = @(
                '-hide_banner','-xerror','-err_detect','explode','-copyts',
                '-f',[string]$case.demuxer,'-i',$sourcePath,
                '-map','0:v:0','-map','0:a:0',
                '-c:v','copy','-c:a','copy',
                '-bsf:v',[string]$contract.componentMapping.setts.expression
            )
            if ([string] $case.muxer -eq 'mp4') { $muxArguments += @('-video_track_timescale','1000') }
            $muxArguments += @('-f',[string]$case.muxer,'-y',$partialPath)
            $mux = Invoke-RecordedCommand "setts-remux-$($case.id)" $ffmpeg $muxArguments ([ordered]@{
                purpose = 'bounded duration-only setts stream-copy experiment'
                inputDemuxer = [string] $case.demuxer
                outputMuxer = [string] $case.muxer
                videoCodecMode = 'copy'
                audioCodecMode = 'copy'
                videoBitstreamFilter = 'setts'
                expression = [string] $contract.componentMapping.setts.expression
                targetSelection = 'unique input video packet with PTS=1200, never packet ordinal'
            })
            if ($mux.record.exitCode -ne 0 -or -not (Test-Path -LiteralPath $partialPath -PathType Leaf)) { throw 'setts remux command failed.' }
            if ($mux.stderr -match 'Automatically inserted bitstream filter') { throw 'The mux path auto-inserted an unreviewed bitstream filter.' }
            Move-Item -LiteralPath $partialPath -Destination $finalPath

            $targetProbe = Invoke-JsonProbe "inspect-target-$($case.id)" ([string] $case.demuxer) $finalPath 'fresh post-mux target inspection'
            $targetVideo = Get-Stream $targetProbe.data 'video'
            $targetAudio = Get-Stream $targetProbe.data 'audio'
            $videoPackets = Compare-PacketStreams $sourceProbe.data $targetProbe.data $sourceVideo $targetVideo $true
            $audioPackets = Compare-PacketStreams $sourceProbe.data $targetProbe.data $sourceAudio $targetAudio $false
            $presentation = Assert-PresentationFrames $targetProbe.data ([int[]] $contract.requiredSemantics.presentationTimestamps) 800
            $sourceDuration = [double] $sourceProbe.data.format.duration
            $targetDuration = [double] $targetProbe.data.format.duration
            if ([math]::Abs($sourceDuration - $targetDuration) -gt 0.001 -or [math]::Abs($targetDuration - 2.0) -gt 0.02) { throw 'Container duration changed or left the approved two-second tolerance.' }
            $decode = Compare-Decodes $case $sourcePath $finalPath

            $caseResults.Add([ordered]@{
                caseId = [string] $case.id
                family = [string] $case.muxer
                status = 'passed'
                reason = 'The unique PTS-1200 video packet received duration 800 after the final mux; all other packet timing, payloads, decoded frames, and decoded audio were preserved.'
                source = [ordered]@{ path=[string]$case.source; length=[int64](Get-Item $sourcePath).Length; sha256=Get-Sha256 $sourcePath; priorFrameIdentityOracle=$capability.oracleEvidence.observed.f7Identity }
                output = Get-OutputFileEvidence $finalPath
                components = [ordered]@{ demuxer=[string]$case.demuxer; muxer=[string]$case.muxer; videoDecoder=[string]$case.videoDecoder; audioDecoder=[string]$case.audioDecoder; bitstreamFilter='setts'; encoders=@(); streamCopy=$true }
                sourceInspection = $sourceProbe.record
                targetInspection = $targetProbe.record
                packetIdentity = [ordered]@{ video=$videoPackets; audio=$audioPackets }
                presentation = $presentation
                containerDuration = [ordered]@{ source=$sourceDuration; target=$targetDuration; toleranceSeconds=0.001 }
                decodeIdentity = $decode
                elapsedMilliseconds = ([DateTimeOffset]::UtcNow-$started).TotalMilliseconds
            })
        }
        catch {
            if (-not [string]::IsNullOrWhiteSpace([string] $partialPath) -and (Test-Path -LiteralPath $partialPath -PathType Leaf)) { Remove-Item -LiteralPath $partialPath -Force }
            $caseResults.Add([ordered]@{
                caseId = [string] $case.id
                family = [string] $case.muxer
                status = 'blocked'
                reason = $_.Exception.Message
                contractWasNotWeakened = $true
                elapsedMilliseconds = ([DateTimeOffset]::UtcNow-$started).TotalMilliseconds
            })
            $stop = $true
        }
    }

    $passed = @($caseResults | Where-Object status -eq 'passed').Count
    $blocked = @($caseResults | Where-Object status -eq 'blocked').Count
    $notRun = @($caseResults | Where-Object status -eq 'not-run').Count
    $runStatus = if ($passed -eq @($contract.cases).Count) { 'passed' } else { 'blocked' }
    $runReason = if ($runStatus -eq 'passed') { 'All six direct MP4/WebM F7 setts cases passed the bounded post-mux semantic proof.' } else { 'A direct F7 case reached an approved stop condition; remaining cases were not substituted or forced.' }
}
catch {
    $runStatus = 'preflight-blocked'
    $runReason = $_.Exception.Message
    $preflight.status = 'blocked'
    $preflight.reason = $runReason
}
finally {
    $finalArtifacts = @(Get-ChildItem -LiteralPath $mediaDirectory -File | Sort-Object Name | ForEach-Object { Get-OutputFileEvidence $_.FullName })
    $evidence = [ordered]@{
        schemaVersion = 1
        experimentId = [string] $contract.experimentId
        profileId = [string] $contract.profileId
        run = [ordered]@{ status=$runStatus; reason=$runReason; passed=@($caseResults|Where-Object status -eq 'passed').Count; blocked=@($caseResults|Where-Object status -eq 'blocked').Count; notRun=@($caseResults|Where-Object status -eq 'not-run').Count }
        statement = 'Proof-only F7 terminal-duration experiment. No product, shipping-runtime, distribution, or public media-contract selection is made.'
        contract = [ordered]@{ path='eng/gate0/f7-setts-experiment-contract.json'; sha256=Get-Sha256 $contractPath }
        preflight = $preflight
        componentMapping = $contract.componentMapping
        requiredSemantics = $contract.requiredSemantics
        cases = @($caseResults)
        commands = @($commands)
        finalArtifacts = $finalArtifacts
        stopConditions = $contract.stopConditions
        boundaries = $contract.boundaries
        nextGate = if ($runStatus -eq 'passed') { 'The owner-approved six-case direct Matroska pilot may proceed separately.' } else { 'The Matroska pilot remains blocked unless the owner explicitly dispositions this result.' }
    }
    [IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
}

Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json -Depth 10
