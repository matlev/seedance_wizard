[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$IncludeLongForm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This runner is deliberately operational proof infrastructure.  It selects a
# concrete component for every media operation and records the command before
# it runs.  It neither selects a shipping runtime nor a public delivery format.
function Require-OutsideRepositoryEmptyDirectory([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'OutputDirectory must be an explicit rooted path outside the repository.' }
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full.Equals($repo, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith("$repo\", [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory must be outside the repository.'
    }
    if (Test-Path -LiteralPath $full) {
        if (-not (Test-Path -LiteralPath $full -PathType Container) -or (Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1)) {
            throw 'OutputDirectory must be new or empty so evidence cannot include stale files.'
        }
    } else { New-Item -ItemType Directory -Path $full | Out-Null }
    return $full
}

function Require-Tool([string]$Path, [string]$Name, [string]$Root) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name must be an existing explicit rooted path." }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    if (-not $resolved.StartsWith("$resolvedRoot\", [StringComparison]::OrdinalIgnoreCase)) { throw "$Name must resolve beneath RuntimeRoot. PATH fallback is prohibited." }
    return $resolved
}

function Invoke-Tool([string]$Tool, [string[]]$Arguments, [string]$Step) {
    $command = @($Tool) + $Arguments
    $stdoutFile = Join-Path $work "logs\$Step.stdout.txt"; $stderrFile = Join-Path $work "logs\$Step.stderr.txt"
    & $Tool @Arguments 1> $stdoutFile 2> $stderrFile
    $exit = $LASTEXITCODE
    $record = [ordered]@{ step=$Step; executable=$Tool; arguments=$Arguments; exitCode=$exit; stdout=(Get-Content $stdoutFile -Raw); stderr=(Get-Content $stderrFile -Raw) }
    $commands.Add($record)
    if ($exit -ne 0) { throw "Step '$Step' failed with exit code $exit. See '$stderrFile'." }
    return $record
}

function New-AtomicMedia([string]$Name, [string[]]$Arguments) {
    $final = Join-Path $media "$Name"
    $partial = "$final.partial"
    Invoke-Tool $ffmpeg ($Arguments + @('-y', $partial)) "encode-$Name" | Out-Null
    if (-not (Test-Path -LiteralPath $partial -PathType Leaf) -or (Get-Item -LiteralPath $partial).Length -le 0) { throw "Atomic output '$Name' was not created or is empty." }
    Move-Item -LiteralPath $partial -Destination $final
    if ((Get-Item -LiteralPath $final).Length -le 0) { throw "Atomic output '$Name' is empty after rename." }
    return $final
}

function Probe([string]$File, [string]$Step) {
    $record = Invoke-Tool $ffprobe @('-v','error','-of','json','-show_format','-show_streams','-show_frames','-show_packets',$File) "probe-$Step"
    $json = [string]$record.stdout
    if ([string]::IsNullOrWhiteSpace($json)) { throw "ffprobe produced no JSON for '$Step'." }
    [IO.File]::WriteAllText((Join-Path $work "probes\$Step.json"), $json, [Text.UTF8Encoding]::new($false))
    return ($json | ConvertFrom-Json)
}

function DecodeRaw([string]$File, [string]$Selector, [string]$Kind, [string]$Step) {
    $out = Join-Path $work "decoded\$Step.raw"
    if ($Kind -eq 'video') {
        Invoke-Tool $ffmpeg @('-v','error','-f','matroska','-c:v','ffv1','-i',$File,'-map',$Selector,'-vf','format=rgb24','-fps_mode','passthrough','-c:v','rawvideo','-f','rawvideo','-y',$out) "decode-$Step" | Out-Null
    } else {
        Invoke-Tool $ffmpeg @('-v','error','-f','matroska','-c:a','flac','-i',$File,'-map',$Selector,'-c:a','pcm_s16le','-f','s16le','-y',$out) "decode-$Step" | Out-Null
    }
    if (-not (Test-Path -LiteralPath $out -PathType Leaf) -or (Get-Item $out).Length -eq 0) { throw "Decode-again output '$Step' is empty." }
    return $out
}

function PpmPixels([string[]]$Files) {
    $memory = [IO.MemoryStream]::new()
    foreach ($file in $Files) {
        $bytes = [IO.File]::ReadAllBytes($file); $offset = [Text.Encoding]::ASCII.GetString($bytes).IndexOf("255`n") + 4
        $memory.Write($bytes, $offset, $bytes.Length - $offset)
    }
    return $memory.ToArray()
}

function Assert-Bytes([byte[]]$Expected, [string]$ActualPath, [string]$Description) {
    $actual = [IO.File]::ReadAllBytes($ActualPath)
    if ($Expected.Length -ne $actual.Length -or -not [Linq.Enumerable]::SequenceEqual[byte]($Expected, $actual)) { throw "Decoded $Description does not match the authored source bytes." }
}

function Assert-Stream([object]$Stream, [int]$ExpectedIndex, [string]$ExpectedType, [string]$ExpectedCodec, [string]$ExpectedTimeBase, [string]$Label) {
    if ($null -eq $Stream -or $Stream.index -ne $ExpectedIndex -or $Stream.codec_type -ne $ExpectedType -or $Stream.codec_name -ne $ExpectedCodec -or $Stream.time_base -ne $ExpectedTimeBase) {
        throw "$Label stream contract does not match its authored index/type/codec/time-base expectation."
    }
}

function Get-FramePts([object]$Probe, [string]$MediaType, [int]$StreamIndex) {
    return @($Probe.packets_and_frames | Where-Object { $_.type -eq 'frame' -and $_.media_type -eq $MediaType -and $_.stream_index -eq $StreamIndex } | ForEach-Object { [int64]$_.pts })
}

function Assert-ExactPts([int64[]]$Actual, [int64[]]$Expected, [string]$Label) {
    if ($Actual.Count -ne $Expected.Count -or (($Actual -join ',') -ne ($Expected -join ','))) {
        throw "$Label presentation timestamps do not match the authored expectation. Actual: $($Actual -join ', '). Expected: $($Expected -join ', ')."
    }
}

function Add-FixtureProof([string]$Fixture, [string]$Status, [string]$Summary, [object]$Details) {
    $fixtureProofs.Add([ordered]@{ fixtureId=$Fixture; status=$Status; summary=$Summary; executedFixtureProof=$Status -eq 'passed'; details=$Details })
}

$output = Require-OutsideRepositoryEmptyDirectory $OutputDirectory
$semanticContractPath = Join-Path $PSScriptRoot 'semantic-proof-contract.json'
if (-not (Test-Path -LiteralPath $semanticContractPath -PathType Leaf)) { throw 'The approved semantic-proof contract is required.' }
$semanticContract = Get-Content -LiteralPath $semanticContractPath -Raw | ConvertFrom-Json
if (-not [IO.Path]::IsPathRooted($RuntimeRoot) -or -not (Test-Path -LiteralPath $RuntimeRoot -PathType Container)) { throw 'RuntimeRoot must be an existing explicit rooted directory.' }
$runtime = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$ffmpeg = Require-Tool (Join-Path $runtime 'bin\ffmpeg.exe') 'ffmpeg.exe' $runtime
$ffprobe = Require-Tool (Join-Path $runtime 'bin\ffprobe.exe') 'ffprobe.exe' $runtime
$work = Join-Path $output 'work'; $media = Join-Path $output 'media'; New-Item -ItemType Directory -Path $work, $media, (Join-Path $work 'logs'), (Join-Path $work 'probes'), (Join-Path $work 'decoded') | Out-Null
$commands = [Collections.Generic.List[object]]::new(); $fixtureProofs = [Collections.Generic.List[object]]::new()
$inspectionReadiness = [ordered]@{
    schemaVersion = 1
    readinessId = 'Media.Inspect.StructureAndTiming'
    status = 'not-run'
    executedInspectionProof = $false
    fixtureIds = @('F1', 'F7', 'F8')
    summary = 'Inspection readiness was not completed.'
}

# Identity is a separate prerequisite, not a semantic proof. It executes the strict paired-runtime validator.
$identity = Join-Path $output 'runtime-identity.json'
& (Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1') -RuntimeRoot $runtime -EvidencePath $identity
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $identity)) { throw 'Approved paired runtime identity validation failed; no semantic proof was run.' }

