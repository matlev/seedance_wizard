# G0.4 executable proof helpers.  This file is dot-sourced by the runner; it
# intentionally has no product-facing behavior.
Set-StrictMode -Version Latest

if (-not ('ReelForge.Gate0.ByteOracle' -as [type])) {
    Add-Type -TypeDefinition @'
namespace ReelForge.Gate0
{
    public static class ByteOracle
    {
        public static double MeanAbsoluteError(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                throw new System.ArgumentException("Oracle byte lengths differ.");
            if (left.Length == 0) return 0;
            long sum = 0;
            for (int i = 0; i < left.Length; i++) sum += System.Math.Abs(left[i] - right[i]);
            return (double)sum / left.Length;
        }
    }
}
'@
}

function Get-G04ConcreteDemuxer([string]$DisplayName) {
    $map = @{ 'mov,mp4,m4a,3gp,3g2,mj2' = 'mov'; 'matroska,webm' = 'matroska'; 'matroska' = 'matroska'; 'image2' = 'image2'; 'wav' = 'wav'; 'flac' = 'flac'; 'mp3' = 'mp3'; 'aac' = 'aac'; 'ogg' = 'ogg' }
    if (-not $map.ContainsKey($DisplayName)) { throw "No safe concrete FFmpeg demuxer mapping exists for contract display '$DisplayName'." }
    return $map[$DisplayName]
}

