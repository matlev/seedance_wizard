[CmdletBinding()]
param([Parameter(Mandatory)][string] $RuntimeRoot, [string] $OutputDirectory, [ValidateRange(1,64)][int] $Threads = 1, [switch] $KeepArtifacts)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# This is an explicit engineering command: it never downloads, installs, calls R2,
# or touches product assemblies. Fixtures are generated in a fresh temporary root.
$validation = & (Join-Path $PSScriptRoot 'Validate-MediaRuntime.ps1') -RuntimeRoot $RuntimeRoot -Live
$root = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ("reelforge-media-smoke-" + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $root) { throw 'Smoke output root must be new.' }; [IO.Directory]::CreateDirectory($root) | Out-Null
$final = $null
try {
  $ffmpeg = Join-Path $RuntimeRoot 'bin/ffmpeg.exe'; $ffprobe = Join-Path $RuntimeRoot 'bin/ffprobe.exe'
  function Invoke-SmallFfmpeg([string] $Family, [string[]] $Tokens, [string] $Output) {
    & $ffmpeg -threads $Threads @Tokens 2> (Join-Path $root "$Family.stderr.txt")
    $encodeExit = $LASTEXITCODE
    $probeExit = -1; $decodeExit = -1
    if ($encodeExit -eq 0 -and (Test-Path -LiteralPath $Output -PathType Leaf)) {
      & $ffprobe -v error -show_format -show_streams -of json $Output 1> (Join-Path $root "$Family.probe.json") 2> (Join-Path $root "$Family.probe.stderr.txt"); $probeExit = $LASTEXITCODE
      & $ffmpeg -v error -xerror -threads $Threads -i $Output -f null - 2> (Join-Path $root "$Family.decode.stderr.txt"); $decodeExit = $LASTEXITCODE
    }
    [ordered]@{ family=$Family; output=[IO.Path]::GetFileName($Output); encodeExitCode=$encodeExit; inspectExitCode=$probeExit; strictDecodeExitCode=$decodeExit; passed=($encodeExit -eq 0 -and $probeExit -eq 0 -and $decodeExit -eq 0) }
  }
  $source = Join-Path $root 'source.mkv'
  $seed = Invoke-SmallFfmpeg 'fixture' @('-hide_banner','-nostdin','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=2','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=2','-c:v','ffv1','-c:a','pcm_s16le','-y',$source) $source
  if (-not $seed.passed) { throw 'Generated smoke fixture did not pass inspection and strict decode.' }
  $profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'baseline-profile.json') -Raw | ConvertFrom-Json -Depth 32
  $fonts = @($profile.fonts | ForEach-Object { Join-Path $PSScriptRoot ([string]$_.relativePath).Replace('/','\') })
  for ($index = 0; $index -lt $fonts.Count; $index++) { if (-not (Test-Path -LiteralPath $fonts[$index] -PathType Leaf) -or (Get-FileHash -LiteralPath $fonts[$index] -Algorithm SHA256).Hash -ne [string]$profile.fonts[$index].sha256) { throw "Pinned smoke font is absent or hash-drifted: $([IO.Path]::GetFileName($fonts[$index]))." } }
  $fontFilterPath = { param($value) ([IO.Path]::GetFullPath($value).Replace('\','/').Replace(':','\:').Replace("'","\'")) }
  $textFilter = "drawtext=fontfile='$(& $fontFilterPath $fonts[0])':text='ReelForge':x=10:y=10:fontcolor=white:fontsize=20,drawtext=fontfile='$(& $fontFilterPath $fonts[1])':text='العربية':x=10:y=40:fontcolor=white:fontsize=20,drawtext=fontfile='$(& $fontFilterPath $fonts[2])':text='中文':x=10:y=70:fontcolor=white:fontsize=20"
  $cases = @(
    @{ n='frame-extraction'; o='frame.png'; t=@('-i',$source,'-vf','select=eq(n\,10)','-frames:v','1','-y') },
    @{ n='trim-concat'; o='trim.webm'; t=@('-i',$source,'-filter_complex','[0:v]trim=0:0.8,setpts=PTS-STARTPTS[v0];[0:a]atrim=0:0.8,asetpts=PTS-STARTPTS[a0];[0:v]trim=1:1.8,setpts=PTS-STARTPTS[v1];[0:a]atrim=1:1.8,asetpts=PTS-STARTPTS[a1];[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]','-map','[v]','-map','[a]','-c:v','libvpx-vp9','-c:a','libopus','-y') },
    @{ n='split-screen-mixed-audio'; o='pip.webm'; t=@('-i',$source,'-filter_complex','[0:v]split[a][b];[b]scale=160:90[sm];[a][sm]overlay=10:10[v];[0:a][0:a]amix=inputs=2[a]','-map','[v]','-map','[a]','-c:v','libvpx-vp9','-c:a','libopus','-y') },
    @{ n='transform-basic-color'; o='color.webm'; t=@('-i',$source,'-vf','transpose=1,colorlevels=rimin=0.05:gimin=0.05:bimin=0.05,hue=s=1.05','-c:v','libvpx-vp9','-c:a','libopus','-y') },
    @{ n='av-transition'; o='transition.webm'; t=@('-i',$source,'-filter_complex','[0:v]split[v0][v1];[v0][v1]xfade=transition=fade:duration=0.2:offset=0.8[v];[0:a]asplit[a0][a1];[a0][a1]acrossfade=d=0.2[a]','-map','[v]','-map','[a]','-c:v','libvpx-vp9','-c:a','libopus','-y') },
    @{ n='unicode-title-caption'; o='text.webm'; t=@('-i',$source,'-vf',$textFilter,'-c:v','libvpx-vp9','-c:a','libopus','-y') },
    @{ n='proxy'; o='proxy.webm'; t=@('-i',$source,'-vf','scale=160:-2','-c:v','libvpx-vp9','-c:a','libopus','-y') },
    @{ n='webm-vp9-opus'; o='delivery.webm'; t=@('-i',$source,'-c:v','libvpx-vp9','-c:a','libopus','-y') },
    @{ n='flac'; o='audio.flac'; t=@('-i',$source,'-vn','-c:a','flac','-y') },
    @{ n='png'; o='image.png'; t=@('-i',$source,'-frames:v','1','-c:v','png','-y') },
    @{ n='jpeg'; o='image.jpg'; t=@('-i',$source,'-frames:v','1','-c:v','mjpeg','-y') }
  )
  $results = @(); foreach ($case in $cases) { $results += Invoke-SmallFfmpeg $case.n ($case.t + (Join-Path $root $case.o)) (Join-Path $root $case.o) }
  $encoders = & $ffmpeg -hide_banner -encoders 2>&1 | Out-String
  if ($encoders -match 'libopenh264' -and $encoders -match '\baac\b') { $results += Invoke-SmallFfmpeg 'conditional-mp4' @('-i',$source,'-c:v','libopenh264','-c:a','aac','-y',(Join-Path $root 'conditional.mp4')) (Join-Path $root 'conditional.mp4') }
  else { $results += [ordered]@{family='conditional-mp4';status='runtime-unavailable';passed=$true} }
  $final = [ordered]@{ profile=$validation.profileId; status=if(@($results|Where-Object{-not $_.passed}).Count){'completed-with-failures'}else{'passed'}; targetMinutes=10; threads=$Threads; results=$results; outputRoot=if($KeepArtifacts -or $OutputDirectory){$root}else{$null}; artifactsRetained=[bool]($KeepArtifacts -or $OutputDirectory) }
} finally { if (-not $KeepArtifacts -and -not $OutputDirectory -and (Test-Path -LiteralPath $root)) { Remove-Item -LiteralPath $root -Recurse -Force } }
[pscustomobject]$final
if ($final.status -ne 'passed') { exit 1 }
