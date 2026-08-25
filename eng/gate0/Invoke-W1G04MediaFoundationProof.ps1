[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$FixtureRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Optional Windows-only Gate 0 evidence. This never establishes the portable
# baseline, project meaning, a shipping runtime, or a licensing conclusion.
function Assert-RootedDirectory([string]$Path, [string]$Name) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Name must be an existing explicit rooted directory." }
    return (Resolve-Path -LiteralPath $Path).Path
}
function Assert-NewOutsideRepositoryDirectory([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'OutputDirectory must be an explicit rooted path outside the repository.' }
    $repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\','/')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    if ($full.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith("$repository\", [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be outside the repository.' }
    $ancestor=$full
    while(-not (Test-Path -LiteralPath $ancestor)) { $parent=[IO.Path]::GetDirectoryName($ancestor); if([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $ancestor){break}; $ancestor=$parent }
    while(Test-Path -LiteralPath $ancestor) { if(((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'OutputDirectory must not be beneath a reparse point.'}; $parent=[IO.Path]::GetDirectoryName($ancestor);if([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $ancestor){break};$ancestor=$parent }
    if (Test-Path -LiteralPath $full) {
        if (-not (Test-Path -LiteralPath $full -PathType Container) -or (Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1)) { throw 'OutputDirectory must be new or empty so evidence cannot include stale files.' }
    } else { New-Item -ItemType Directory -Path $full | Out-Null }
    if (((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'OutputDirectory must not be a reparse point.' }
    return $full
}
function Assert-Tool([string]$Path, [string]$Name, [string]$Root) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name must be an existing explicit rooted path." }
    $resolved = (Resolve-Path -LiteralPath $Path).Path; $root = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\','/')
    if (-not $resolved.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase)) { throw "$Name must resolve beneath RuntimeRoot. PATH fallback is prohibited." }
    return $resolved
}
function Normalize-RelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) { throw 'Fixture inventory path is unsafe.' }
    $result = $Path.Replace('\\','/'); if ($result.Split('/') | Where-Object { $_ -in @('','.', '..') }) { throw 'Fixture inventory path is unsafe.' }; return $result
}
function Assert-FixtureFile([string]$Relative) {
    $normalized = Normalize-RelativePath $Relative; $candidate = [IO.Path]::GetFullPath((Join-Path $script:fixtures ($normalized.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    if (-not $candidate.StartsWith("$($script:fixtures)$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Approved fixture input is missing or escapes FixtureRoot: $normalized" }
    $current=$script:fixtures
    if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'FixtureRoot must not be a reparse point.' }
    foreach($segment in $normalized.Split('/')) { $current=Join-Path $current $segment; $item=Get-Item -LiteralPath $current -Force; if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Approved fixture input contains a reparse point: $normalized"} }
    return $candidate
}
function Assert-FixtureReport() {
    $reportPath = Join-Path $script:fixtures 'generated-fixture-report.json'; $inventoryPath = Join-Path $PSScriptRoot 'fixture-source-inventory.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf) -or -not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) { throw 'FixtureRoot and checked-in fixture-source-inventory.json are required.' }
    try { $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json; $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json } catch { throw 'Fixture report is truncated or invalid JSON.' }
    $hash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($report.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820' -or $report.externalMediaCommandsExecuted -or $report.approvedInventory.path -ne 'eng/gate0/fixture-source-inventory.json' -or $report.approvedInventory.sha256 -ne $hash) { throw 'Fixture report approved inventory does not match the checked-in inventory.' }
    $expected = @{}; foreach ($entry in @($inventory.files)) { $key=Normalize-RelativePath ([string]$entry.path); if($expected.ContainsKey($key)){throw "Fixture inventory duplicates $key."}; $expected[$key] = $entry }
    $reported = @{}; foreach ($entry in @($report.sourceFiles)) { $key=Normalize-RelativePath ([string]$entry.path); if($reported.ContainsKey($key)){throw "Fixture report duplicates $key."}; $reported[$key] = $entry }
    if($expected.Count -ne $reported.Count -or @($expected.Keys|Where-Object{-not $reported.ContainsKey($_)}).Count -ne 0 -or @($reported.Keys|Where-Object{-not $expected.ContainsKey($_)}).Count -ne 0){throw 'Fixture report file set does not exactly match the checked-in inventory.'}
    foreach ($required in $expected.Keys) {
        $entry=$expected[$required]; $reportedEntry=$reported[$required]
        if($reportedEntry.length -ne $entry.length -or $reportedEntry.sha256 -ne $entry.sha256){throw "Fixture report hash or length mismatch: $required"}
        $path = Assert-FixtureFile $required; $actual = Get-Item -LiteralPath $path
        if ($actual.Length -ne [int64]$entry.length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant() -ne $entry.sha256) { throw "Fixture report hash or length mismatch: $required" }
    }
    return [ordered]@{ reportPath=$reportPath; reportSha256=(Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToUpperInvariant(); inventoryPath='eng/gate0/fixture-source-inventory.json'; inventorySha256=$hash }
}
function Invoke-Recorded([string]$Name, [string]$Executable, [string[]]$Arguments, [hashtable]$Components) {
    $stdout = Join-Path $script:work "$Name.stdout.txt"; $stderr = Join-Path $script:work "$Name.stderr.txt"
    & $Executable @Arguments 1> $stdout 2> $stderr; $exit = $LASTEXITCODE
    $record = [ordered]@{ name=$Name; executable=$Executable; arguments=$Arguments; components=$Components; exitCode=$exit; stdout=(Get-Content -LiteralPath $stdout -Raw); stderr=(Get-Content -LiteralPath $stderr -Raw) }
    $script:commands.Add($record); if ($exit -ne 0) { throw "Command '$Name' failed with exit code $exit." }; return $record
}
function Move-Atomic([string]$Partial, [string]$Final) {
    if (-not (Test-Path -LiteralPath $Partial -PathType Leaf) -or (Get-Item -LiteralPath $Partial).Length -le 0) { throw "Atomic output was not created or is empty: $Partial" }
    Move-Item -LiteralPath $Partial -Destination $Final; return Get-Artifact $Final
}
function Get-Artifact([string]$Path) { $item = Get-Item -LiteralPath $Path; return [ordered]@{ path=[IO.Path]::GetRelativePath($script:output,$item.FullName).Replace('\','/'); length=$item.Length; sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant() } }
function Invoke-Encode([string]$Name, [string[]]$Arguments, [hashtable]$Components) {
    $final = Join-Path $script:media $Name; $partial = "$final.partial"; Invoke-Recorded "encode-$Name" $script:ffmpeg ($Arguments + @('-y',$partial)) $Components | Out-Null; return Move-Atomic $partial $final
}
function Probe([string]$Name, [string]$Path) { $record = Invoke-Recorded "probe-$Name" $script:ffprobe @('-v','error','-print_format','json','-show_format','-show_streams','-show_frames',$Path) @{ demuxer='mov,mp4,m4a,3gp,3g2,mj2'; streamSelectors=@('0:v:0','0:a:0') }; return ($record.stdout | ConvertFrom-Json) }
function Decode([string]$Name, [string]$Path, [string]$Type, [string]$Decoder, [string]$PixelOrFormat) {
    $extension = if ($Type -eq 'video') { 'rgb' } else { 'pcm' }; $partial = Join-Path $script:work "$Name.partial.$extension"; $selector = if ($Type -eq 'video') {'0:v:0'} else {'0:a:0'}
    $args = if ($Type -eq 'video') { @('-v','error','-c:v',$Decoder,'-i',$Path,'-map',$selector,'-c:v','rawvideo','-pix_fmt',$PixelOrFormat,'-f','rawvideo',$partial) } else { @('-v','error','-c:a',$Decoder,'-i',$Path,'-map',$selector,'-c:a','pcm_s16le','-f','s16le',$partial) }
    Invoke-Recorded "decode-$Name" $script:ffmpeg $args @{ decoder=$Decoder; encoder=if($Type -eq 'video'){'rawvideo'}else{'pcm_s16le'}; muxer=if($Type -eq 'video'){'rawvideo'}else{'s16le'}; streamSelector=$selector } | Out-Null
    $final = Join-Path $script:media "$Name.$extension"; Move-Atomic $partial $final | Out-Null; return $final
}
function Get-PpmPayload([string]$Path) { $bytes=[IO.File]::ReadAllBytes($Path); $lines=0; $offset=0; while($lines -lt 3 -and $offset -lt $bytes.Length){if($bytes[$offset++] -eq 10){$lines++}}; if($lines -ne 3){throw 'Invalid PPM fixture header.'}; return $bytes[$offset..($bytes.Length-1)] }
function Get-Mae([byte[]]$Expected,[byte[]]$Actual) { if($Expected.Length -ne $Actual.Length){throw "Video identity/order oracle failed: expected $($Expected.Length) bytes, observed $($Actual.Length)."}; [double]$sum=0; for($i=0;$i -lt $Expected.Length;$i++){$sum += [math]::Abs([int]$Expected[$i]-[int]$Actual[$i])}; return $sum/$Expected.Length }
function Assert-FrameOrder([string]$Path, [byte[][]]$Sources) {
    $bytes=[IO.File]::ReadAllBytes($Path);$frameBytes=320*180*3;if($bytes.Length -ne 3*$frameBytes){throw 'Video identity/order oracle failed: decoded output does not contain three RGB frames.'};$matrix=[Collections.Generic.List[object]]::new()
    for($frame=0;$frame -lt 3;$frame++){ $actual=[byte[]]::new($frameBytes);[Buffer]::BlockCopy($bytes,$frame*$frameBytes,$actual,0,$frameBytes);$row=[Collections.Generic.List[double]]::new();for($source=0;$source -lt 3;$source++){$row.Add((Get-Mae $Sources[$source] $actual))};$expected=$row[$frame];for($source=0;$source -lt 3;$source++){if($source -ne $frame -and $row[$source] -le $expected){throw "Video identity/order oracle failed for frame $frame against source $source."}};if($expected -gt 18){throw "Video identity/order oracle failed for frame $frame."};$matrix.Add([ordered]@{decodedFrame=$frame;maeBySourceFrame=$row;matchedSourceFrame=$frame;matchedMae=$expected}) }
    return [ordered]@{threshold=18;frames=$matrix;assertion='Each decoded frame matches its corresponding authored source at MAE <= 18 and strictly better than every nonmatching source.'}
}
function Get-ToneMagnitude([byte[]]$Bytes,[int]$Channel,[double]$Frequency) { $samples=[int]($Bytes.Length/4); [double]$real=0;[double]$imag=0; for($i=0;$i -lt $samples;$i++){ $sample=[BitConverter]::ToInt16($Bytes, (($i*2+$Channel)*2)); $angle=2*[math]::PI*$Frequency*$i/48000; $real += $sample*[math]::Cos($angle); $imag -= $sample*[math]::Sin($angle) }; return [math]::Sqrt($real*$real+$imag*$imag) }
function Assert-AudioTone([string]$Path) { $bytes=[IO.File]::ReadAllBytes($Path);$samples=[int]($bytes.Length/4);$expected=5760;$tolerance=1024;if([math]::Abs($samples-$expected) -gt $tolerance){throw "Audio timing/tone oracle failed: expected $expected samples per channel +/- $tolerance, observed $samples."}; $left440=Get-ToneMagnitude $bytes 0 440; $left880=Get-ToneMagnitude $bytes 0 880; $right880=Get-ToneMagnitude $bytes 1 880; $right440=Get-ToneMagnitude $bytes 1 440; if($left440 -le ($left880*1.2) -or $right880 -le ($right440*1.2)){throw 'Audio timing/tone oracle failed: expected channel tones are not stronger than declared comparisons.'}; return [ordered]@{ decodedBytes=$bytes.Length;decodedSamplesPerChannel=$samples;expectedSamplesPerChannel=$expected;primingPaddingToleranceSamples=$tolerance;expectedDurationSeconds=0.12;decodedDurationSeconds=($samples/48000.0); left440=$left440; left880=$left880; right880=$right880; right440=$right440; assertion='AAC decoded sample count is within declared priming/padding tolerance and 440 Hz left / 880 Hz right are stronger than declared comparison frequencies' } }
function Assert-MoovBeforeMdat([string]$Path) { $bytes=[IO.File]::ReadAllBytes($Path); $moov=-1;$mdat=-1; for($i=4;$i -lt $bytes.Length-3;$i++){ $tag=[Text.Encoding]::ASCII.GetString($bytes,$i,4); if($tag -eq 'moov' -and $moov -lt 0){$moov=$i}; if($tag -eq 'mdat' -and $mdat -lt 0){$mdat=$i} }; if($moov -lt 0 -or $mdat -lt 0 -or $moov -ge $mdat){throw 'MP4 faststart oracle failed: moov is not before mdat.'}; return [ordered]@{moovOffset=$moov;mdatOffset=$mdat} }
function Get-EnvironmentInventory() {
    $result=[ordered]@{}; foreach($entry in @(@{name='windows';class='Win32_OperatingSystem';properties=@('Caption','Version','BuildNumber','OSArchitecture')},@{name='cpu';class='Win32_Processor';properties=@('Name','Manufacturer','NumberOfCores','NumberOfLogicalProcessors')},@{name='gpu';class='Win32_VideoController';properties=@('Name','DriverVersion','PNPDeviceID','AdapterRAM')})) {
        try { $result[$entry.name]=@(Get-CimInstance $entry.class -ErrorAction Stop | Select-Object -Property $entry.properties); } catch { $result[$entry.name]=[ordered]@{status='unavailable';reason=$_.Exception.Message} }
    }; return $result
}
function Get-WindowsIdentity() { try { Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop | Select-Object ProductName,DisplayVersion,CurrentBuild,CurrentBuildNumber,UBR } catch { [ordered]@{status='unavailable';reason=$_.Exception.Message} } }
function Write-PreflightFailureEvidence([string]$Message) {
    $failure=[ordered]@{capabilityId='W1.G0.4.Preflight';status='environment-dependent';executedSemanticProof=$false;classification='preflight-failed';error=$Message;commands=@($script:commands)}
    $body=[ordered]@{schemaVersion=1;proofProfileId='W1.G0.4.MediaFoundation.WindowsOnly';optionalWindowsEvidence=$true;portableBaseline=$false;shippingConclusion=$false;statement='Optional Windows-only technical evidence; preflight failure does not affect portable P2 proof.';windowsIdentity=Get-WindowsIdentity;commands=$script:commands;capabilities=@($failure);artifacts=@();disclaimer='Preflight/component failure is recorded before termination. This is not a shipping or portability conclusion.'}
    [IO.File]::WriteAllText((Join-Path $script:output 'w1-g0.4-mediafoundation-proof.json'),($body|ConvertTo-Json -Depth 14),[Text.UTF8Encoding]::new($false))
}

if ($env:OS -ne 'Windows_NT') { throw 'W1 is Windows-only and cannot run on this operating system.' }
$script:output=Assert-NewOutsideRepositoryDirectory $OutputDirectory; $script:fixtures=Assert-RootedDirectory $FixtureRoot 'FixtureRoot'; $runtime=Assert-RootedDirectory $RuntimeRoot 'RuntimeRoot'
$script:work=Join-Path $script:output 'work'; $script:media=Join-Path $script:output 'media'; New-Item -ItemType Directory -Path $script:work,$script:media | Out-Null
$script:commands=[Collections.Generic.List[object]]::new(); $script:artifacts=[Collections.Generic.List[object]]::new()
try {
    $script:ffmpeg=Assert-Tool (Join-Path $runtime 'bin\ffmpeg.exe') 'ffmpeg.exe' $runtime; $script:ffprobe=Assert-Tool (Join-Path $runtime 'bin\ffprobe.exe') 'ffprobe.exe' $runtime
    $validation=Join-Path $script:output 'p2-runtime-validation.json'; & (Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1') -RuntimeRoot $runtime -EvidencePath $validation; if($LASTEXITCODE -ne 0){throw 'Approved P2 runtime validation failed; W1 proof was not run.'}
    $fixtureEvidence=Assert-FixtureReport; $f0=Assert-FixtureFile 'F1/f1-pattern-000.ppm'; $f1=Assert-FixtureFile 'F1/f1-pattern-001.ppm'; $f2=Assert-FixtureFile 'F1/f1-pattern-002.ppm'; $audio=Assert-FixtureFile 'F1/f1-sync-440hz-880hz-48000-stereo.pcm'
    Invoke-Recorded 'ffmpeg-version' $script:ffmpeg @('-version') @{purpose='runtime identity'} | Out-Null; Invoke-Recorded 'ffmpeg-buildconf' $script:ffmpeg @('-buildconf') @{purpose='runtime identity'} | Out-Null
    $h264MfHelp = Invoke-Recorded 'h264-mf-help' $script:ffmpeg @('-hide_banner','-h','encoder=h264_mf') @{encoder='h264_mf';purpose='wrapper initialization/options'}
    if (([string]$h264MfHelp.stdout + [string]$h264MfHelp.stderr) -notmatch 'H264 via MediaFoundation') { throw 'The h264_mf wrapper initialization log did not identify the Media Foundation encoder wrapper.' }
    Invoke-Recorded 'native-aac-help' $script:ffmpeg @('-hide_banner','-h','encoder=aac') @{encoder='aac';purpose='wrapper initialization/options'} | Out-Null
} catch { Write-PreflightFailureEvidence $_.Exception.Message; throw }

$base=@('-hide_banner','-f','image2','-framerate','25','-c:v','ppm','-i',(Join-Path $script:fixtures 'F1\f1-pattern-%03d.ppm'),'-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','2','-i',$audio,'-map','0:v:0','-map','1:a:0','-vf','format=yuv420p','-c:v','h264_mf','-pix_fmt','yuv420p','-hw_encoding','false','-rate_control','cbr','-b:v','2M','-g','25','-c:a','aac','-profile:a','aac_low','-b:a','192k','-ar','48000','-ac','2','-frames:v','3','-movflags','+faststart','-f','mp4')
$results=[Collections.Generic.List[object]]::new()
foreach($case in @(@{id='W1.Video.Export.Mp4H264Aac.MediaFoundation';name='w1-h264-aac.mp4';audio=$true},@{id='W1.Video.Export.Mp4H264VideoOnly.MediaFoundation';name='w1-h264-video-only.mp4';audio=$false})) {
    $start=$script:commands.Count
    try {
        $args=if($case.audio){$base}else{@('-hide_banner','-f','image2','-framerate','25','-c:v','ppm','-i',(Join-Path $script:fixtures 'F1\f1-pattern-%03d.ppm'),'-map','0:v:0','-an','-vf','format=yuv420p','-c:v','h264_mf','-pix_fmt','yuv420p','-hw_encoding','false','-rate_control','cbr','-b:v','2M','-g','25','-frames:v','3','-movflags','+faststart','-f','mp4')}
        $selectedAudioEncoder = if ($case.audio) { 'aac' } else { $null }
        $selectedMaps = if ($case.audio) { @('0:v:0','1:a:0') } else { @('0:v:0') }
        $inputDemuxers=if($case.audio){@('image2','s16le')}else{@('image2')};$inputDecoders=if($case.audio){@('ppm','pcm_s16le')}else{@('ppm')}
        $artifact=Invoke-Encode $case.name $args @{inputDemuxers=$inputDemuxers;decoders=$inputDecoders;filters=@('format=yuv420p');videoEncoder='h264_mf';audioEncoder=$selectedAudioEncoder;muxer='mp4';maps=$selectedMaps;hardwareEncodingNotForced=$true;implementation='not observable from this proof';rateControl='cbr';videoBitrate='2M';gop=25}
        $script:artifacts.Add($artifact); $path=Join-Path $script:media $case.name; $probe=Probe $case.name $path; $streams=@($probe.streams); $video=@($streams|Where-Object codec_type -eq 'video');$aud=@($streams|Where-Object codec_type -eq 'audio')
        if($video.Count -ne 1 -or $video[0].codec_name -ne 'h264' -or $video[0].width -ne 320 -or $video[0].height -ne 180 -or $video[0].pix_fmt -ne 'yuv420p' -or $video[0].avg_frame_rate -ne '25/1' -or [int]$video[0].nb_frames -ne 3 -or $video[0].profile -notmatch 'Constrained Baseline' -or [int]$video[0].level -ne 20){throw 'Video stream-layout/profile oracle failed.'}; if($case.audio -and ($aud.Count -ne 1 -or $aud[0].codec_name -ne 'aac' -or $aud[0].profile -ne 'LC' -or [int]$aud[0].sample_rate -ne 48000 -or [int]$aud[0].channels -ne 2)){throw 'Audio stream-layout/profile oracle failed.'}; if(-not $case.audio -and $aud.Count -ne 0){throw 'Video-only MP4 contains audio despite explicit -an.'}
        $moov=Assert-MoovBeforeMdat $path; $rgb=Decode "$($case.name)-video" $path 'video' 'h264' 'rgb24'; $frameOrder=Assert-FrameOrder $rgb @([byte[]](Get-PpmPayload $f0),[byte[]](Get-PpmPayload $f1),[byte[]](Get-PpmPayload $f2))
        $tone=$null;if($case.audio){$pcm=Decode "$($case.name)-audio" $path 'audio' 'aac' ''; $tone=Assert-AudioTone $pcm}
        $selectedAudioDecoder = if ($case.audio) { 'aac' } else { $null }
        $results.Add([ordered]@{capabilityId=$case.id;status='passed';executedSemanticProof=$true;commands=@($script:commands|Select-Object -Skip $start);details=[ordered]@{artifact=$artifact;structural=[ordered]@{format=$probe.format.format_name;streamCount=$streams.Count;streams=$streams};requestedSettings=[ordered]@{videoFilter='format=yuv420p';pixelFormat='yuv420p';hardwareEncodingNotForced=$true;rateControl='cbr';videoBitrate='2M';gop=25;audioProfile=if($case.audio){'aac_low'}else{$null};audioBitrate=if($case.audio){'192k'}else{$null}};observedSettings=[ordered]@{videoProfile=$video[0].profile;videoLevel=$video[0].level;pixelFormat=$video[0].pix_fmt;frameRate=$video[0].avg_frame_rate;audioProfile=if($case.audio){$aud[0].profile}else{$null}};moovBeforeMdat=$moov;videoIdentityOrder=$frameOrder;audioTimingTone=$tone;components=[ordered]@{videoEncoder='h264_mf';audioEncoder=$selectedAudioEncoder;videoDecoder='h264';audioDecoder=$selectedAudioDecoder;muxer='mp4';runtimeProfile='P2.BtbnLgplShared.WindowsX64.20260820';dependencyIdentity='P2 validated binary closure; Windows Media Foundation/MFT implementation identity is limited to recorded OS and wrapper evidence'}}})
    } catch { $results.Add([ordered]@{capabilityId=$case.id;status='environment-dependent';executedSemanticProof=$false;classification='execution-failed';error=$_.Exception.Message;commands=@($script:commands|Select-Object -Skip $start)}) }
}
$evidence=[ordered]@{schemaVersion=1;proofProfileId='W1.G0.4.MediaFoundation.WindowsOnly';optionalWindowsEvidence=$true;portableBaseline=$false;shippingConclusion=$false;generatedAtUtc=[DateTimeOffset]::UtcNow;statement='Optional Windows-only technical evidence. A result neither proves portable project meaning nor approves shipping, licensing, patent, distribution, or independent playback.';runtimeIdentityEvidence=(Get-Artifact $validation);fixtureReportVerified=$true;fixtureEvidence=$fixtureEvidence;windowsIdentity=Get-WindowsIdentity;environment=Get-EnvironmentInventory;commands=$script:commands;capabilities=$results;artifacts=$script:artifacts;disclaimer='-hw_encoding false means hardware encoding is not forced; this proof does not claim to observe the selected implementation. The P2 validated binary closure and recorded Windows/wrapper evidence are the available dependency identity; Media Foundation/MFT version identity remains limited. The native FFmpeg AAC encoder is deliberately the same route proposed for P2; the historical MF-specific AAC route is not used.'}
$evidencePath=Join-Path $script:output 'w1-g0.4-mediafoundation-proof.json'; [IO.File]::WriteAllText($evidencePath,($evidence|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false)); Write-Output "W1 G0.4 proof evidence: $evidencePath"
if(@($results|Where-Object status -ne 'passed').Count -ne 0){ exit 1 }
