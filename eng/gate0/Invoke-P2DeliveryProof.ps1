[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$FixtureRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This is proof infrastructure only. WebM VP9/Opus is an open-delivery proof
# candidate, never a shipping/default delivery decision.
function Require-OutsideRepositoryEmptyDirectory([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'OutputDirectory must be an explicit rooted path outside the repository.' }
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full.Equals($repo, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith("$repo\", [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be outside the repository.' }
    if (Test-Path -LiteralPath $full) {
        if (-not (Test-Path -LiteralPath $full -PathType Container) -or (Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1)) { throw 'OutputDirectory must be new or empty so evidence cannot include stale files.' }
    } else { New-Item -ItemType Directory -Path $full | Out-Null }
    return $full
}

function Require-Tool([string]$Path, [string]$Name, [string]$Root) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name must be an existing explicit rooted path. PATH fallback is prohibited." }
    $resolved = (Resolve-Path -LiteralPath $Path).Path; $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    if (-not $resolved.StartsWith("$resolvedRoot\", [StringComparison]::OrdinalIgnoreCase)) { throw "$Name must resolve beneath RuntimeRoot. PATH fallback is prohibited." }
    return $resolved
}

function Get-SafeFixtureRelativePath([string]$Root, [string]$RelativePath, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains('\') -or $RelativePath -eq '..' -or $RelativePath.StartsWith('../', [StringComparison]::Ordinal) -or $RelativePath.Contains('/../')) { throw "$Description contains an unsafe path '$RelativePath'." }
    $full=[IO.Path]::GetFullPath((Join-Path $Root $RelativePath.Replace('/',[IO.Path]::DirectorySeparatorChar)))
    $rootFull=(Resolve-Path -LiteralPath $Root).Path.TrimEnd('\','/'); if(-not $full.StartsWith("$rootFull\",[StringComparison]::OrdinalIgnoreCase)){throw "$Description escapes FixtureRoot: '$RelativePath'."}
    return $full
}

function Assert-FixtureHashes([string]$Root) {
    if (-not [IO.Path]::IsPathRooted($Root) -or -not (Test-Path -LiteralPath $Root -PathType Container)) { throw 'FixtureRoot must be an existing explicit rooted directory produced by Generate-Fixtures.ps1.' }
    $resolvedRoot=(Resolve-Path -LiteralPath $Root).Path
    if(((Get-Item -LiteralPath $resolvedRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'FixtureRoot must not be a reparse point.'}
    $reportPath = Join-Path $resolvedRoot 'generated-fixture-report.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'FixtureRoot is missing generated-fixture-report.json; fixture provenance cannot be verified.' }
    try { $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -AsHashtable -ErrorAction Stop } catch { throw "Fixture report is truncated or invalid JSON: $($_.Exception.Message)" }
    if ($report.externalMediaCommandsExecuted -ne $false) { throw 'Fixture report is invalid: source primitive generation must not execute media commands.' }
    $inventoryPath=Join-Path $PSScriptRoot 'fixture-source-inventory.json'; $inventory=Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json -AsHashtable
    if($inventory.schemaVersion -ne 1 -or $inventory.inventoryVersion -ne 1 -or $inventory.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820'){throw 'Checked-in fixture source inventory schema or profile is invalid.'}
    if($null -eq $report.approvedInventory -or $report.approvedInventory.schemaVersion -ne $inventory.schemaVersion -or $report.approvedInventory.inventoryVersion -ne $inventory.inventoryVersion -or $report.approvedInventory.path -ne 'eng/gate0/fixture-source-inventory.json' -or $report.approvedInventory.sha256 -ne (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToUpperInvariant()){throw 'Fixture report approved inventory does not match the checked-in fixture-source-inventory.json.'}
    $expected=@{}; foreach($entry in $inventory.files){$path=[string]$entry.path; Get-SafeFixtureRelativePath $resolvedRoot $path 'Checked-in fixture inventory' | Out-Null; if($expected.ContainsKey($path) -or $entry.length -lt 0 -or ([string]$entry.sha256) -notmatch '^[A-F0-9]{64}$'){throw "Checked-in fixture inventory is invalid for '$path'."};$expected[$path]=$entry}
    $reported=@{}; foreach($entry in $report.sourceFiles){$path=[string]$entry.path; Get-SafeFixtureRelativePath $resolvedRoot $path 'Fixture report'; if($reported.ContainsKey($path) -or $entry.length -lt 0 -or ([string]$entry.sha256) -notmatch '^[A-F0-9]{64}$'){throw "Fixture report source inventory is invalid for '$path'."};$reported[$path]=$entry}
    $actual=@{}; foreach($item in @(Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force)){
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "FixtureRoot contains a reparse-point file '$($item.FullName)'."}; $relative=[IO.Path]::GetRelativePath($resolvedRoot,$item.FullName).Replace('\','/'); if($relative -eq 'generated-fixture-report.json'){continue}; Get-SafeFixtureRelativePath $resolvedRoot $relative 'Fixture output' | Out-Null; $actual[$relative]=[ordered]@{length=$item.Length;sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant()}
    }
    foreach($item in @(Get-ChildItem -LiteralPath $resolvedRoot -Directory -Recurse -Force)){if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "FixtureRoot contains a reparse-point directory '$($item.FullName)'."}}
    foreach($name in @($expected.Keys+$reported.Keys+$actual.Keys | Select-Object -Unique)){if(-not $expected.ContainsKey($name) -or -not $reported.ContainsKey($name) -or -not $actual.ContainsKey($name)){throw "Fixture source file set does not exactly match the approved inventory: '$name'."}; foreach($candidate in @($reported[$name],$actual[$name])){if([int64]$candidate.length -ne [int64]$expected[$name].length -or ([string]$candidate.sha256).ToUpperInvariant() -ne ([string]$expected[$name].sha256).ToUpperInvariant()){throw "Fixture report hash or length mismatch: $name"}}}
    foreach($consumed in @('F1/f1-pattern-000.ppm','F1/f1-pattern-001.ppm','F1/f1-pattern-002.ppm','F1/f1-sync-440hz-880hz-48000-stereo.pcm','F2/f2-portrait-360x640-30000_1001fps.ppm','F2/f2-48000-stereo-660hz.pcm','F2/f2-landscape-640x360-25fps.ppm','F2/f2-44100-mono-330hz.pcm','F4/f4-stereo-48000-1000hz-opposed.pcm')){if(-not $expected.ContainsKey($consumed)){throw "Consumed delivery input is absent from the approved fixture inventory: $consumed"}}
    return $report
}

function Invoke-Tool([string]$Tool, [string[]]$Arguments, [string]$Step) {
    $stdoutFile = Join-Path $work "logs\$Step.stdout.txt"; $stderrFile = Join-Path $work "logs\$Step.stderr.txt"
    & $Tool @Arguments 1> $stdoutFile 2> $stderrFile; $exit = $LASTEXITCODE
    $record = [ordered]@{ step=$Step; executable=$Tool; arguments=$Arguments; exitCode=$exit; stdout=(Get-Content $stdoutFile -Raw); stderr=(Get-Content $stderrFile -Raw) }
    $commands.Add($record)
    if ($exit -ne 0) { throw "Step '$Step' failed with exit code $exit. See '$stderrFile'." }
    return $record
}

function New-AtomicMedia([string]$Name, [string[]]$Arguments) {
    $final = Join-Path $media $Name; $partial = "$final.partial"
    Invoke-Tool $ffmpeg ($Arguments + @('-y', $partial)) "encode-$Name" | Out-Null
    if (-not (Test-Path -LiteralPath $partial -PathType Leaf) -or (Get-Item -LiteralPath $partial).Length -le 0) { throw "Atomic output '$Name' was not created or is empty." }
    Move-Item -LiteralPath $partial -Destination $final
    return $final
}

function Probe([string]$File, [string]$Step) {
    $record = Invoke-Tool $ffprobe @('-v','error','-of','json','-show_format','-show_streams',$File) "probe-$Step"
    if ([string]::IsNullOrWhiteSpace([string]$record.stdout)) { throw "ffprobe produced no JSON for '$Step'." }
    [IO.File]::WriteAllText((Join-Path $work "probes\$Step.json"), [string]$record.stdout, [Text.UTF8Encoding]::new($false))
    return ([string]$record.stdout | ConvertFrom-Json)
}

function Decode([string]$File, [string]$Kind, [string]$Decoder, [string]$Muxer, [string]$Selector, [string]$Step) {
    $out = Join-Path $work "decoded\$Step.raw"
    if ($Kind -eq 'video') {
        Invoke-Tool $ffmpeg @('-v','error','-f',$Muxer,'-c:v',$Decoder,'-i',$File,'-map',$Selector,'-vf','format=rgb24','-c:v','rawvideo','-f','rawvideo','-y',$out) "decode-$Step" | Out-Null
    } else {
        Invoke-Tool $ffmpeg @('-v','error','-f',$Muxer,'-c:a',$Decoder,'-i',$File,'-map',$Selector,'-c:a','pcm_s16le','-f','s16le','-y',$out) "decode-$Step" | Out-Null
    }
    if (-not (Test-Path -LiteralPath $out -PathType Leaf) -or (Get-Item -LiteralPath $out).Length -eq 0) { throw "Decode-again output '$Step' is empty." }
    return $out
}

function PpmPixels([string[]]$Files) {
    $memory = [IO.MemoryStream]::new()
    foreach ($file in $Files) { $bytes=[IO.File]::ReadAllBytes($file); $offset=[Text.Encoding]::ASCII.GetString($bytes).IndexOf("255`n") + 4; $memory.Write($bytes,$offset,$bytes.Length-$offset) }
    return $memory.ToArray()
}

function MeanAbsoluteError([byte[]]$Expected, [byte[]]$Actual) {
    if ($Expected.Length -ne $Actual.Length) { throw 'Visual oracle is invalid: decoded frame length differs from the authored frame length.' }
    [double]$total=0; for($i=0;$i -lt $Expected.Length;$i++) { $total += [Math]::Abs([int]$Expected[$i]-[int]$Actual[$i]) }; return $total / $Expected.Length
}

function Get-ToneMagnitude([byte[]]$Bytes, [int]$SampleRate, [int]$Channels, [int]$FrequencyHz) {
    $samples=[int]($Bytes.Length/(2*$Channels)); [double]$sine=0; [double]$cosine=0
    for($i=0;$i -lt $samples;$i++){ $sample=[BitConverter]::ToInt16($Bytes,($i*$Channels)*2); $angle=2*[Math]::PI*$FrequencyHz*$i/$SampleRate; $sine += $sample*[Math]::Sin($angle); $cosine += $sample*[Math]::Cos($angle) }
    return [Math]::Sqrt($sine*$sine+$cosine*$cosine)
}

function Assert-ExpectedToneAgainstComparisons([string]$PcmPath, [int]$SampleRate, [int]$Channels, [int]$ExpectedHz) {
    $bytes=[IO.File]::ReadAllBytes($PcmPath); if($bytes.Length -eq 0 -or $bytes.Length%(2*$Channels) -ne 0){throw 'Audio oracle is invalid: decoded PCM is empty or misaligned.'}
    $candidates=@(500,750,$ExpectedHz,1250,1500); $magnitudes=[ordered]@{}; foreach($candidate in $candidates){$magnitudes[[string]$candidate]=Get-ToneMagnitude $bytes $SampleRate $Channels $candidate}; $expectedMagnitude=[double]$magnitudes[[string]$ExpectedHz]; $competitor=($magnitudes.GetEnumerator() | Where-Object Key -ne ([string]$ExpectedHz) | ForEach-Object Value | Measure-Object -Maximum).Maximum
    if($expectedMagnitude -le [double]$competitor){throw "Frequency-comparison oracle failed: $ExpectedHz Hz is not stronger than the declared comparison frequencies."}
    return [ordered]@{candidateFrequenciesHz=$candidates; magnitudes=$magnitudes; expectedFrequencyHz=$ExpectedHz; expectedMagnitude=$expectedMagnitude; strongestCompetitorMagnitude=$competitor}
}

function Get-OptionalProbeProperty([object]$Object, [string]$Name) {
    $property=$Object.PSObject.Properties[$Name]; if($null -eq $property){return $null}; return $property.Value
}

function Assert-WebmVp9Opus([object]$ProbeResult, [string]$Description) {
    $streams=@($ProbeResult.streams)
    if($streams.Count -ne 2 -or $streams[0].index -ne 0 -or $streams[0].codec_type -ne 'video' -or $streams[0].codec_name -ne 'vp9' -or $streams[1].index -ne 1 -or $streams[1].codec_type -ne 'audio' -or $streams[1].codec_name -ne 'opus'){throw "$Description stream-layout oracle failed: expected exactly video VP9 stream 0 followed by audio Opus stream 1."}
    if ($ProbeResult.format.format_name -notmatch 'webm') { throw "$Description did not inspect as WebM." }
    return [ordered]@{video=$streams[0];audio=$streams[1];inspectedStreamMap=@($streams | ForEach-Object {[ordered]@{index=$_.index;type=$_.codec_type;codec=$_.codec_name;sampleRate=Get-OptionalProbeProperty $_ 'sample_rate';channels=Get-OptionalProbeProperty $_ 'channels';width=Get-OptionalProbeProperty $_ 'width';height=Get-OptionalProbeProperty $_ 'height'}})}
}

function Assert-ProxyAspectAndPadding([string]$RawPath, [int]$Width, [int]$Height) {
    $raw=[IO.File]::ReadAllBytes($RawPath); if($raw.Length -lt $Width*$Height*3){throw 'Draft-proxy aspect oracle is invalid: decoded frame is incomplete.'}
    $row=[int]($Height/2); $active=@()
    for($x=0;$x -lt $Width;$x++) {
        $offset=($row*$Width+$x)*3; $distance=[Math]::Abs([int]$raw[$offset]-0)+[Math]::Abs([int]$raw[$offset+1]-128)+[Math]::Abs([int]$raw[$offset+2]-255)
        if($distance -le 48){$active += $x}
    }
    if($active.Count -eq 0){throw 'Draft-proxy aspect oracle failed: no active portrait content was found.'}
    $activeWidth=$active[-1]-$active[0]+1; $expectedWidth=[Math]::Round($Height*360/640)
    # Integer scaler rounding may distribute up to four pixels unevenly across the two pad regions.
    if([Math]::Abs($activeWidth-$expectedWidth) -gt 2 -or [Math]::Abs($active[0]-($Width-$active[-1]-1)) -gt 4){throw 'Draft-proxy aspect/padding oracle failed.'}
    $leftStart=($row*$Width)*3; $rightStart=(($row*$Width+$Width-1)*3)
    $left=$raw[$leftStart..($leftStart+2)]; $right=$raw[$rightStart..($rightStart+2)]
    if(($left | Measure-Object -Sum).Sum -gt 30 -or ($right | Measure-Object -Sum).Sum -gt 30){throw 'Draft-proxy padding oracle failed: expected black side padding is absent.'}
}

function Slice-Bytes([byte[]]$Bytes, [int]$Offset, [int]$Length) {
    $slice=New-Object byte[] $Length; [Buffer]::BlockCopy($Bytes,$Offset,$slice,0,$Length); return $slice
}

function Write-Capability([object]$Capability, [string]$Status, [string]$Summary, [object]$Details, [object]$SelectedComponents=$null) {
    if($null -eq $SelectedComponents){$SelectedComponents=$Capability.components}
    $proof=[ordered]@{ schemaVersion=1; capabilityId=$Capability.id; status=$Status; executedSemanticProof=($Status -eq 'passed'); oracleVerdict=if($Status -eq 'passed'){'valid'}else{'invalid-or-blocked'}; deliveryNote='WebM VP9/Opus is an open-delivery proof candidate only, not the final ReelForge default.'; selectedComponents=$SelectedComponents; summary=$Summary; details=$Details }
    $proofs.Add($proof); $file=($Capability.id -replace '[^A-Za-z0-9._-]','_')+'.json'; [IO.File]::WriteAllText((Join-Path $capabilityDirectory $file),($proof|ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))
}

$output=Require-OutsideRepositoryEmptyDirectory $OutputDirectory
if (-not [IO.Path]::IsPathRooted($RuntimeRoot) -or -not (Test-Path -LiteralPath $RuntimeRoot -PathType Container)) { throw 'RuntimeRoot must be an existing explicit rooted directory. PATH fallback is prohibited.' }
$runtime=(Resolve-Path -LiteralPath $RuntimeRoot).Path
$work=Join-Path $output 'work'; $media=Join-Path $output 'media'; $capabilityDirectory=Join-Path $output 'capabilities'
New-Item -ItemType Directory -Path $work,$media,$capabilityDirectory,(Join-Path $work 'logs'),(Join-Path $work 'probes'),(Join-Path $work 'decoded') | Out-Null
$commands=[Collections.Generic.List[object]]::new(); $proofs=[Collections.Generic.List[object]]::new()
$activeCapability=$null
$contract=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'semantic-proof-contract.json') -Raw | ConvertFrom-Json

# Identity precedes all delivery work. The validator proves the exact approved P2 pair, not a semantic capability.
$identityPath=Join-Path $output 'runtime-identity.json'
& (Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1') -RuntimeRoot $runtime -EvidencePath $identityPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $identityPath)) { throw 'Approved paired runtime identity validation failed; no delivery proof was run.' }
$fixtureReport=Assert-FixtureHashes $FixtureRoot
$fixtureReportHash=(Get-FileHash -LiteralPath (Join-Path $FixtureRoot 'generated-fixture-report.json') -Algorithm SHA256).Hash.ToUpperInvariant()
$fixtures=(Resolve-Path -LiteralPath $FixtureRoot).Path
$ffmpeg=Require-Tool (Join-Path $runtime 'bin\ffmpeg.exe') 'ffmpeg.exe' $runtime; $ffprobe=Require-Tool (Join-Path $runtime 'bin\ffprobe.exe') 'ffprobe.exe' $runtime

try {
    # Create an explicitly decoded F1 Matroska selected-media source, then deliver it separately.
    $f1Source=New-AtomicMedia 'f1-source.mkv' @('-hide_banner','-f','image2','-c:v','ppm','-framerate','25','-i',(Join-Path $fixtures 'F1\f1-pattern-%03d.ppm'),'-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','2','-i',(Join-Path $fixtures 'F1\f1-sync-440hz-880hz-48000-stereo.pcm'),'-map','0:v:0','-map','1:a:0','-filter:v','format=rgb24','-filter:a','aformat=sample_rates=48000:channel_layouts=stereo','-c:v','ffv1','-c:a','flac','-f','matroska')

    $cap=$contract.capabilities | Where-Object id -eq 'Preview.GenerateDraftProxy'; $activeCapability=$cap
    $proxy=New-AtomicMedia 'draft-proxy.webm' @('-hide_banner','-loop','1','-f','image2','-c:v','ppm','-framerate','30000/1001','-i',(Join-Path $fixtures 'F2\f2-portrait-360x640-30000_1001fps.ppm'),'-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','2','-i',(Join-Path $fixtures 'F2\f2-48000-stereo-660hz.pcm'),'-t','0.25','-map','0:v:0','-map','1:a:0','-filter:v','scale=320:180:force_original_aspect_ratio=decrease,pad=320:180:(ow-iw)/2:(oh-ih)/2,format=yuv420p,fps=15','-filter:a','aresample=48000,aformat=channel_layouts=stereo','-c:v','libvpx-vp9','-c:a','libopus','-f','webm')
    $p=Probe $proxy 'draft-proxy'; $streams=Assert-WebmVp9Opus $p 'Draft proxy'; if($streams.video.width -gt 320 -or $streams.video.height -gt 180 -or $streams.audio.sample_rate -ne '48000' -or $streams.audio.channels -ne 2 -or $streams.video.avg_frame_rate -ne '15/1' -or $streams.video.start_time -ne '0.000000' -or [Math]::Abs(([double]$p.format.duration*1000)-250) -gt 20){throw 'Draft proxy target cadence/timing oracle failed.'}; $proxyVideo=Decode $proxy 'video' 'vp9' 'matroska' '0:v:0' 'draft-proxy-video'; Decode $proxy 'audio' 'opus' 'matroska' '0:a:0' 'draft-proxy-audio' | Out-Null; Assert-ProxyAspectAndPadding $proxyVideo 320 180; Write-Capability $cap 'passed' 'Explicit VP9/Opus WebM draft proxy decoded again successfully with active-content aspect/padding and target cadence/timing evidence.' @{output=$proxy; inspectedStreamMap=$streams.inspectedStreamMap; outputFrameRate=$streams.video.avg_frame_rate; outputStartTime=$streams.video.start_time; outputDurationMilliseconds=([double]$p.format.duration*1000)}

    $cap=$contract.capabilities | Where-Object id -eq 'Video.Export.OpenDelivery.SelectedMedia'; $activeCapability=$cap
    $selected=New-AtomicMedia 'selected-media.webm' @('-hide_banner','-f','matroska','-c:v','ffv1','-c:a','flac','-i',$f1Source,'-map','0:v:0','-map','0:a:0','-filter:v','scale=320:180,format=yuv420p','-filter:a','aresample=48000,aformat=channel_layouts=stereo','-c:v','libvpx-vp9','-c:a','libopus','-f','webm')
    $p=Probe $selected 'selected-media'; $streams=Assert-WebmVp9Opus $p 'Selected-media export'; if($streams.video.width -ne 320 -or $streams.video.height -ne 180 -or $streams.audio.sample_rate -ne '48000' -or $streams.audio.channels -ne 2){throw 'Selected-media structure oracle failed.'}; $decoded=Decode $selected 'video' 'vp9' 'matroska' '0:v:0' 'selected-media-video'; Decode $selected 'audio' 'opus' 'matroska' '0:a:0' 'selected-media-audio' | Out-Null; $expected=PpmPixels @((Get-ChildItem (Join-Path $fixtures 'F1\f1-pattern-*.ppm') | Sort-Object Name | ForEach-Object FullName)); $mae=MeanAbsoluteError $expected ([IO.File]::ReadAllBytes($decoded)); if($mae -gt 18){throw "Selected-media visual oracle failed: MAE $mae exceeds 18."}; Write-Capability $cap 'passed' 'Explicit VP9/Opus selected-media proof passed with exact stream-layout, decode-again, and visual error evidence.' @{output=$selected; meanAbsoluteError=$mae; inspectedStreamMap=$streams.inspectedStreamMap}

    $cap=$contract.capabilities | Where-Object id -eq 'Video.Export.OpenDelivery.Composition'; $activeCapability=$cap
    $composition=New-AtomicMedia 'composition.webm' @('-hide_banner','-f','matroska','-c:v','ffv1','-c:a','flac','-i',$f1Source,'-loop','1','-f','image2','-c:v','ppm','-framerate','25','-i',(Join-Path $fixtures 'F2\f2-landscape-640x360-25fps.ppm'),'-f','s16le','-c:a','pcm_s16le','-ar','44100','-ac','1','-i',(Join-Path $fixtures 'F2\f2-44100-mono-330hz.pcm'),'-filter_complex','[0:v]scale=320:180,pad=320:180:0:0,format=yuv420p,fps=25,setpts=PTS-STARTPTS[v0];[0:a]aresample=48000,aformat=channel_layouts=stereo,asetpts=PTS-STARTPTS[a0];[1:v]trim=duration=0.12,scale=320:180,pad=320:180:0:0,format=yuv420p,fps=25,setpts=PTS-STARTPTS[v1];[2:a]atrim=duration=0.12,aresample=48000,aformat=channel_layouts=stereo,asetpts=PTS-STARTPTS[a1];[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]','-map','[v]','-map','[a]','-c:v','libvpx-vp9','-c:a','libopus','-f','webm')
    $p=Probe $composition 'composition'; $compositionStreams=Assert-WebmVp9Opus $p 'Composition export'; $duration=[double]$p.format.duration*1000; if([Math]::Abs($duration-240) -gt 25){throw "Composition duration oracle failed: $duration ms."}; $decoded=Decode $composition 'video' 'vp9' 'matroska' '0:v:0' 'composition-video'; Decode $composition 'audio' 'opus' 'matroska' '0:a:0' 'composition-audio' | Out-Null; $raw=[IO.File]::ReadAllBytes($decoded); $frameBytes=320*180*3; if($raw.Length -ne 6*$frameBytes){throw "Composition frame-count oracle failed: expected six frames, got $($raw.Length/$frameBytes)."}; $f1Expected=PpmPixels @((Get-ChildItem (Join-Path $fixtures 'F1\f1-pattern-*.ppm') | Sort-Object Name | ForEach-Object FullName)); $f2Expected=New-Object byte[] $frameBytes; for($i=0;$i -lt $f2Expected.Length;$i+=3){$f2Expected[$i]=255;$f2Expected[$i+1]=128;$f2Expected[$i+2]=0}; for($frame=0;$frame -lt 3;$frame++){if((MeanAbsoluteError (Slice-Bytes $f1Expected ($frame*$frameBytes) $frameBytes) (Slice-Bytes $raw ($frame*$frameBytes) $frameBytes)) -gt 18){throw "Composition F1 identity/order oracle failed at frame $frame."}}; for($frame=3;$frame -lt 6;$frame++){if((MeanAbsoluteError $f2Expected (Slice-Bytes $raw ($frame*$frameBytes) $frameBytes)) -gt 18){throw "Composition F2 identity/order oracle failed at frame $frame."}}; $compositionComponents=[ordered]@{inputDemuxers=@('matroska','image2','s16le');decoders=@('ffv1','flac','ppm','pcm_s16le');filters=@('scale','pad','format','fps','aresample','aformat','setpts','asetpts','concat');encoders=@('libvpx-vp9','libopus');muxer='webm';outputDecoders=@('vp9','opus');streamSelectors=@('0:v:0','0:a:0','1:v:0','2:a:0')}; Write-Capability $cap 'passed' 'Explicit normalized two-segment VP9/Opus composition decoded again with six ordered F1-then-F2 frame identities across the boundary.' @{output=$composition; durationMilliseconds=$duration; expectedFrameCount=6; boundaryAfterFrame=2; inspectedStreamMap=$compositionStreams.inspectedStreamMap} $compositionComponents

    $cap=$contract.capabilities | Where-Object id -eq 'Audio.Export.Standalone'; $activeCapability=$cap
    $source=Join-Path $fixtures 'F4\f4-stereo-48000-1000hz-opposed.pcm'
    $flac=New-AtomicMedia 'standalone.flac' @('-hide_banner','-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','2','-i',$source,'-map','0:a:0','-filter:a','aformat=sample_rates=48000:channel_layouts=stereo,aresample=48000','-c:a','flac','-f','flac')
    $opus=New-AtomicMedia 'standalone.ogg' @('-hide_banner','-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','2','-i',$source,'-map','0:a:0','-filter:a','aformat=sample_rates=48000:channel_layouts=stereo,aresample=48000','-c:a','libopus','-f','ogg')
    $flacProbe=Probe $flac 'standalone-flac'; if(@($flacProbe.streams | Where-Object codec_type -eq 'audio')[0].codec_name -ne 'flac'){throw 'FLAC inspection oracle failed.'}; $flacDecoded=Decode $flac 'audio' 'flac' 'flac' '0:a:0' 'standalone-flac'; if(-not [Linq.Enumerable]::SequenceEqual[byte]([IO.File]::ReadAllBytes($source),[IO.File]::ReadAllBytes($flacDecoded))){throw 'FLAC lossless oracle failed: decoded PCM is not byte exact.'}; $opusProbe=Probe $opus 'standalone-opus'; $opusStream=@($opusProbe.streams | Where-Object codec_type -eq 'audio')[0]; if($opusStream.codec_name -ne 'opus' -or $opusStream.sample_rate -ne '48000' -or $opusStream.channels -ne 2){throw 'Ogg Opus inspection oracle failed.'}; $opusPaddingToleranceMilliseconds=20; $opusPaddingToleranceSamples=1024; if([Math]::Abs(([double]$opusProbe.format.duration*1000)-500) -gt $opusPaddingToleranceMilliseconds){throw 'Ogg Opus duration oracle failed outside explicit padding tolerance.'}; $opusDecoded=Decode $opus 'audio' 'opus' 'ogg' '0:a:0' 'standalone-opus'; $decodedSamples=([IO.File]::ReadAllBytes($opusDecoded).Length/(2*2)); if([Math]::Abs($decodedSamples-24000) -gt $opusPaddingToleranceSamples){throw 'Ogg Opus decoded sample-count oracle failed outside explicit padding tolerance.'}; $frequencyComparison=Assert-ExpectedToneAgainstComparisons $opusDecoded 48000 2 1000; Write-Capability $cap 'passed' 'Explicit FLAC byte-exact and Ogg Opus decode-again standalone-audio proofs passed with declared-frequency comparison and duration/sample padding evidence.' @{flac=$flac; oggOpus=$opus; opusPaddingToleranceMilliseconds=$opusPaddingToleranceMilliseconds; opusPaddingToleranceSamples=$opusPaddingToleranceSamples; decodedSamplesPerChannel=$decodedSamples; frequencyComparison=$frequencyComparison}
}
catch {
    if($null -ne $activeCapability -and $activeCapability.id -notin @($proofs | ForEach-Object capabilityId)){ Write-Capability $activeCapability 'invalid-oracle' $_.Exception.Message @{reason='The proof contract was not changed to accommodate this result.'} }
    foreach($remaining in $contract.capabilities | Where-Object { $_.id -in @('Preview.GenerateDraftProxy','Video.Export.OpenDelivery.SelectedMedia','Video.Export.OpenDelivery.Composition','Audio.Export.Standalone') -and $_.id -notin @($proofs | ForEach-Object capabilityId) }) { Write-Capability $remaining 'not-run' 'Not run because an earlier active delivery capability failed.' @{blockedBy=$activeCapability.id} }
    throw
}
finally {
    $finalArtifacts=@(Get-ChildItem -LiteralPath $media -File | Sort-Object Name | ForEach-Object { [ordered]@{path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/'); length=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()} })
    $evidence=[ordered]@{schemaVersion=1; profileId='P2.BtbnLgplShared.WindowsX64.20260820'; semanticProofContractProfileId=$contract.profileId; runtimeIdentityEvidence='runtime-identity.json'; fixtureRoot=$fixtures; fixtureReportVerified=$true; fixtureReportSha256=$fixtureReportHash; componentPresence='See runtime-identity.json; presence is not semantic proof.'; deliveryNote='WebM VP9/Opus is an approved open-delivery proof candidate only; not the final ReelForge default.'; commands=$commands; finalArtifacts=$finalArtifacts; semanticProofs=$proofs}
    [IO.File]::WriteAllText((Join-Path $output 'delivery-proof-evidence.json'),($evidence|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
}