function Get-G04Property($Object, [string]$Name, $Default = $null) {
    if ($Object -is [Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $Default
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}
function Normalize-G04Profile([string]$Profile) {
    if([string]::IsNullOrWhiteSpace($Profile)){return $null}
    $token=($Profile.ToLowerInvariant() -replace '[ _-]','')
    $map=@{'constrainedbaseline'='constrained_baseline';'baseline'='baseline';'main'='main';'high'='high';'profile0'='profile0';'lc'='aac_low';'aaclc'='aac_low';'lcaac'='aac_low';'aaclow'='aac_low'}
    if($map.ContainsKey($token)){return $map[$token]};return $token
}
function Get-G04SofMarker([string]$Path) {
    $bytes=Get-G04RawBytes $Path;for($i=0;$i -lt $bytes.Length-1;$i++){if($bytes[$i] -eq 0xFF -and $bytes[$i+1] -in 0xC0,0xC1,0xC2){return ('0x{0:X2}' -f $bytes[$i+1])}};throw 'JPEG SOF marker was not found.'
}
function Get-G04ToneEvidence([byte[]]$Bytes,[int]$Rate,[int]$Channels) {
    $samples=[int]($Bytes.Length/(2*$Channels));$sum=[double[]]::new($Channels);$cross=0.0;$leftSq=0.0;$rightSq=0.0
    for($n=0;$n -lt $samples;$n++){for($c=0;$c -lt $Channels;$c++){$v=[BitConverter]::ToInt16($Bytes,(($n*$Channels+$c)*2));$sum[$c]+=[math]::Abs($v)};if($Channels -eq 2){$l=[BitConverter]::ToInt16($Bytes,$n*4);$r=[BitConverter]::ToInt16($Bytes,$n*4+2);$cross+=$l*$r;$leftSq+=$l*$l;$rightSq+=$r*$r}}
    $phase=if($Channels -eq 2 -and $leftSq -gt 0 -and $rightSq -gt 0){$cross/[math]::Sqrt($leftSq*$rightSq)}else{$null};return [ordered]@{samplesPerChannel=$samples;meanAbsoluteAmplitude=@($sum|%{$_/$samples});opposedPhaseCorrelation=$phase;rate=$Rate;channels=$Channels}
}

function Test-G04F7TerminalTiming([object[]]$Frames, [object]$Timing) {
    if ($Frames.Count -eq 0) { throw 'F7 terminal timing requires at least one video frame.' }
    $terminalFrame=$Frames[-1]
    $terminalPts=[int64](Get-G04Property $terminalFrame 'pts' 0)
    $observedDuration=[int64](Get-G04Property $terminalFrame 'duration' (Get-G04Property $terminalFrame 'pkt_duration' 0))
    $observedEnd=$terminalPts+$observedDuration
    $expectedDuration=[int64]$Timing.terminalFrameDuration
    $expectedEnd=[int64]@($Timing.presentationPts)[-1]+$expectedDuration
    return [ordered]@{
        passed=($observedDuration -eq $expectedDuration -and $observedEnd -eq $expectedEnd)
        observedDuration=$observedDuration
        observedEnd=$observedEnd
        expectedDuration=$expectedDuration
        expectedEnd=$expectedEnd
    }
}

function Invoke-G04OracleCommand([hashtable]$Context, [string]$Name, [string]$Executable, [string[]]$Arguments, [hashtable]$Components, [string]$CaseId) {
    $record = Invoke-G04RecordedCommand -Context $Context -Name $Name -Executable $Executable -Arguments $Arguments -Components $Components
    $null = Test-G04UndeclaredDiagnostics -Stderr ([string](Get-G04Property $record 'stderr' '')) -AllowedPatterns @()
    return $record
}

function Get-G04Probe([object]$Case, [string]$ArtifactPath, [hashtable]$Context) {
    $demuxer = Get-G04ConcreteDemuxer ([string]$Case.requiredComponents.demuxer)
    $record = Invoke-G04OracleCommand $Context ("inspect-" + $Case.id) $Context.Ffprobe @('-v','error','-f',$demuxer,'-show_format','-show_streams','-show_frames','-show_packets','-show_data_hash','sha256','-of','json',$ArtifactPath) @{ purpose='fresh content inspection'; demuxer=$demuxer; allStreams=$true; allFrames=$true; allPackets=$true; packetDataHash='sha256' } $Case.id
    $text = [string](Get-G04Property $record 'stdout' '')
    if ([string]::IsNullOrWhiteSpace($text)) { throw "Fresh ffprobe inspection returned no JSON for $($Case.id)." }
    try { $data = $text | ConvertFrom-Json } catch { throw "Fresh ffprobe inspection JSON is invalid for $($Case.id): $($_.Exception.Message)" }
    # Current ffprobe combines -show_frames and -show_packets into one
    # packets_and_frames array. Normalize that documented output shape without
    # changing the retained raw command evidence.
    if ($null -eq (Get-G04Property $data 'frames') -and $null -ne (Get-G04Property $data 'packets_and_frames')) {
        $data | Add-Member -NotePropertyName frames -NotePropertyValue @($data.packets_and_frames | Where-Object type -eq 'frame')
        $data | Add-Member -NotePropertyName packets -NotePropertyValue @($data.packets_and_frames | Where-Object type -eq 'packet')
    }
    return @{ record=$record; data=$data }
}

function Assert-G04StreamContract([object]$Case, [object]$Probe) {
    $observed = @($Probe.streams)
    if ($observed.Count -ne @($Case.streams).Count) { throw "Stream-count oracle failed for $($Case.id): expected $(@($Case.streams).Count), observed $($observed.Count)." }
    $checks = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt @($Case.streams).Count; $i++) {
        $expected = @($Case.streams)[$i]; $actual = $observed[$i]
        $actualCodecType=Get-G04Property $actual 'codec_type';$actualCodec=Get-G04Property $actual 'codec_name';$actualProfile=Get-G04Property $actual 'profile';$actualLevel=Get-G04Property $actual 'level';$actualPixelFormat=Get-G04Property $actual 'pix_fmt';$actualWidth=Get-G04Property $actual 'width';$actualHeight=Get-G04Property $actual 'height';$actualFrameRate=Get-G04Property $actual 'avg_frame_rate';$actualSampleRate=Get-G04Property $actual 'sample_rate';$actualChannels=Get-G04Property $actual 'channels'
        $actualType = if ($expected.type -eq 'image') { 'image' } else { [string]$actualCodecType }
        $pass = $actualType -eq [string]$expected.type -and [string]$actualCodec -eq [string]$expected.codec
        if ($null -ne (Get-G04Property $expected 'profile')) { $pass = $pass -and ((Normalize-G04Profile ([string]$actualProfile)) -eq (Normalize-G04Profile ([string]$expected.profile))) }
        if ($null -ne (Get-G04Property $expected 'pixelFormat')) { $pass = $pass -and ([string]$actualPixelFormat -eq [string]$expected.pixelFormat) }
        if ($null -ne (Get-G04Property $expected 'width')) { $pass = $pass -and ([int]$actualWidth -eq [int]$expected.width) }
        if ($null -ne (Get-G04Property $expected 'height')) { $pass = $pass -and ([int]$actualHeight -eq [int]$expected.height) }
        if ($null -ne (Get-G04Property $expected 'sampleRate')) { $pass = $pass -and ([int]$actualSampleRate -eq [int]$expected.sampleRate) }
        $expectedChannels = Get-G04Property $expected 'channels'
        if ($null -ne $expectedChannels) { $count = if ($expectedChannels -eq 'mono') { 1 } elseif ($expectedChannels -eq 'stereo') { 2 } else { [int]$expectedChannels }; $pass = $pass -and ([int]$actualChannels -eq $count) }
        $maximumLevel = Get-G04Property $expected 'maximumLevel'
        if ($null -ne $maximumLevel -and $null -ne $actualLevel) { $pass = $pass -and ([int]$actualLevel -le [int]([double]$maximumLevel * 10)) }
        if($expected.type -eq 'video' -and $Case.timing.kind -eq 'cfr'){$pass=$pass -and ([string]$actualFrameRate -eq [string]$Case.timing.frameRate)}
        $checks.Add([ordered]@{ streamIndex=$i; expected=$expected; observed=[ordered]@{codec=$actualCodec;profile=$actualProfile;normalizedProfile=(Normalize-G04Profile ([string]$actualProfile));level=$actualLevel;pixelFormat=$actualPixelFormat;width=$actualWidth;height=$actualHeight;averageFrameRate=$actualFrameRate;sampleRate=$actualSampleRate;channels=$actualChannels}; passed=$pass })
        if (-not $pass) { throw "Stream codec/profile/level/pixel-format/raster/sample oracle failed for $($Case.id), stream $i." }
    }
    return @($checks)
}

function Invoke-G04StrictDecode([object]$Case, [string]$ArtifactPath, [hashtable]$Context, [string]$StreamMap, [string]$OutputPath, [string[]]$OutputArguments) {
    $demuxer = Get-G04ConcreteDemuxer ([string]$Case.requiredComponents.demuxer)
    $args = @('-hide_banner','-xerror','-err_detect','explode','-f',$demuxer)
    # Native decoder selection must precede the input.  It is deliberately
    # repeated by media type so FFmpeg cannot auto-select an unrelated decoder.
    $byType = @{}; foreach ($stream in @($Case.streams)) { $type = if ($stream.type -eq 'image') { 'v' } elseif ($stream.type -eq 'video') { 'v' } else { 'a' }; if (-not $byType.ContainsKey($type)) { $byType[$type] = [string]$stream.codec } }
    foreach ($type in @('v','a')) { if ($byType.ContainsKey($type)) { $args += @("-c:$type",$byType[$type]) } }
    $args += @('-i',$ArtifactPath,'-map',$StreamMap,'-map_metadata','-1') + $OutputArguments + @($OutputPath)
    return Invoke-G04OracleCommand $Context ("decode-$($Case.id)-$($StreamMap.Replace(':','_'))") $Context.Ffmpeg $args @{ purpose='strict complete decode'; demuxer=$demuxer; streamMap=$StreamMap; explicitDecoders=$byType; strictErrors=@('-xerror','-err_detect explode') } $Case.id
}

function Get-G04RawBytes([string]$Path) { if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Expected decoded oracle artifact missing: $Path" }; return [IO.File]::ReadAllBytes($Path) }
function Get-G04Mae([byte[]]$Left,[byte[]]$Right) { return [ReelForge.Gate0.ByteOracle]::MeanAbsoluteError($Left,$Right) }
function Get-G04PpmPixels([string]$Path) {
    $bytes=Get-G04RawBytes $Path;$header=[Text.Encoding]::ASCII.GetString($bytes,0,[Math]::Min($bytes.Length,128));$match=[regex]::Match($header,'^P6\s+(?:#.*\s+)?(\d+)\s+(\d+)\s+255\s+',[Text.RegularExpressions.RegexOptions]::Singleline)
    if(-not $match.Success){throw "Expected binary PPM reference raster: $Path"};$offset=$match.Length;return [ordered]@{width=[int]$match.Groups[1].Value;height=[int]$match.Groups[2].Value;pixels=$bytes[$offset..($bytes.Length-1)]}
}
function Get-G04Sha256([byte[]]$Bytes) { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)) }
function Test-G04ByteIdentity([byte[]]$Left, [byte[]]$Right) { return $Left.Length -eq $Right.Length -and [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($Left,$Right) }
function Remove-G04OracleArtifact([string]$Path) { if (Test-Path -LiteralPath $Path -PathType Leaf) { Remove-Item -LiteralPath $Path -Force } }
function Get-G04StreamMaps([object]$Case) {
    $maps = [Collections.Generic.List[string]]::new(); $ordinals = @{}
    foreach ($stream in @($Case.streams)) {
        $kind = if ($stream.type -in @('image','video')) { 'v' } else { 'a' }
        if (-not $ordinals.ContainsKey($kind)) { $ordinals[$kind] = 0 }
        $maps.Add("0:$kind`:$($ordinals[$kind])"); $ordinals[$kind]++
    }
    return @($maps)
}
function Get-G04RawVideoFrames([object]$Case, [string]$ArtifactPath, [hashtable]$Context, [string]$Name) {
    $raw = Join-Path $Context.Work "$Name-320x180.rgb24"
    try {
        Invoke-G04StrictDecode $Case $ArtifactPath $Context '0:v:0' $raw @('-fps_mode','passthrough','-vf','scale=320:180:flags=bilinear','-f','rawvideo','-pix_fmt','rgb24') | Out-Null
        $bytes = Get-G04RawBytes $raw; $frameLength = 320 * 180 * 3
        if (($bytes.Length % $frameLength) -ne 0) { throw "Decoded video raster for $($Case.id) does not contain whole 320x180 rgb24 frames." }
        $frames = [Collections.Generic.List[byte[]]]::new()
        for ($offset = 0; $offset -lt $bytes.Length; $offset += $frameLength) { $frame = [byte[]]::new($frameLength); [Array]::Copy($bytes, $offset, $frame, 0, $frameLength); $frames.Add($frame) }
        Write-Output -NoEnumerate ([byte[][]]$frames.ToArray())
    } finally { Remove-G04OracleArtifact $raw }
}
function Get-G04PpmReferenceFrames([hashtable]$Context, [string[]]$FileIds) {
    $references = [Collections.Generic.List[byte[]]]::new()
    foreach ($fileId in $FileIds) { $ppm = Get-G04PpmPixels (Join-Path $Context.FixtureRoot $fileId); if ($ppm.width -ne 320 -or $ppm.height -ne 180 -or $ppm.pixels.Length -ne 172800) { throw "Reference PPM $fileId is not 320x180 rgb24." }; $references.Add([byte[]]$ppm.pixels) }
    Write-Output -NoEnumerate ([byte[][]]$references.ToArray())
}
function Assert-G04FrameSequence([byte[][]]$Frames, [byte[][]]$References, [string[]]$Names, [double]$MaximumMae, [string[]]$ExpectedSequence) {
    if ($Frames.Count -ne $ExpectedSequence.Count) { throw "Frame identity count mismatch: expected $($ExpectedSequence.Count), observed $($Frames.Count)." }
    $referenceSeparation = for ($left = 0; $left -lt $References.Count; $left++) { for ($right = $left + 1; $right -lt $References.Count; $right++) { Get-G04Mae $References[$left] $References[$right] } }
    $referenceHashes = @($References | ForEach-Object { Get-G04Sha256 $_ })
    if (@($referenceHashes | Select-Object -Unique).Count -ne $References.Count) { throw 'Reference frame identities are not byte-distinct.' }
    $observed = [Collections.Generic.List[object]]::new(); $passed = $true
    for ($i = 0; $i -lt $Frames.Count; $i++) {
        $maes = @($References | ForEach-Object { Get-G04Mae $Frames[$i] $_ }); $minimum = ($maes | Measure-Object -Minimum).Minimum; $actualIndex = [array]::IndexOf([double[]]$maes, [double]$minimum); $actual = $Names[$actualIndex]
        $expectedIndex = [array]::IndexOf([string[]]$Names, $ExpectedSequence[$i]); if ($expectedIndex -lt 0) { throw "Unknown expected frame identity $($ExpectedSequence[$i])." }
        $passed = $passed -and ($actual -eq $ExpectedSequence[$i]) -and ($maes[$expectedIndex] -le $MaximumMae)
        $observed.Add([ordered]@{ index=$i; expected=$ExpectedSequence[$i]; observed=$actual; maes=$maes; expectedMae=$maes[$expectedIndex] })
    }
    $distinct = @($observed | ForEach-Object { $_.observed } | Select-Object -Unique)
    $passed = $passed -and ($distinct.Count -eq $References.Count)
    return [ordered]@{ passed=$passed; frames=@($observed); distinctIdentityCount=$distinct.Count; referenceSha256=$referenceHashes; referencePairwiseMae=@($referenceSeparation); maximumMae=$MaximumMae }
}
function Assert-G04ExpectedAudioDecode([byte[]]$Bytes, [object]$Audio, [object]$Recipe, [Collections.IDictionary]$Observed, [Collections.IDictionary]$Threshold) {
    $tone = Get-G04ToneEvidence $Bytes ([int]$Audio.sample_rate) ([int]$Audio.channels); $Observed.audioTone = $tone
    $expect = Get-G04Property $Recipe 'expectedAudioDecode' @{}
    $sampleEnvelope = Get-G04Property $expect 'sampleEnvelope'
    if ($null -ne $sampleEnvelope) { $Threshold.audioSampleTolerance=$sampleEnvelope.tolerance; if ([math]::Abs($tone.samplesPerChannel-[int]$sampleEnvelope.expected) -gt [int]$sampleEnvelope.tolerance) { return $false } }
    if (@($tone.meanAbsoluteAmplitude | Where-Object { $_ -gt 50 }).Count -ne [int]$Audio.channels) { return $false }
    if ([int]$Audio.channels -eq 2) { $Threshold.opposedPhaseMaximum=-0.60; if ($tone.opposedPhaseCorrelation -gt -0.60) { return $false } }
    return $true
}
function Get-G04LosslessExpectedPcm([object]$Recipe, [object]$Audio, [hashtable]$Context, [string]$CaseId) {
    $source = @($Recipe.sourceArtifacts | Where-Object { $_.declaredFormat -isnot [string] }) | Select-Object -First 1
    if ($null -eq $source) { throw "Lossless recipe $($Recipe.id) has no typed raw PCM source." }
    $format = $source.declaredFormat; $sourcePath = Join-Path $Context.FixtureRoot ([string]@($source.fileIds)[0]); $rawChannels = if ([string]$format.channels -eq 'mono') { 1 } elseif ([string]$format.channels -eq 'stereo') { 2 } else { throw "Lossless recipe $($Recipe.id) has unsupported source channel layout." }
    $targetChannels = [int]$Audio.channels; $targetRate = [int]$Audio.sample_rate
    $declaredTransforms = [string[]]@($source.transforms); if ($declaredTransforms -notcontains "aresample=$targetRate" -or $declaredTransforms -notcontains "channels=$(if($targetChannels -eq 1){'mono'}else{'stereo'})" -or $declaredTransforms -notcontains 'channel-layout=identity') { throw "Lossless recipe $($Recipe.id) does not declare the exact PCM transform." }
    $work = Join-Path $Context.Work "$CaseId-expected-lossless.s16le"
    try {
        $filter = "aresample=$targetRate,aformat=channel_layouts=$(if($targetChannels -eq 1){'mono'}else{'stereo'})"
        Invoke-G04OracleCommand $Context "expected-lossless-$CaseId" $Context.Ffmpeg @('-hide_banner','-xerror','-err_detect','explode','-f','s16le','-c:a','pcm_s16le','-ar',[string]$format.sampleRate,'-ac',[string]$rawChannels,'-i',$sourcePath,'-map','0:a:0','-af',$filter,'-f','s16le','-c:a','pcm_s16le','-y',$work) @{purpose='declared lossless PCM transform oracle';source=$sourcePath;declaredFormat=$format;transforms=$declaredTransforms;strictErrors=@('-xerror','-err_detect explode')} $CaseId | Out-Null
        return Get-G04RawBytes $work
    } finally { Remove-G04OracleArtifact $work }
}
function Get-G04ArtifactPath([hashtable]$Context, [string]$CaseId) { $entry=$Context.ArtifactsByCase[$CaseId]; if ($entry -is [string]) { return $entry }; $path=Get-G04Property $entry 'path'; if ([string]::IsNullOrWhiteSpace([string]$path)) { throw "No authored artifact path is available for source case $CaseId." }; return [string]$path }
function Get-G04ComparableStreamStructure([object]$Probe) {
    return @($Probe.streams | ForEach-Object {
        [ordered]@{
            index=Get-G04Property $_ 'index'; codecType=Get-G04Property $_ 'codec_type'; codec=Get-G04Property $_ 'codec_name'
            profile=Normalize-G04Profile ([string](Get-G04Property $_ 'profile')); level=Get-G04Property $_ 'level'
            pixelFormat=Get-G04Property $_ 'pix_fmt'; sampleRate=Get-G04Property $_ 'sample_rate'; channels=Get-G04Property $_ 'channels'
            disposition=Get-G04Property $_ 'disposition'
        }
    })
}
function Get-G04ComparablePresentationTimeline([object]$Probe) {
    return @($Probe.frames | ForEach-Object {
        $pts = Get-G04Property $_ 'best_effort_timestamp_time' (Get-G04Property $_ 'pts_time')
        [ordered]@{
            streamIndex=Get-G04Property $_ 'stream_index'; mediaType=Get-G04Property $_ 'media_type'
            presentationSeconds=$(if($null -eq $pts){$null}else{[math]::Round([double]$pts,6)})
            durationSeconds=$(if($null -eq (Get-G04Property $_ 'duration_time')){$null}else{[math]::Round([double](Get-G04Property $_ 'duration_time'),6)})
        }
    })
}
function Test-G04NumericSequenceEqual([object[]]$Left, [object[]]$Right, [double]$Tolerance) {
    if ($Left.Count -ne $Right.Count) { return $false }
    for($i=0;$i -lt $Left.Count;$i++) {
        if ($null -eq $Left[$i] -or $null -eq $Right[$i]) { if ($null -ne $Left[$i] -or $null -ne $Right[$i]) { return $false }; continue }
        if ([math]::Abs([double]$Left[$i]-[double]$Right[$i]) -gt $Tolerance) { return $false }
    }
    return $true
}
function Get-G04PacketPayloadEvidence([object]$Probe, [int]$StreamIndex) {
    $hashes = @($Probe.packets | Where-Object { [int](Get-G04Property $_ 'stream_index' -1) -eq $StreamIndex } | ForEach-Object {
        $hash = [string](Get-G04Property $_ 'data_hash')
        if ([string]::IsNullOrWhiteSpace($hash) -or -not $hash.StartsWith('SHA256:',[StringComparison]::OrdinalIgnoreCase)) { throw "Packet payload hash is missing for stream $StreamIndex." }
        $hash.ToUpperInvariant()
    })
    if ($hashes.Count -eq 0) { throw "No packet payload hashes were observed for stream $StreamIndex." }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($hashes -join "`n"))
    return [ordered]@{ streamIndex=$StreamIndex; packetCount=$hashes.Count; packetSha256=$hashes; aggregateSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) }
}
function Assert-G04RemuxIdentity([object]$Case, [object]$Recipe, [string]$ArtifactPath, [hashtable]$Context) {
    $sourceId = [string]$Case.fixtureProduction.sourceCaseId; if ([string]::IsNullOrWhiteSpace($sourceId) -or -not $Context.CaseById.ContainsKey($sourceId)) { throw "Remux case $($Case.id) has no resolved source case." }
    $sourceCase=$Context.CaseById[$sourceId]; $sourcePath=Get-G04ArtifactPath $Context $sourceId; $sourceProbe=Get-G04Probe $sourceCase $sourcePath $Context; $targetProbe=Get-G04Probe $Case $ArtifactPath $Context
    $sourceStructure=Get-G04ComparableStreamStructure $sourceProbe.data; $targetStructure=Get-G04ComparableStreamStructure $targetProbe.data; $streamStructureEqual=(($sourceStructure|ConvertTo-Json -Depth 10 -Compress) -eq ($targetStructure|ConvertTo-Json -Depth 10 -Compress))
    $sourceTimeline=Get-G04ComparablePresentationTimeline $sourceProbe.data; $targetTimeline=Get-G04ComparablePresentationTimeline $targetProbe.data
    $timelineShapeEqual=$true;$presentationEqual=$true;$durationEqual=$true
    foreach($streamIndex in @($sourceStructure.index)) {
        $sourceStreamTimeline=@($sourceTimeline|Where-Object streamIndex -eq $streamIndex);$targetStreamTimeline=@($targetTimeline|Where-Object streamIndex -eq $streamIndex)
        $timelineShapeEqual=$timelineShapeEqual -and (($sourceStreamTimeline.mediaType -join ',') -eq ($targetStreamTimeline.mediaType -join ','))
        $presentationEqual=$presentationEqual -and (Test-G04NumericSequenceEqual @($sourceStreamTimeline.presentationSeconds) @($targetStreamTimeline.presentationSeconds) 0.001)
        $durationEqual=$durationEqual -and (Test-G04NumericSequenceEqual @($sourceStreamTimeline.durationSeconds) @($targetStreamTimeline.durationSeconds) 0.001)
    }
    $sourceDuration=[double](Get-G04Property $sourceProbe.data.format 'duration' 0);$targetDuration=[double](Get-G04Property $targetProbe.data.format 'duration' 0);$containerDurationEqual=[math]::Abs($sourceDuration-$targetDuration) -le 0.002
    $timingEqual=$timelineShapeEqual -and $presentationEqual -and $durationEqual -and $containerDurationEqual
    $payloads=[Collections.Generic.List[object]]::new();$hashesEqual=$true
    foreach($streamIndex in @($sourceStructure.index)){$sourcePayload=Get-G04PacketPayloadEvidence $sourceProbe.data $streamIndex;$targetPayload=Get-G04PacketPayloadEvidence $targetProbe.data $streamIndex;$equal=$sourcePayload.aggregateSha256 -eq $targetPayload.aggregateSha256;$hashesEqual=$hashesEqual -and $equal;$payloads.Add([ordered]@{streamIndex=$streamIndex;packetCount=$sourcePayload.packetCount;sourceSha256=$sourcePayload.aggregateSha256;targetSha256=$targetPayload.aggregateSha256;equal=$equal;sourcePacketSha256=$sourcePayload.packetSha256;targetPacketSha256=$targetPayload.packetSha256})}
    return [ordered]@{passed=($streamStructureEqual -and $timingEqual -and $hashesEqual);sourceCaseId=$sourceId;sourceStructure=$sourceStructure;targetStructure=$targetStructure;streamStructureEqual=$streamStructureEqual;timingEqual=$timingEqual;timing=[ordered]@{sourceTimeline=$sourceTimeline;targetTimeline=$targetTimeline;timelineShapeEqual=$timelineShapeEqual;presentationEqual=$presentationEqual;durationEqual=$durationEqual;containerDurationEqual=$containerDurationEqual;toleranceSeconds=0.001};streamCopyPayloads=@($payloads);independentCompleteDecode='Source case passed its bound semantic proof before remux authoring; target case passed explicit strict complete decode before this packet-payload identity oracle.';sourceProbe=$sourceProbe.record;targetProbe=$targetProbe.record}
}

