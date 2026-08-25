# G0.4 executable proof helpers.  This file is dot-sourced by the runner; it
# intentionally has no product-facing behavior.
Set-StrictMode -Version Latest

function Get-G04ConcreteDemuxer([string]$DisplayName) {
    $map = @{ 'mov,mp4,m4a,3gp,3g2,mj2' = 'mov'; 'matroska,webm' = 'matroska'; 'matroska' = 'matroska'; 'image2' = 'image2'; 'wav' = 'wav'; 'flac' = 'flac'; 'mp3' = 'mp3'; 'aac' = 'aac'; 'ogg' = 'ogg' }
    if (-not $map.ContainsKey($DisplayName)) { throw "No safe concrete FFmpeg demuxer mapping exists for contract display '$DisplayName'." }
    return $map[$DisplayName]
}

function Get-G04Property($Object, [string]$Name, $Default = $null) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}
function Normalize-G04Profile([string]$Profile) {
    if([string]::IsNullOrWhiteSpace($Profile)){return $null}
    $token=($Profile.ToLowerInvariant() -replace '[ _-]','')
    $map=@{'constrainedbaseline'='constrained_baseline';'baseline'='baseline';'main'='main';'high'='high';'profile0'='profile0';'aaclc'='aac_low';'lcaac'='aac_low';'aaclow'='aac_low'}
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

function Invoke-G04OracleCommand([hashtable]$Context, [string]$Name, [string]$Executable, [string[]]$Arguments, [hashtable]$Components, [string]$CaseId) {
    $record = Invoke-G04RecordedCommand -Context $Context -Name $Name -Executable $Executable -Arguments $Arguments -Components $Components
    Test-G04UndeclaredDiagnostics -Stderr ([string](Get-G04Property $record 'stderr' '')) -AllowedPatterns @()
    return $record
}

function Get-G04Probe([object]$Case, [string]$ArtifactPath, [hashtable]$Context) {
    $demuxer = Get-G04ConcreteDemuxer ([string]$Case.requiredComponents.demuxer)
    $record = Invoke-G04OracleCommand $Context ("inspect-" + $Case.id) $Context.Ffprobe @('-v','error','-f',$demuxer,'-show_format','-show_streams','-show_frames','-show_packets','-of','json',$ArtifactPath) @{ purpose='fresh content inspection'; demuxer=$demuxer; allStreams=$true; allFrames=$true; allPackets=$true } $Case.id
    $text = [string](Get-G04Property $record 'stdout' '')
    if ([string]::IsNullOrWhiteSpace($text)) { throw "Fresh ffprobe inspection returned no JSON for $($Case.id)." }
    try { return @{ record=$record; data=($text | ConvertFrom-Json) } } catch { throw "Fresh ffprobe inspection JSON is invalid for $($Case.id): $($_.Exception.Message)" }
}

function Assert-G04StreamContract([object]$Case, [object]$Probe) {
    $observed = @($Probe.streams)
    if ($observed.Count -ne @($Case.streams).Count) { throw "Stream-count oracle failed for $($Case.id): expected $(@($Case.streams).Count), observed $($observed.Count)." }
    $checks = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt @($Case.streams).Count; $i++) {
        $expected = @($Case.streams)[$i]; $actual = $observed[$i]
        $actualType = if ($expected.type -eq 'image') { 'image' } else { [string]$actual.codec_type }
        $pass = $actualType -eq [string]$expected.type -and [string]$actual.codec_name -eq [string]$expected.codec
        if ($null -ne (Get-G04Property $expected 'profile')) { $pass = $pass -and ((Normalize-G04Profile ([string]$actual.profile)) -eq (Normalize-G04Profile ([string]$expected.profile))) }
        if ($null -ne (Get-G04Property $expected 'pixelFormat')) { $pass = $pass -and ([string]$actual.pix_fmt -eq [string]$expected.pixelFormat) }
        if ($null -ne (Get-G04Property $expected 'width')) { $pass = $pass -and ([int]$actual.width -eq [int]$expected.width) }
        if ($null -ne (Get-G04Property $expected 'height')) { $pass = $pass -and ([int]$actual.height -eq [int]$expected.height) }
        if ($null -ne (Get-G04Property $expected 'sampleRate')) { $pass = $pass -and ([int]$actual.sample_rate -eq [int]$expected.sampleRate) }
        $expectedChannels = Get-G04Property $expected 'channels'
        if ($null -ne $expectedChannels) { $count = if ($expectedChannels -eq 'mono') { 1 } elseif ($expectedChannels -eq 'stereo') { 2 } else { [int]$expectedChannels }; $pass = $pass -and ([int]$actual.channels -eq $count) }
        $maximumLevel = Get-G04Property $expected 'maximumLevel'
        if ($null -ne $maximumLevel -and $null -ne (Get-G04Property $actual 'level')) { $pass = $pass -and ([int]$actual.level -le [int]([double]$maximumLevel * 10)) }
        if($expected.type -eq 'video' -and $Case.timing.kind -eq 'cfr'){$pass=$pass -and ([string]$actual.avg_frame_rate -eq [string]$Case.timing.frameRate)}
        $checks.Add([ordered]@{ streamIndex=$i; expected=$expected; observed=[ordered]@{codec=$actual.codec_name;profile=$actual.profile;normalizedProfile=(Normalize-G04Profile ([string]$actual.profile));level=$actual.level;pixelFormat=$actual.pix_fmt;width=$actual.width;height=$actual.height;averageFrameRate=$actual.avg_frame_rate;sampleRate=$actual.sample_rate;channels=$actual.channels}; passed=$pass })
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
function Get-G04Mae([byte[]]$Left,[byte[]]$Right) { if ($Left.Length -ne $Right.Length) { throw 'Image oracle byte lengths differ.' }; $sum = [Int64]0; for($i=0;$i -lt $Left.Length;$i++){ $sum += [math]::Abs([int]$Left[$i]-[int]$Right[$i]) }; return [double]$sum / $Left.Length }
function Get-G04PpmPixels([string]$Path) {
    $bytes=Get-G04RawBytes $Path;$header=[Text.Encoding]::ASCII.GetString($bytes,0,[Math]::Min($bytes.Length,128));$match=[regex]::Match($header,'^P6\s+(?:#.*\s+)?(\d+)\s+(\d+)\s+255\s+',[Text.RegularExpressions.RegexOptions]::Singleline)
    if(-not $match.Success){throw "Expected binary PPM reference raster: $Path"};$offset=$match.Length;return [ordered]@{width=[int]$match.Groups[1].Value;height=[int]$match.Groups[2].Value;pixels=$bytes[$offset..($bytes.Length-1)]}
}
function Get-G04Sha256([byte[]]$Bytes) { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)) }
function Test-G04ByteIdentity([byte[]]$Left, [byte[]]$Right) { if ($Left.Length -ne $Right.Length) { return $false }; for($i=0;$i-lt $Left.Length;$i++){if($Left[$i] -ne $Right[$i]){return $false}}; return $true }
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
        Invoke-G04StrictDecode $Case $ArtifactPath $Context '0:v:0' $raw @('-vf','scale=320:180:flags=bilinear','-f','rawvideo','-pix_fmt','rgb24') | Out-Null
        $bytes = Get-G04RawBytes $raw; $frameLength = 320 * 180 * 3
        if (($bytes.Length % $frameLength) -ne 0) { throw "Decoded video raster for $($Case.id) does not contain whole 320x180 rgb24 frames." }
        $frames = for ($offset = 0; $offset -lt $bytes.Length; $offset += $frameLength) { $frame = [byte[]]::new($frameLength); [Array]::Copy($bytes, $offset, $frame, 0, $frameLength); $frame }
        return @($frames)
    } finally { Remove-G04OracleArtifact $raw }
}
function Get-G04PpmReferenceFrames([hashtable]$Context, [string[]]$FileIds) {
    return @($FileIds | ForEach-Object { $ppm = Get-G04PpmPixels (Join-Path $Context.FixtureRoot $_); if ($ppm.width -ne 320 -or $ppm.height -ne 180 -or $ppm.pixels.Length -ne 172800) { throw "Reference PPM $_ is not 320x180 rgb24." }; [byte[]]$ppm.pixels })
}
function Assert-G04FrameSequence([byte[][]]$Frames, [byte[][]]$References, [string[]]$Names, [double]$MaximumMae, [string[]]$ExpectedSequence) {
    if ($Frames.Count -ne $ExpectedSequence.Count) { throw "Frame identity count mismatch: expected $($ExpectedSequence.Count), observed $($Frames.Count)." }
    $referenceSeparation = for ($left = 0; $left -lt $References.Count; $left++) { for ($right = $left + 1; $right -lt $References.Count; $right++) { Get-G04Mae $References[$left] $References[$right] } }
    if (@($referenceSeparation | Where-Object { $_ -le 1 }).Count) { throw 'Reference frame identities are not visually distinct.' }
    $observed = [Collections.Generic.List[object]]::new(); $passed = $true
    for ($i = 0; $i -lt $Frames.Count; $i++) {
        $maes = @($References | ForEach-Object { Get-G04Mae $Frames[$i] $_ }); $minimum = ($maes | Measure-Object -Minimum).Minimum; $actualIndex = [array]::IndexOf([double[]]$maes, [double]$minimum); $actual = $Names[$actualIndex]
        $expectedIndex = [array]::IndexOf([string[]]$Names, $ExpectedSequence[$i]); if ($expectedIndex -lt 0) { throw "Unknown expected frame identity $($ExpectedSequence[$i])." }
        $passed = $passed -and ($actual -eq $ExpectedSequence[$i]) -and ($maes[$expectedIndex] -le $MaximumMae)
        $observed.Add([ordered]@{ index=$i; expected=$ExpectedSequence[$i]; observed=$actual; maes=$maes; expectedMae=$maes[$expectedIndex] })
    }
    $distinct = @($observed | ForEach-Object { $_.observed } | Select-Object -Unique)
    $passed = $passed -and ($distinct.Count -eq $References.Count)
    return [ordered]@{ passed=$passed; frames=@($observed); distinctIdentityCount=$distinct.Count; referencePairwiseMae=@($referenceSeparation); maximumMae=$MaximumMae }
}
function Assert-G04ExpectedAudioDecode([byte[]]$Bytes, [object]$Audio, [object]$Recipe, [hashtable]$Observed, [hashtable]$Threshold) {
    $tone = Get-G04ToneEvidence $Bytes ([int]$Audio.sample_rate) ([int]$Audio.channels); $Observed.audioTone = $tone
    $expect = Get-G04Property $Recipe 'expectedAudioDecode' @{}
    if ($null -ne $expect.sampleEnvelope) { $Threshold.audioSampleTolerance=$expect.sampleEnvelope.tolerance; if ([math]::Abs($tone.samplesPerChannel-[int]$expect.sampleEnvelope.expected) -gt [int]$expect.sampleEnvelope.tolerance) { return $false } }
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
function Get-G04ComparableStreamStructure([object]$Probe) { return @($Probe.streams | ForEach-Object { [ordered]@{index=$_.index;codecType=$_.codec_type;codec=$_.codec_name;profile=(Normalize-G04Profile ([string]$_.profile));pixelFormat=$_.pix_fmt;sampleRate=$_.sample_rate;channels=$_.channels;timeBase=$_.time_base;startPts=$_.start_pts;durationTs=$_.duration_ts;disposition=$_.disposition} }) }
function Assert-G04RemuxIdentity([object]$Case, [object]$Recipe, [string]$ArtifactPath, [hashtable]$Context) {
    $sourceId = [string]$Case.fixtureProduction.sourceCaseId; if ([string]::IsNullOrWhiteSpace($sourceId) -or -not $Context.CaseById.ContainsKey($sourceId)) { throw "Remux case $($Case.id) has no resolved source case." }
    $sourceCase=$Context.CaseById[$sourceId]; $sourcePath=Get-G04ArtifactPath $Context $sourceId; $sourceProbe=Get-G04Probe $sourceCase $sourcePath $Context; $targetProbe=Get-G04Probe $Case $ArtifactPath $Context
    $sourceStructure=Get-G04ComparableStreamStructure $sourceProbe.data; $targetStructure=Get-G04ComparableStreamStructure $targetProbe.data; $streamStructureEqual=(($sourceStructure|ConvertTo-Json -Depth 10 -Compress) -eq ($targetStructure|ConvertTo-Json -Depth 10 -Compress)); $timingEqual=(([string]$sourceProbe.data.format.duration -eq [string]$targetProbe.data.format.duration) -and (($sourceStructure|ConvertTo-Json -Depth 10 -Compress) -eq ($targetStructure|ConvertTo-Json -Depth 10 -Compress)))
    $maps=Get-G04StreamMaps $sourceCase; $decoded=[Collections.Generic.List[object]]::new(); $hashesEqual=$true
    for($i=0;$i -lt $maps.Count;$i++){ $sourceRaw=Join-Path $Context.Work "$($Case.id)-source-$i.raw";$targetRaw=Join-Path $Context.Work "$($Case.id)-target-$i.raw";try{$kind=if(@($sourceCase.streams)[$i].type -eq 'audio'){'audio'}else{'video'};$output=if($kind -eq 'audio'){@('-f','s16le','-acodec','pcm_s16le')}else{@('-f','rawvideo','-pix_fmt','rgb24')};Invoke-G04StrictDecode $sourceCase $sourcePath $Context $maps[$i] $sourceRaw $output|Out-Null;Invoke-G04StrictDecode $Case $ArtifactPath $Context $maps[$i] $targetRaw $output|Out-Null;$left=Get-G04RawBytes $sourceRaw;$right=Get-G04RawBytes $targetRaw;$leftHash=Get-G04Sha256 $left;$rightHash=Get-G04Sha256 $right;$equal=($leftHash -eq $rightHash) -and (Test-G04ByteIdentity $left $right);$hashesEqual=$hashesEqual -and $equal;$decoded.Add([ordered]@{map=$maps[$i];sourceSha256=$leftHash;targetSha256=$rightHash;equal=$equal})}finally{Remove-G04OracleArtifact $sourceRaw;Remove-G04OracleArtifact $targetRaw}}
    return [ordered]@{passed=($streamStructureEqual -and $timingEqual -and $hashesEqual);sourceCaseId=$sourceId;sourceStructure=$sourceStructure;targetStructure=$targetStructure;streamStructureEqual=$streamStructureEqual;timingEqual=$timingEqual;decodedStreams=@($decoded);sourceProbe=$sourceProbe.record;targetProbe=$targetProbe.record}
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
    foreach($map in $allMaps) { $raw=Join-Path $Context.Work ("$($Case.id)-$($map.Replace(':','_')).raw"); $cleanupArtifacts.Add($raw); $selected=@($Case.streams)[$decodeEvidence.Count];$outputArgs=if($selected.type -eq 'audio'){@('-f','s16le','-acodec','pcm_s16le')}else{@('-f','rawvideo','-pix_fmt','rgb24')}; $decodeEvidence.Add([ordered]@{map=$map;type=$selected.type;path=$raw;record=(Invoke-G04StrictDecode $Case $ArtifactPath $Context $map $raw $outputArgs)}) }
    $kind=[string]$Oracle.kind; $expected=[ordered]@{case=$Case.id;oracle=$Oracle.id;recipe=$Recipe.id}; $observed=[ordered]@{streamChecks=$streamEvidence;format=$inspection.data.format;frameCount=@($inspection.data.frames).Count;decodeMaps=@($allMaps)}; $threshold=[ordered]@{}; $passed=$true
    if($kind -eq 'complete-decode') {
        $expectedFrames=Get-G04Property (Get-G04Property $Recipe 'expectedDecodedFrameCount' @{}) 'exact'
        if($null -ne $expectedFrames) { $threshold.exactFrameCount=$expectedFrames; $passed=$passed -and (@($inspection.data.frames | Where-Object media_type -eq 'video').Count -eq [int]$expectedFrames) }
        $f1Source = @($Recipe.sourceArtifacts | Where-Object { $_.variantId -eq 'F1-three-patterns' }) | Select-Object -First 1
        if ($null -ne $f1Source) { $f1References=Get-G04PpmReferenceFrames $Context ([string[]]@($f1Source.fileIds));$frameCount=@($inspection.data.frames|Where-Object media_type -eq 'video').Count;$cycle=for($i=0;$i-lt $frameCount;$i++){@('f1-pattern-000','f1-pattern-001','f1-pattern-002')[$i % 3]};$f1=Assert-G04FrameSequence (Get-G04RawVideoFrames $Case $ArtifactPath $Context "$($Case.id)-f1") $f1References @('f1-pattern-000','f1-pattern-001','f1-pattern-002') 20 $cycle;$threshold.f1MaximumMeanAbsoluteError=20;$threshold.f1DistinctIdentities=3;$observed.f1Identity=$f1;$passed=$passed -and $f1.passed }
        if($Oracle.id -match 'F7') { $timing=$Recipe.presentationTiming; $vframes=@($inspection.data.frames|Where-Object media_type -eq 'video'); $videoStream=@($inspection.data.streams|Where-Object codec_type -eq 'video')[0]; $pts=@($vframes|ForEach-Object {[int64]$_.pts});$intervals=@();for($i=1;$i -lt $pts.Count;$i++){$intervals += $pts[$i]-$pts[$i-1]}; $expected.presentationPts=@($timing.presentationPts);$expected.preserveSignedNonZeroPts=$true;$expected.intervals=@($Oracle.timing.intervals);$observed.presentationPts=$pts;$observed.presentationIntervals=$intervals;$observed.streamTimeBase=$videoStream.time_base;$observed.containerDuration=$inspection.data.format.duration;$threshold.timeBase=$timing.timeBase;$threshold.terminalFrameDuration=$timing.terminalFrameDuration;$f7Source=@($Recipe.sourceArtifacts|Where-Object {$_.variantId -eq 'F7-vfr-offset'})|Select-Object -First 1;$f7=Assert-G04FrameSequence (Get-G04RawVideoFrames $Case $ArtifactPath $Context "$($Case.id)-f7") (Get-G04PpmReferenceFrames $Context ([string[]]@($f7Source.fileIds))) @('red','green','blue','white','black') 20 @('red','green','blue','white','black');$threshold.f7MaximumMeanAbsoluteError=20;$observed.f7Identity=$f7;$passed=$passed -and $f7.passed -and ($pts -join ',') -eq ((@($timing.presentationPts) -join ',')) -and ($pts[0] -ne 0) -and (($intervals -join ',') -eq ((@($Oracle.timing.intervals) -join ','))) -and ([string]$videoStream.time_base -eq [string]$timing.timeBase) -and ([double]$inspection.data.format.duration -eq [double]$timing.containerDurationSeconds) }
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
    if(-not $passed){throw "Concrete $kind oracle failed for $($Case.id)."}
    return [ordered]@{caseId=$Case.id;recipeId=$Recipe.id;oracleId=$Oracle.id;expected=$expected;observed=$observed;threshold=$threshold;passed=$passed;inspectionCommand=$inspection.record;decodeCommands=@($decodeEvidence)}
    } finally {
        foreach($path in @($cleanupArtifacts)) { Remove-G04OracleArtifact $path }
    }
}
