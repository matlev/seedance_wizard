[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $RuntimeRoot,
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'manifests\p2-btbn-lgplv3-shared-windows-x64-20260820.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ExplicitPath([string] $Path, [string] $Name) {
    if (-not [System.IO.Path]::IsPathRooted($Path)) { throw "$Name must be an explicit rooted path." }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name does not exist: $Path" }
    return (Resolve-Path -LiteralPath $Path).Path
}

if ($env:OS -ne 'Windows_NT') { throw 'W1 is Windows-only and cannot run on this operating system.' }
if (-not [System.IO.Path]::IsPathRooted($RuntimeRoot) -or -not (Test-Path -LiteralPath $RuntimeRoot -PathType Container)) {
    throw 'RuntimeRoot must be an existing explicit rooted directory. PATH fallback is prohibited.'
}
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be an explicit rooted directory.' }

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if ($outputFull.Equals($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    $outputFull.StartsWith("$repositoryRoot\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be outside the repository.'
}
if (Test-Path -LiteralPath $outputFull) {
    if (-not (Get-ChildItem -LiteralPath $outputFull -Force | Select-Object -First 1)) { }
    else { throw 'OutputDirectory must be new or empty.' }
} else { New-Item -ItemType Directory -Path $outputFull | Out-Null }

$runtimeFull = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$ffmpeg = Resolve-ExplicitPath (Join-Path $runtimeFull 'bin\ffmpeg.exe') 'ffmpeg.exe'
$ffprobe = Resolve-ExplicitPath (Join-Path $runtimeFull 'bin\ffprobe.exe') 'ffprobe.exe'
$manifestFull = (Resolve-Path -LiteralPath $ManifestPath).Path

$validate = Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1'
& pwsh -NoProfile -File $validate -RuntimeRoot $runtimeFull -ManifestPath $manifestFull -EvidencePath (Join-Path $outputFull 'p2-runtime-validation.json')
if ($LASTEXITCODE -ne 0) { throw "P2 runtime validation failed with exit code $LASTEXITCODE." }

$fixtureDirectory = Join-Path $outputFull 'fixtures'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Generate-Fixtures.ps1') -FfmpegPath $ffmpeg -FfprobePath $ffprobe -ApprovedRuntimeRoot $runtimeFull -OutputDirectory $fixtureDirectory
if ($LASTEXITCODE -ne 0) { throw "Fixture generation failed with exit code $LASTEXITCODE." }

function Invoke-Captured([string] $FilePath, [string[]] $Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ exitCode = $process.ExitCode; stdout = $stdout; stderr = $stderr }
}

$f1 = Join-Path $fixtureDirectory 'F1'
$mp4 = Join-Path $outputFull 'w1-mf-h264-aac.mp4'
$mp4Partial = "$mp4.partial"
$encodeArgs = @('-hide_banner','-y','-f','image2','-framerate','25','-c:v','ppm','-i',(Join-Path $f1 'f1-pattern-%03d.ppm'),'-f','s16le','-ar','48000','-ac','2','-c:a','pcm_s16le','-i',(Join-Path $f1 'f1-sync-440hz-880hz-48000-stereo.pcm'),'-map','0:v:0','-map','1:a:0','-c:v','h264_mf','-pix_fmt','yuv420p','-c:a','aac_mf','-ar','48000','-ac','2','-f','mp4','-frames:v','3','-t','0.12',$mp4Partial)
$encode = Invoke-Captured $ffmpeg $encodeArgs
$encode.stderr | Set-Content -LiteralPath (Join-Path $outputFull 'encode.stderr.txt')

$encodeSucceeded = $encode.exitCode -eq 0 -and (Test-Path -LiteralPath $mp4Partial -PathType Leaf) -and (Get-Item -LiteralPath $mp4Partial).Length -gt 0
if ($encodeSucceeded) {
    Move-Item -LiteralPath $mp4Partial -Destination $mp4
}

$probeArgs = @('-v','error','-print_format','json','-show_entries','format=format_name,duration,size:stream=index,codec_type,codec_name,codec_tag_string,width,height,pix_fmt,avg_frame_rate,sample_rate,channels,channel_layout,nb_frames,start_time,duration','-i',$mp4)
$probe = if ($encodeSucceeded) { Invoke-Captured $ffprobe $probeArgs } else { [pscustomobject]@{ exitCode = 125; stdout = ''; stderr = 'Skipped because the explicit W1 encode did not produce a nonempty partial artifact.' } }
$probe.stdout | Set-Content -LiteralPath (Join-Path $outputFull 'probe.json')
$probe.stderr | Set-Content -LiteralPath (Join-Path $outputFull 'probe.stderr.txt')
$videoDecodeArgs = @('-hide_banner','-y','-c:v','h264','-i',$mp4,'-map','0:v:0','-c:v','rawvideo','-pix_fmt','yuv420p','-f','rawvideo','NUL')
$audioDecodeArgs = @('-hide_banner','-y','-c:a','aac','-i',$mp4,'-map','0:a:0','-c:a','pcm_s16le','-f','s16le','NUL')
$videoDecode = if ($encodeSucceeded) { Invoke-Captured $ffmpeg $videoDecodeArgs } else { [pscustomobject]@{ exitCode = 125; stdout = ''; stderr = 'Skipped because the explicit W1 encode did not produce a finalized artifact.' } }
$audioDecode = if ($encodeSucceeded) { Invoke-Captured $ffmpeg $audioDecodeArgs } else { [pscustomobject]@{ exitCode = 125; stdout = ''; stderr = 'Skipped because the explicit W1 encode did not produce a finalized artifact.' } }
$videoDecode.stderr | Set-Content -LiteralPath (Join-Path $outputFull 'decode-video.stderr.txt')
$audioDecode.stderr | Set-Content -LiteralPath (Join-Path $outputFull 'decode-audio.stderr.txt')

$componentArgs = @('-hide_banner','-encoders')
$componentListing = Invoke-Captured $ffmpeg $componentArgs
$componentListing.stdout | Set-Content -LiteralPath (Join-Path $outputFull 'encoders.txt')
$hasMf = $componentListing.stdout -match '(?m)\bh264_mf\b' -and $componentListing.stdout -match '(?m)\baac_mf\b'
$demuxerArgs = @('-hide_banner','-demuxers')
$demuxerListing = Invoke-Captured $ffmpeg $demuxerArgs
$demuxerListing.stdout | Set-Content -LiteralPath (Join-Path $outputFull 'demuxers.txt')
$muxerArgs = @('-hide_banner','-muxers')
$muxerListing = Invoke-Captured $ffmpeg $muxerArgs
$muxerListing.stdout | Set-Content -LiteralPath (Join-Path $outputFull 'muxers.txt')
$structural = $false
$durationWithinTolerance = $false
$observedFormat = $null
$streams = @()
if ($probe.exitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $outputFull 'probe.json'))) {
    $json = Get-Content -LiteralPath (Join-Path $outputFull 'probe.json') -Raw | ConvertFrom-Json
    $observedFormat = $json.format
    $streams = @($json.streams)
    $durationWithinTolerance = [Math]::Abs(([double]$json.format.duration) - 0.128) -le 0.02
    $structural = $json.format.format_name -match 'mp4' -and $streams.Count -eq 2 -and
        $streams[0].codec_name -eq 'h264' -and $streams[1].codec_name -eq 'aac' -and
        $streams[0].width -eq 320 -and $streams[0].height -eq 180 -and $streams[0].pix_fmt -eq 'yuv420p' -and
        $streams[0].avg_frame_rate -eq '25/1' -and $streams[0].nb_frames -eq '3' -and
        $streams[1].sample_rate -eq '48000' -and $streams[1].channels -eq 2 -and
        $streams[1].channel_layout -eq 'stereo' -and $durationWithinTolerance
}
$status = if ($encode.exitCode -eq 0 -and $probe.exitCode -eq 0 -and $videoDecode.exitCode -eq 0 -and $audioDecode.exitCode -eq 0 -and $structural) { 'basic-wrapper-supported' } elseif (-not $hasMf) { 'basic-wrapper-unsupported' } else { 'environment-dependent' }
$evidence = [ordered]@{
    schemaVersion = 1; capability = 'W1.MediaFoundation.H264.AAC.MP4'; status = $status
    optionalWindowsEvidence = $true; portableBaseline = $false; shippingConclusion = $false
    runtimeRoot = $runtimeFull; ffmpegPath = $ffmpeg; ffprobePath = $ffprobe
    os = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' | Select-Object ProductName,DisplayVersion,CurrentBuild,UBR)
    commands = @(@{ tool='ffmpeg'; args=$encodeArgs; purpose='explicit ppm/pcm_s16le input, h264_mf/aac_mf MP4 encode with F1 maps' }, @{ tool='ffprobe'; args=$probeArgs; purpose='paired explicit MP4 stream inspection' }, @{ tool='ffmpeg'; args=$videoDecodeArgs; purpose='explicit H.264 decode-again to rawvideo' }, @{ tool='ffmpeg'; args=$audioDecodeArgs; purpose='explicit AAC decode-again to PCM' }, @{ tool='ffmpeg'; args=$componentArgs; purpose='explicit encoder presence observation' }, @{ tool='ffmpeg'; args=$demuxerArgs; purpose='explicit MOV/MP4 demuxer observation' }, @{ tool='ffmpeg'; args=$muxerArgs; purpose='explicit MP4 muxer observation' })
    componentPresence = @{ command=$componentArgs; h264_mf=($componentListing.stdout -match '(?m)\bh264_mf\b'); aac_mf=($componentListing.stdout -match '(?m)\baac_mf\b'); rawResult=$componentListing.stdout }
    containerPresence = @{ command=$demuxerArgs; movMp4=($demuxerListing.stdout -match '(?m)\bmov,mp4,m4a,3gp,3g2,mj2\b'); rawResult=$demuxerListing.stdout }
    muxerPresence = @{ command=$muxerArgs; mp4=($muxerListing.stdout -match '(?m)\bmp4\b'); rawResult=$muxerListing.stdout }
    observedStreamCodecs = @($streams | ForEach-Object { @{ index=$_.index; codecType=$_.codec_type; codecName=$_.codec_name; codecTag=$_.codec_tag_string } })
    exitCodes = @{ encode=$encode.exitCode; probe=$probe.exitCode; videoDecode=$videoDecode.exitCode; audioDecode=$audioDecode.exitCode }
    structuralAssertions = @{ passed=$structural; streamCount=$streams.Count; expected='MP4, H.264 video, AAC audio, 320x180 yuv420p at 25/1, 3 frames, stereo 48 kHz audio, duration 0.128s +/- 0.020s'; durationWithinTolerance=$durationWithinTolerance; observedFormat=$observedFormat; observedStreams=$streams }
    executionLimits = @{ softwareHardware='unknown'; independentPlayback='not collected'; hardwareDriverProfile='not collected'; rateControl='not collected'; resourceBehavior='not collected'; determinism='not collected'; fullWindowsCompatibility='incomplete and requires manual validation' }
    artifacts = @(Get-ChildItem -LiteralPath $outputFull -File | ForEach-Object { @{ name=$_.Name; length=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    disclaimer = 'Optional Windows capability evidence only. basic-wrapper-supported is limited to this encode/probe/decode path; it is not broad Windows compatibility. No portability, licensing, patent, distribution, or shipping conclusion.'
}
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputFull 'w1-evidence.json')
Write-Output "W1 status: $status"
Write-Output "W1 evidence: $(Join-Path $outputFull 'w1-evidence.json')"