function Test-G04CaseEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Case,[Parameter(Mandatory)]$Recipe,[Parameter(Mandatory)]$Oracle,[Parameter(Mandatory)][string]$ArtifactPath,[Parameter(Mandatory)][hashtable]$Context)
    if (-not [IO.Path]::IsPathRooted($ArtifactPath)) { throw 'ArtifactPath must be rooted.' }
    if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf)) { throw "ArtifactPath does not exist: $ArtifactPath" }
    foreach($key in @('Ffmpeg','Ffprobe','FixtureRoot','Work','Commands')) { if (-not $Context.ContainsKey($key) -or $null -eq $Context[$key]) { throw "Oracle context is missing $key." } }
    $cleanupArtifacts = [Collections.Generic.List[string]]::new()
    $decodeEvidence=[Collections.Generic.List[object]]::new()
    try {
    $inspection = Get-G04Probe $Case $ArtifactPath $Context
    $streamEvidence = Assert-G04StreamContract $Case $inspection.data
    $allMaps = [Collections.Generic.List[string]]::new(); foreach($map in @(Get-G04StreamMaps $Case)) { $allMaps.Add($map) }
    foreach($map in $allMaps) { $raw=Join-Path $Context.Work ("$($Case.id)-$($map.Replace(':','_')).raw"); $cleanupArtifacts.Add($raw); $selected=@($Case.streams)[$decodeEvidence.Count];$outputArgs=if($selected.type -eq 'audio'){@('-f','s16le','-acodec','pcm_s16le')}else{@('-fps_mode','passthrough','-f','rawvideo','-pix_fmt','rgb24')}; $decodeEvidence.Add([ordered]@{map=$map;type=$selected.type;path=$raw;record=(Invoke-G04StrictDecode $Case $ArtifactPath $Context $map $raw $outputArgs)}) }
    $kind=[string]$Oracle.kind; $expected=[ordered]@{case=$Case.id;oracle=$Oracle.id;recipe=$Recipe.id}; $observed=[ordered]@{streamChecks=$streamEvidence;format=$inspection.data.format;frameCount=@($inspection.data.frames).Count;decodeMaps=@($allMaps)}; $threshold=[ordered]@{}; $passed=$true
    if($kind -eq 'complete-decode') {
        $expectedFrames=Get-G04Property (Get-G04Property $Recipe 'expectedDecodedFrameCount' @{}) 'exact'
        if($null -ne $expectedFrames) { $threshold.exactFrameCount=$expectedFrames; $passed=$passed -and (@($inspection.data.frames | Where-Object media_type -eq 'video').Count -eq [int]$expectedFrames) }
        $f1Source = @($Recipe.sourceArtifacts | Where-Object { $_.variantId -eq 'F1-three-patterns' }) | Select-Object -First 1
        if ($null -ne $f1Source) { $f1References=Get-G04PpmReferenceFrames $Context ([string[]]@($f1Source.fileIds));$frameCount=@($inspection.data.frames|Where-Object media_type -eq 'video').Count;$cycle=for($i=0;$i-lt $frameCount;$i++){@('f1-pattern-000','f1-pattern-001','f1-pattern-002')[$i % 3]};$f1=Assert-G04FrameSequence (Get-G04RawVideoFrames $Case $ArtifactPath $Context "$($Case.id)-f1") $f1References @('f1-pattern-000','f1-pattern-001','f1-pattern-002') 20 $cycle;$threshold.f1MaximumMeanAbsoluteError=20;$threshold.f1DistinctIdentities=3;$observed.f1Identity=$f1;$passed=$passed -and $f1.passed }
        if($Oracle.id -match 'F7') {
            $timing=$Recipe.presentationTiming
            $vframes=@($inspection.data.frames|Where-Object media_type -eq 'video')
            $videoStream=@($inspection.data.streams|Where-Object codec_type -eq 'video')[0]
            $pts=@($vframes|ForEach-Object {[int64]$_.pts})
            $intervals=@()
            for($i=1;$i -lt $pts.Count;$i++){$intervals += $pts[$i]-$pts[$i-1]}
            $expectedIntervals=@()
            for($i=1;$i -lt @($timing.presentationPts).Count;$i++){$expectedIntervals += [int64]$timing.presentationPts[$i]-[int64]$timing.presentationPts[$i-1]}
            $terminal=Test-G04F7TerminalTiming $vframes $timing
            $expected.presentationPts=@($timing.presentationPts)
            $expected.preserveSignedNonZeroPts=$true
            $expected.intervals=$expectedIntervals
            $expected.terminalFrameDuration=$terminal.expectedDuration
            $expected.terminalPresentationEnd=$terminal.expectedEnd
            $observed.presentationPts=$pts
            $observed.presentationIntervals=$intervals
            $observed.terminalFrameDuration=$terminal.observedDuration
            $observed.terminalPresentationEnd=$terminal.observedEnd
            $observed.streamTimeBase=$videoStream.time_base
            $observed.containerDuration=$inspection.data.format.duration
            $threshold.timeBase=$timing.timeBase
            $threshold.terminalFrameDurationTicks=0
            $threshold.terminalPresentationEndTicks=0
            $threshold.containerDurationToleranceSeconds=0.02
            $f7Source=@($Recipe.sourceArtifacts|Where-Object {$_.variantId -eq 'F7-vfr-offset'})|Select-Object -First 1
            $f7=Assert-G04FrameSequence (Get-G04RawVideoFrames $Case $ArtifactPath $Context "$($Case.id)-f7") (Get-G04PpmReferenceFrames $Context ([string[]]@($f7Source.fileIds))) @('red','green','blue','white','black') 20 @('red','green','blue','white','black')
            $threshold.f7MaximumMeanAbsoluteError=20
            $observed.f7Identity=$f7
            $passed=$passed -and $f7.passed -and
                ($pts -join ',') -eq ((@($timing.presentationPts) -join ',')) -and
                ($pts[0] -ne 0) -and
                (($intervals -join ',') -eq (($expectedIntervals -join ','))) -and
                ([string]$videoStream.time_base -eq [string]$timing.timeBase) -and
                $terminal.passed -and
                ([math]::Abs([double]$inspection.data.format.duration-[double]$timing.containerDurationSeconds) -le 0.02)
        }
        $paired=@($decodeEvidence|Where-Object type -eq 'audio');if($paired.Count){$audio=@($inspection.data.streams|Where-Object codec_type -eq 'audio')[0];$bytes=Get-G04RawBytes $paired[0].path;$passed=$passed -and (Assert-G04ExpectedAudioDecode $bytes $audio $Recipe $observed $threshold)}
    } elseif($kind -eq 'audio') {
        $audio=@($inspection.data.streams|Where-Object codec_type -eq 'audio')[0];$entry=@($decodeEvidence|Where-Object type -eq 'audio')[0];$bytes=Get-G04RawBytes $entry.path;$samples=[int]($bytes.Length/(2*[int]$audio.channels));$observed.decodedSamplesPerChannel=$samples;$observed.tone=(Get-G04ToneEvidence $bytes ([int]$audio.sample_rate) ([int]$audio.channels))
        $expect=Get-G04Property $Recipe 'expectedAudioDecode' @{};if($Oracle.id -eq 'O-AUDIO-LOSSLESS-BYTE-EXACT'){$expectedPcm=Get-G04LosslessExpectedPcm $Recipe $audio $Context $Case.id;$threshold.exactSamples=$expect.exactSampleCount;$threshold.pcmIdentity='full decoded s16le SHA-256 and byte equality';$observed.expectedPcmSha256=Get-G04Sha256 $expectedPcm;$observed.decodedPcmSha256=Get-G04Sha256 $bytes;$observed.decodedPcmBytes=$bytes.Length;$observed.expectedPcmBytes=$expectedPcm.Length;$passed=$passed -and ($samples -eq [int]$expect.exactSampleCount) -and ($bytes.Length -eq $expectedPcm.Length) -and ((Get-G04Sha256 $bytes) -eq (Get-G04Sha256 $expectedPcm)) -and (Test-G04ByteIdentity $bytes $expectedPcm)}else{$threshold.sampleTolerance=$expect.sampleEnvelope.tolerance;$passed=$passed -and (Assert-G04ExpectedAudioDecode $bytes $audio $Recipe $observed $threshold)}
    } elseif($kind -eq 'image') {
        $imageFrames=@($inspection.data.frames|Where-Object media_type -eq 'video');$threshold.exactFrameCount=1;$passed=$passed -and ($imageFrames.Count -eq 1);$stream=@($inspection.data.streams|Where-Object codec_type -eq 'video')[0];$source=Join-Path $Context.FixtureRoot ([string]$Recipe.sourceArtifacts[0].fileIds[0]);$alpha=([string](Get-G04Property $Recipe.sourceArtifacts[0] 'declaredFormat' '') -match 'rgba');$pixelFormat=if($alpha){'rgba'}else{'rgb24'};$bytesPerPixel=if($alpha){4}else{3};$raw=Join-Path $Context.Work "$($Case.id)-image.$pixelFormat";$cleanupArtifacts.Add($raw);Invoke-G04StrictDecode $Case $ArtifactPath $Context '0:v:0' $raw @('-frames:v','1','-f','rawvideo','-pix_fmt',$pixelFormat)|Out-Null;$actual=Get-G04RawBytes $raw
        $referenceRaw=Join-Path $Context.Work "$($Case.id)-reference.$pixelFormat";$cleanupArtifacts.Add($referenceRaw);$sourceArgs=if($source.EndsWith('.rgba')){@('-f','rawvideo','-pixel_format','rgba','-video_size','320x180','-i',$source)}else{@('-f','image2','-c:v','ppm','-i',$source)};Invoke-G04OracleCommand $Context ("reference-raster-$($Case.id)") $Context.Ffmpeg (@('-hide_banner','-xerror','-err_detect','explode')+$sourceArgs+@('-frames:v','1','-vf',"scale=$($stream.width):$($stream.height)",'-f','rawvideo','-pix_fmt',$pixelFormat,$referenceRaw)) @{purpose='deterministic image reference raster';source=$source;pixelFormat=$pixelFormat;strictErrors=@('-xerror','-err_detect','explode')} $Case.id|Out-Null;$reference=Get-G04RawBytes $referenceRaw
        $geometryPass=($actual.Length -eq ($stream.width*$stream.height*$bytesPerPixel)) -and ($reference.Length -eq $actual.Length);$observed.decodedRasterLength=$actual.Length;$observed.geometry=@{width=$stream.width;height=$stream.height;referenceLength=$reference.Length;alpha=$alpha};$threshold.singleImage=$true
        if($Oracle.id -eq 'O-IMAGE-JPEG-TOLERANCE' -or $Oracle.id -eq 'O-IMAGE-JPEG-EXIF-ORIENTATION'){$mae=Get-G04Mae $reference $actual;$sof=Get-G04SofMarker $ArtifactPath;$threshold.maximumMeanAbsoluteError=18;$threshold.jpegSof=@('0xC0','0xC2');$observed.meanAbsoluteError=$mae;$observed.jpegSof=$sof;$passed=$passed -and $geometryPass -and ($mae -le 18) -and ($sof -in $threshold.jpegSof)}else{$exact=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($actual)) -eq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($reference));$threshold.pixelIdentity='exact decoded raster SHA-256';$observed.referenceRasterSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($reference));$observed.decodedRasterSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($actual));$passed=$passed -and $geometryPass -and $exact}
    } elseif($kind -eq 'remux') { $remux=Assert-G04RemuxIdentity $Case $Recipe $ArtifactPath $Context;$observed.remux=$remux;$threshold.streamCopyOnly=$true;$passed=$passed -and $remux.passed }
    $failureReason = if($passed){$null}else{"Concrete $kind oracle failed for $($Case.id)."}
    return [ordered]@{caseId=$Case.id;recipeId=$Recipe.id;oracleId=$Oracle.id;expected=$expected;observed=$observed;threshold=$threshold;passed=$passed;failureReason=$failureReason;inspectionCommand=$inspection.record;decodeCommands=@($decodeEvidence)}
    } finally {
        foreach($path in @($cleanupArtifacts)) { Remove-G04OracleArtifact $path }
    }
}