$fixtures = Join-Path $output 'fixtures'
& (Join-Path $PSScriptRoot 'Generate-Fixtures.ps1') -FfmpegPath $ffmpeg -FfprobePath $ffprobe -ApprovedRuntimeRoot $runtime -OutputDirectory $fixtures
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed.' }

try {
    # F1: byte-exact video/audio lossless round trip, plus authored structural/timing proof.
    $f1 = New-AtomicMedia 'F1.mkv' @('-hide_banner','-f','image2','-c:v','ppm','-framerate','25','-i',(Join-Path $fixtures 'F1\f1-pattern-%03d.ppm'),'-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','2','-i',(Join-Path $fixtures 'F1\f1-sync-440hz-880hz-48000-stereo.pcm'),'-map','0:v:0','-map','1:a:0','-filter:v','format=rgb24','-c:v','ffv1','-c:a','flac','-f','matroska')
    $p = Probe $f1 'F1'; $f1Streams = @($p.streams)
    if ($f1Streams.Count -ne 2) { throw 'F1 structural stream proof failed.' }
    Assert-Stream $f1Streams[0] 0 'video' 'ffv1' '1/1000' 'F1'
    Assert-Stream $f1Streams[1] 1 'audio' 'flac' '1/1000' 'F1'
    if ($f1Streams[0].r_frame_rate -ne '25/1' -or $f1Streams[1].sample_rate -ne '48000' -or $f1Streams[1].channels -ne 2) { throw 'F1 authored cadence/audio properties were not preserved.' }
    $f1VideoPts = Get-FramePts $p 'video' 0; Assert-ExactPts $f1VideoPts ([int64[]](0, 40, 80)) 'F1 video'
    $f1AudioPts = Get-FramePts $p 'audio' 1; Assert-ExactPts $f1AudioPts ([int64[]](0, 96)) 'F1 audio'
    if ([math]::Abs(([double]$p.format.duration * 1000) - 120) -gt 0.001) { throw 'F1 authored 120 ms duration was not preserved.' }
    $v = DecodeRaw $f1 '0:v:0' 'video' 'F1-video'; Assert-Bytes (PpmPixels @(Get-ChildItem (Join-Path $fixtures 'F1\f1-pattern-*.ppm') | Sort Name | % FullName)) $v 'F1 video'
    $a = DecodeRaw $f1 '0:a:0' 'audio' 'F1-audio'; Assert-Bytes ([IO.File]::ReadAllBytes((Join-Path $fixtures 'F1\f1-sync-440hz-880hz-48000-stereo.pcm'))) $a 'F1 audio'
    $f1Inspection = [ordered]@{ streamOrder = @('0:video:ffv1', '1:audio:flac'); timeBases = @('1/1000', '1/1000'); videoPresentationTimestamps = $f1VideoPts; audioPresentationTimestamps = $f1AudioPts; durationMilliseconds = 120 }
    Add-FixtureProof 'F1' 'passed' 'Explicit FFV1/FLAC Matroska lossless round trip and authored inspection timing passed.' @{output=$f1; frameCount=3; durationSeconds=$p.format.duration; inspection=$f1Inspection}

    # F2: run both mismatched sources through explicit normalization filters and inspect the target properties.
    $f2Normalization = [Collections.Generic.List[object]]::new()
    foreach ($source in @(@('landscape','f2-landscape-640x360-25fps.ppm','f2-44100-mono-330hz.pcm','25'), @('portrait','f2-portrait-360x640-30000_1001fps.ppm','f2-48000-stereo-660hz.pcm','30000/1001'))) {
        $f2 = New-AtomicMedia ("F2-$($source[0]).mkv") @('-hide_banner','-loop','1','-framerate',$source[3],'-f','image2','-c:v','ppm','-i',(Join-Path $fixtures "F2\$($source[1])"),'-f','s16le','-c:a','pcm_s16le','-ar',($(if($source[0] -eq 'landscape'){'44100'}else{'48000'})),'-ac',($(if($source[0] -eq 'landscape'){'1'}else{'2'})),'-i',(Join-Path $fixtures "F2\$($source[2])"),'-t','0.25','-map','0:v:0','-map','1:a:0','-filter:v','scale=640:360,pad=640:360:0:0,format=yuv420p,fps=25','-filter:a','aresample=48000,aformat=channel_layouts=stereo','-c:v','ffv1','-c:a','flac','-f','matroska')
        $p = Probe $f2 "F2-$($source[0])"; $vs=@($p.streams | ? codec_type -eq 'video')[0]; $as=@($p.streams | ? codec_type -eq 'audio')[0]
        if ($vs.width -ne 640 -or $vs.height -ne 360 -or $vs.avg_frame_rate -ne '25/1' -or $as.sample_rate -ne '48000' -or $as.channels -ne 2) { throw "F2 $($source[0]) normalization inspection failed." }
        $f2Normalization.Add([ordered]@{ profile=$source[0]; inputFrameRate=$source[3]; outputFrameRate=$vs.avg_frame_rate; outputWidth=$vs.width; outputHeight=$vs.height; outputAudioSampleRate=$as.sample_rate; outputAudioChannels=$as.channels })
    }
    Add-FixtureProof 'F2' 'passed' 'The 25 fps landscape and 30000/1001 portrait source profiles normalized to an inspected 25 fps target with explicit scale/pad/format/fps/aresample/aformat filters.' @{normalization=$f2Normalization}

    Add-FixtureProof 'F3' 'blocked' 'Unicode text fixture proof is blocked: the required separately licensed, hash-pinned Unicode test font is absent; no system font was substituted.' @{prerequisite='Font.Licensed.UnicodeTestFont'}

    # F4: explicit WAV and FLAC outputs for every authored PCM variant, decoded again to source bytes.
    foreach ($variant in @(@('f4-mono-32000-1000hz.pcm','32000','1'),@('f4-mono-44100-1000hz.pcm','44100','1'),@('f4-stereo-48000-1000hz-opposed.pcm','48000','2'))) {
        foreach ($kind in @(@('wav','pcm_s16le','wav'),@('flac','flac','flac'))) {
            $target=New-AtomicMedia ("F4-$($variant[0]).$($kind[0])") @('-hide_banner','-f','s16le','-c:a','pcm_s16le','-ar',$variant[1],'-ac',$variant[2],'-i',(Join-Path $fixtures "F4\$($variant[0])"),'-map','0:a:0','-filter:a',"aformat=sample_rates=$($variant[1]):channel_layouts=$($(if($variant[2] -eq '1'){'mono'}else{'stereo'}))",'-c:a',$kind[1],'-f',$kind[2])
            $decoded=Join-Path $work "decoded\F4-$($variant[0]).$($kind[0]).pcm"; Invoke-Tool $ffmpeg @('-v','error','-f',$kind[2],'-c:a',$kind[1],'-i',$target,'-map','0:a:0','-c:a','pcm_s16le','-f','s16le','-y',$decoded) "decode-F4-$($variant[0])-$($kind[0])" | Out-Null; Assert-Bytes ([IO.File]::ReadAllBytes((Join-Path $fixtures "F4\$($variant[0])"))) $decoded "F4 $($kind[0])"
        }
    }
    Add-FixtureProof 'F4' 'passed' 'All 32/44.1/48 kHz WAV and FLAC lossless outputs decoded byte-exactly, including the opposed-phase stereo source.' @{}

    # F5: distinct absence and digital-silence cases.
    $noAudio=New-AtomicMedia 'F5-no-audio.mkv' @('-hide_banner','-loop','1','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F5\f5-silent-yellow.ppm'),'-t','0.25','-map','0:v:0','-filter:v','format=rgb24','-c:v','ffv1','-an','-f','matroska')
    $p=Probe $noAudio 'F5-no-audio'; if (@($p.streams | ? codec_type -eq 'audio').Count -ne 0) { throw 'F5 no-audio output has an audio stream.' }
    $silence=New-AtomicMedia 'F5-digital-silence.mkv' @('-hide_banner','-loop','1','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F5\f5-silent-yellow.ppm'),'-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','1','-i',(Join-Path $fixtures 'F5\f5-digital-silence-48000-mono.pcm'),'-t','0.25','-map','0:v:0','-map','1:a:0','-filter:v','format=rgb24','-filter:a','aformat=sample_rates=48000:channel_layouts=mono','-c:v','ffv1','-c:a','flac','-f','matroska')
    $a=DecodeRaw $silence '0:a:0' 'audio' 'F5-silence'; if ([IO.File]::ReadAllBytes($a) | ? { $_ -ne 0 } | Select -First 1) { throw 'F5 digital-silence output is not zero-valued PCM.' }; Add-FixtureProof 'F5' 'passed' 'No-audio and digital-silence forms are structurally distinct.' @{}

    # F6 is intentionally opt-in because it materializes a one-hour proof artifact.
    if ($IncludeLongForm) {
        $f6=New-AtomicMedia 'F6-one-hour.mkv' @('-hide_banner','-stream_loop','29999','-f','matroska','-c:v','ffv1','-c:a','flac','-i',$f1,'-t','3600','-map','0:v:0','-map','0:a:0','-filter_complex','[0:v]setpts=PTS-STARTPTS[v0];[0:a]asetpts=PTS-STARTPTS[a0];[v0][a0]concat=n=1:v=1:a=1[v][a]','-map','[v]','-map','[a]','-c:v','ffv1','-c:a','flac','-f','matroska')
        $p=Probe $f6 'F6'; if ([math]::Abs([double]$p.format.duration-3600) -gt 0.12) { throw 'F6 duration is not one hour.' }; Add-FixtureProof 'F6' 'observed' 'One-hour fixture artifact was produced, but this duration-only observation is not a Project.LongForm.Integrity capability verdict.' @{durationSeconds=$p.format.duration; capabilityVerdict='not-reported'}
    } else { Add-FixtureProof 'F6' 'not-run' 'Long-form fixture run is opt-in (-IncludeLongForm); no Project.LongForm.Integrity capability verdict is reported.' @{reason='Expensive proof explicitly omitted'} }

    # F7: concat-demuxer VFR packaging, preserve a non-zero timestamp offset, then inspect decoded presentation order by first pixel.
    $list=Join-Path $work 'F7-concat.txt'; @("file '$($fixtures.Replace('\','/') + '/F7/f7-red.ppm')'","duration 0.04","file '$($fixtures.Replace('\','/') + '/F7/f7-green.ppm')'","duration 0.08","file '$($fixtures.Replace('\','/') + '/F7/f7-blue.ppm')'","duration 0.01","file '$($fixtures.Replace('\','/') + '/F7/f7-white.ppm')'","duration 0.07","file '$($fixtures.Replace('\','/') + '/F7/f7-black.ppm')'","duration 0.04") | Set-Content -LiteralPath $list -NoNewline:$false
    $f7=New-AtomicMedia 'F7-vfr-nonzero-pts.mkv' @('-hide_banner','-copyts','-itsoffset','1','-f','concat','-safe','0','-c:v','ppm','-i',$list,'-map','0:v:0','-vf','settb=1/90000,setpts=if(eq(N\,0)\,90000\,if(eq(N\,1)\,93600\,if(eq(N\,2)\,100800\,if(eq(N\,3)\,101700\,108000)))),select=not(mod(n\,1)),format=rgb24','-fps_mode','passthrough','-enc_time_base:v','1/90000','-frames:v','5','-c:v','ffv1','-f','matroska')
    $p=Probe $f7 'F7'; $f7Streams = @($p.streams); if ($f7Streams.Count -ne 1 -or $f7Streams[0].index -ne 0 -or $f7Streams[0].codec_type -ne 'video' -or $f7Streams[0].codec_name -ne 'ffv1') { throw 'F7 structural stream proof failed.' }; $frames=@($p.packets_and_frames | ? { $_.type -eq 'frame' -and $_.media_type -eq 'video' }); [int64[]]$actualPts=@($frames | % { [int64][math]::Round([double]$_.pts_time * 90000) }); if ($frames.Count -ne 5 -or (($actualPts -join ',') -ne '90000,93600,100800,101700,108000')) { throw 'F7 did not preserve the authored non-zero VFR presentation timestamps.' }
    $f7PresentationFiles = @('f7-red.ppm','f7-green.ppm','f7-blue.ppm','f7-white.ppm','f7-black.ppm' | ForEach-Object { Join-Path $fixtures "F7\$_" })
    $decoded=DecodeRaw $f7 '0:v:0' 'video' 'F7-video'; Assert-Bytes (PpmPixels $f7PresentationFiles) $decoded 'F7 presentation-order video'; $f7Inspection = [ordered]@{ streamOrder = @('0:video:ffv1'); containerTimeBase = $f7Streams[0].time_base; authoredPresentationTickBase = '1/90000'; authoredPresentationTimestamps = $actualPts; frameCount = $frames.Count }; Add-FixtureProof 'F7' 'passed' 'Explicit concat demuxer, setpts/select filters, non-zero PTS, VFR frame durations, and byte-exact presentation-order frame identity passed.' @{firstPresentationTimestamp=$frames[0].pts_time; frameCount=$frames.Count; inspection=$f7Inspection}

    # F8: four independent inputs map into two distinguishable video and two audio streams; each output selector is then decoded and compared.
    $f8=New-AtomicMedia 'F8-multistream.mkv' @('-hide_banner','-loop','1','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F8\f8-video-zero-red.ppm'),'-loop','1','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F8\f8-video-one-green.ppm'),'-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','1','-i',(Join-Path $fixtures 'F8\f8-audio-zero-440hz.pcm'),'-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','1','-i',(Join-Path $fixtures 'F8\f8-audio-one-880hz.pcm'),'-t','0.25','-map','0:v:0','-map','1:v:0','-map','2:a:0','-map','3:a:0','-filter:v:0','format=rgb24','-filter:v:1','format=rgb24','-filter:a:0','aformat=sample_rates=48000:channel_layouts=mono','-filter:a:1','aformat=sample_rates=48000:channel_layouts=mono','-c:v','ffv1','-c:a','flac','-f','matroska')
    $p=Probe $f8 'F8'; $f8Streams = @($p.streams); if ($f8Streams.Count -ne 4) { throw 'F8 did not contain exactly four streams.' }
    Assert-Stream $f8Streams[0] 0 'video' 'ffv1' '1/1000' 'F8'
    Assert-Stream $f8Streams[1] 1 'video' 'ffv1' '1/1000' 'F8'
    Assert-Stream $f8Streams[2] 2 'audio' 'flac' '1/1000' 'F8'
    Assert-Stream $f8Streams[3] 3 'audio' 'flac' '1/1000' 'F8'
    if ($f8Streams[0].width -ne 320 -or $f8Streams[0].height -ne 180 -or $f8Streams[1].width -ne 160 -or $f8Streams[1].height -ne 90 -or $f8Streams[2].sample_rate -ne '48000' -or $f8Streams[2].channels -ne 1 -or $f8Streams[3].sample_rate -ne '48000' -or $f8Streams[3].channels -ne 1) { throw 'F8 authored multistream structure was not preserved.' }
    $f8VideoZeroPts = Get-FramePts $p 'video' 0; $f8VideoOnePts = Get-FramePts $p 'video' 1; $f8AudioZeroPts = Get-FramePts $p 'audio' 2; $f8AudioOnePts = Get-FramePts $p 'audio' 3
    Assert-ExactPts $f8VideoZeroPts ([int64[]](0, 40, 80, 120, 160, 200)) 'F8 video stream 0'; Assert-ExactPts $f8VideoOnePts ([int64[]](0, 40, 80, 120, 160, 200)) 'F8 video stream 1'; Assert-ExactPts $f8AudioZeroPts ([int64[]](0, 96, 192)) 'F8 audio stream 0'; Assert-ExactPts $f8AudioOnePts ([int64[]](0, 96, 192)) 'F8 audio stream 1'
    if ([math]::Abs(([double]$p.format.duration * 1000) - 250) -gt 0.001) { throw 'F8 authored 250 ms duration was not preserved.' }
    Assert-Bytes (PpmPixels @((1..6 | ForEach-Object { Join-Path $fixtures 'F8\f8-video-zero-red.ppm' }))) (DecodeRaw $f8 '0:v:0' 'video' 'F8-v0') 'F8 video stream 0'; Assert-Bytes (PpmPixels @((1..6 | ForEach-Object { Join-Path $fixtures 'F8\f8-video-one-green.ppm' }))) (DecodeRaw $f8 '0:v:1' 'video' 'F8-v1') 'F8 video stream 1'
    Assert-Bytes ([IO.File]::ReadAllBytes((Join-Path $fixtures 'F8\f8-audio-zero-440hz.pcm'))) (DecodeRaw $f8 '0:a:0' 'audio' 'F8-a0') 'F8 audio stream 0'; Assert-Bytes ([IO.File]::ReadAllBytes((Join-Path $fixtures 'F8\f8-audio-one-880hz.pcm'))) (DecodeRaw $f8 '0:a:1' 'audio' 'F8-a1') 'F8 audio stream 1'; $f8Inspection = [ordered]@{ streamOrder = @('0:video:ffv1', '1:video:ffv1', '2:audio:flac', '3:audio:flac'); timeBases = @('1/1000', '1/1000', '1/1000', '1/1000'); videoZeroPresentationTimestamps = $f8VideoZeroPts; videoOnePresentationTimestamps = $f8VideoOnePts; audioZeroPresentationTimestamps = $f8AudioZeroPts; audioOnePresentationTimestamps = $f8AudioOnePts; durationMilliseconds = 250; selectors = @('0:v:0', '0:v:1', '0:a:0', '0:a:1') }; Add-FixtureProof 'F8' 'passed' 'Explicit four-input maps and all four packaged stream selectors decoded to their distinguishable authored sources with authored structure/timing evidence.' @{inspection=$f8Inspection}
    $inspectionReadiness = [ordered]@{ schemaVersion = 1; readinessId = 'Media.Inspect.StructureAndTiming'; status = 'passed'; executedInspectionProof = $true; fixtureIds = @('F1', 'F7', 'F8'); summary = 'Dedicated F1/F7/F8 structure, time-base, presentation-timing, and selector-readiness proof passed; it is not itself a semantic capability verdict.'; acceptance = @('F1 stream order, codecs, 1/1000 time bases, 0/40/80 video PTS, 0/96 audio PTS, and 120 ms duration', 'F7 exact non-zero VFR presentation ticks and presentation order; Matroska container time base is recorded separately', 'F8 four-stream order, codecs, 1/1000 time bases, exact video/audio PTS, 250 ms duration, and distinguishable stream selectors'); fixtures = [ordered]@{ F1 = $f1Inspection; F7 = $f7Inspection; F8 = $f8Inspection } }
}
catch { $inspectionReadiness.status = 'failed'; $inspectionReadiness.executedInspectionProof = $false; $inspectionReadiness.summary = "Inspection readiness did not complete: $($_.Exception.Message)"; Add-FixtureProof 'runner' 'failed' $_.Exception.Message @{}; throw }
finally {
    $evidence=[ordered]@{schemaVersion=1; profileId='P2.BtbnLgplShared.WindowsX64.20260820'; contractProfileId=$semanticContract.profileId; declaredCapabilityIds=@($semanticContract.capabilities | ForEach-Object id); runtimeIdentityEvidence='runtime-identity.json'; componentPresence='See runtime-identity.json; presence is not a capability verdict.'; deliveryNote='WebM VP9/Opus is an approved open-delivery proof candidate only; this runner does not select a final ReelForge default.'; fixtureProofs=$fixtureProofs; inspectionReadiness=$inspectionReadiness; capabilityVerdicts=@(); commands=$commands}
    [IO.File]::WriteAllText((Join-Path $output 'semantic-proof-evidence.json'),($evidence|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
}
