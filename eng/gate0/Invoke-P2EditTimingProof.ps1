[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RuntimeRoot,

    [Parameter(Mandatory = $true)]
    [string] $FixtureRoot,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-RootedExistingDirectory([string] $Path, [string] $Name) {
    if (-not [System.IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Name must be an existing explicit rooted directory. PATH fallback is prohibited."
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Assert-NewEmptyOutputDirectory([string] $Path, [string] $RepositoryRoot) {
    if (-not [System.IO.Path]::IsPathRooted($Path)) { throw 'OutputDirectory must be an explicit rooted path outside the repository.' }
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ($full.Equals($RepositoryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($RepositoryRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory must be outside the repository.'
    }
    if (Test-Path -LiteralPath $full) {
        if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw 'OutputDirectory must identify a directory.' }
        if ((Get-ChildItem -LiteralPath $full -Force | Measure-Object).Count -ne 0) { throw 'OutputDirectory must be new or empty.' }
    }
    else { New-Item -ItemType Directory -Path $full | Out-Null }
    return (Resolve-Path -LiteralPath $full).Path
}

function Assert-P2Identity([string] $Root, [object] $Manifest) {
    $entries = @($Manifest.primaryTool, $Manifest.inspectionTool) + @($Manifest.runtimeFiles)
    foreach ($entry in $entries) {
        $relativeProperty = $entry.PSObject.Properties['relativePath']
        $relative = if ($null -ne $relativeProperty) { [string]$relativeProperty.Value } else { [string]$entry.path }
        $expectedHash = [string]$entry.sha256
        $path = Join-Path $Root $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Approved P2 file is missing: $relative" }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant() -ne $expectedHash.ToUpperInvariant()) { throw "Approved P2 file hash mismatch: $relative" }
    }
    $ffmpeg = Join-Path $Root ([string]$Manifest.primaryTool.relativePath)
    $ffprobe = Join-Path $Root ([string]$Manifest.inspectionTool.relativePath)
    $ffmpegVersion = (& $ffmpeg -version 2>&1 | Select-Object -First 1).ToString()
    $ffprobeVersion = (& $ffprobe -version 2>&1 | Select-Object -First 1).ToString()
    if ($ffmpegVersion -ne [string]$Manifest.primaryTool.versionLine) { throw 'Approved P2 ffmpeg version identity mismatch.' }
    if ($ffprobeVersion -ne [string]$Manifest.inspectionTool.versionLine) { throw 'Approved P2 ffprobe version identity mismatch.' }
    return [ordered]@{ ffmpeg = $ffmpeg; ffprobe = $ffprobe; ffmpegVersion = $ffmpegVersion; ffprobeVersion = $ffprobeVersion }
}

function Assert-SafeFixtureRelativePath([string] $Path, [string] $Description) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [System.IO.Path]::IsPathRooted($Path) -or $Path.Contains('\') -or $Path -eq '..' -or $Path.StartsWith('../', [System.StringComparison]::Ordinal) -or $Path.Contains('/../')) {
        throw "$Description has unsafe path '$Path'."
    }
}

function Assert-FixturePathContained([string] $Root, [string] $RelativePath, [string] $Description) {
    Assert-SafeFixtureRelativePath $RelativePath $Description
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    $prefix = $Root + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "$Description escapes FixtureRoot: $RelativePath" }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "$Description is missing: $RelativePath" }
    if ((Get-Item -LiteralPath $candidate -Force).Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) { throw "$Description must not be a reparse point: $RelativePath" }
    return $candidate
}

function Assert-FixtureReport([string] $Root) {
    $reportPath = Join-Path $Root 'generated-fixture-report.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'FixtureRoot must be created by Generate-Fixtures.ps1 and contain generated-fixture-report.json.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $inventoryPath = Join-Path $PSScriptRoot 'fixture-source-inventory.json'
    $inventoryBytes = [System.IO.File]::ReadAllBytes($inventoryPath)
    $inventoryHash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($inventoryBytes))
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
    if ($inventory.schemaVersion -ne 1 -or $inventory.inventoryVersion -ne 1 -or $inventory.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820') { throw 'Checked-in fixture source inventory is not the approved schema/profile.' }
    if ($null -eq $report.approvedInventory -or $report.approvedInventory.schemaVersion -ne $inventory.schemaVersion -or $report.approvedInventory.inventoryVersion -ne $inventory.inventoryVersion -or $report.approvedInventory.sha256 -ne $inventoryHash) { throw 'Fixture report does not attest to the exact checked-in approved inventory.' }
    if ($report.profileId -ne $inventory.profileId) { throw 'Fixture report profileId does not match the approved inventory.' }
    if ($report.externalMediaCommandsExecuted -ne $false) { throw 'Fixture report does not represent deterministic source primitives.' }
    if ((Get-Item -LiteralPath $Root -Force).Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) { throw 'FixtureRoot must not be a reparse point.' }

    $expectedByPath = @{}
    foreach ($file in $inventory.files) {
        $path = [string]$file.path; Assert-SafeFixtureRelativePath $path 'Approved fixture inventory entry'
        if ($expectedByPath.ContainsKey($path)) { throw "Approved fixture inventory contains a duplicate path: $path" }
        $expectedByPath[$path] = $file
    }
    $reportedByPath = @{}
    foreach ($file in $report.sourceFiles) {
        $path = [string]$file.path; Assert-SafeFixtureRelativePath $path 'Fixture report entry'
        if ($reportedByPath.ContainsKey($path)) { throw "Fixture report contains a duplicate path: $path" }
        $reportedByPath[$path] = $file
    }
    $actualByPath = @{}
    foreach ($item in Get-ChildItem -LiteralPath $Root -Force -Recurse) {
        if ($item.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) { throw "FixtureRoot must not contain reparse points: $($item.FullName)" }
        if (-not $item.PSIsContainer -and $item.Name -ne 'generated-fixture-report.json') {
            $relative = [System.IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/')
            Assert-SafeFixtureRelativePath $relative 'Fixture file'
            $actualByPath[$relative] = $item
        }
    }
    foreach ($set in @($reportedByPath, $actualByPath)) {
        $missing = @($expectedByPath.Keys | Where-Object { -not $set.ContainsKey($_) })
        $additional = @($set.Keys | Where-Object { -not $expectedByPath.ContainsKey($_) })
        if ($missing.Count -ne 0 -or $additional.Count -ne 0) { throw "Fixture inventory file-set mismatch. Missing: $($missing -join ', '). Additional: $($additional -join ', ')." }
    }
    foreach ($path in $expectedByPath.Keys) {
        $expected = $expectedByPath[$path]; $reported = $reportedByPath[$path]; $actual = $actualByPath[$path]
        $candidate = Assert-FixturePathContained $Root $path 'Approved fixture input'
        $actualHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToUpperInvariant()
        foreach ($entry in @($reported, $actual)) {
            $length = if ($entry -eq $actual) { [int64]$actual.Length } else { [int64]$entry.length }
            $hash = if ($entry -eq $actual) { $actualHash } else { ([string]$entry.sha256).ToUpperInvariant() }
            if ($length -ne [int64]$expected.length -or $hash -ne ([string]$expected.sha256).ToUpperInvariant()) { throw "Fixture inventory metadata mismatch: $path" }
        }
    }
    $script:approvedFixturePaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $expectedByPath.Keys) { [void]$script:approvedFixturePaths.Add($path) }
    return $report
}

function Get-ApprovedFixtureInput([string] $RelativePath) {
    if (-not $script:approvedFixturePaths.Contains($RelativePath)) { throw "Proof input is not in the approved fixture inventory: $RelativePath" }
    return Assert-FixturePathContained $fixtureRootFull $RelativePath 'Proof input'
}

function Invoke-RecordedCommand([string] $Name, [string] $Executable, [string[]] $Arguments, [hashtable] $Components) {
    $stdoutPath = Join-Path $script:workingDirectory ("$Name.stdout.txt")
    $stderrPath = Join-Path $script:workingDirectory ("$Name.stderr.txt")
    & $Executable @Arguments 1> $stdoutPath 2> $stderrPath
    $exitCode = $LASTEXITCODE
    $record = [ordered]@{
        name = $Name; executable = $Executable; arguments = $Arguments; components = $Components
        exitCode = $exitCode; stdout = Get-Content -LiteralPath $stdoutPath -Raw; stderr = Get-Content -LiteralPath $stderrPath -Raw
    }
    $script:commands.Add($record)
    if ($exitCode -ne 0) { throw "Command '$Name' failed with exit code $exitCode." }
    return $record
}

function Move-Atomic([string] $TemporaryPath, [string] $FinalPath) {
    if (-not (Test-Path -LiteralPath $TemporaryPath -PathType Leaf)) { throw "Expected temporary artifact was not created: $TemporaryPath" }
    Move-Item -LiteralPath $TemporaryPath -Destination $FinalPath -ErrorAction Stop
    return [ordered]@{ path = [System.IO.Path]::GetRelativePath($script:outputDirectory, $FinalPath).Replace('\', '/'); length = (Get-Item -LiteralPath $FinalPath).Length; sha256 = (Get-FileHash -LiteralPath $FinalPath -Algorithm SHA256).Hash.ToUpperInvariant() }
}

function Get-Inspection([string] $Name, [string] $Path, [string[]] $ExtraArguments = @()) {
    $jsonPath = Join-Path $script:workingDirectory ("$Name.probe.json")
    $arguments = @('-v', 'error', '-print_format', 'json', '-show_format', '-show_streams', '-show_frames') + $ExtraArguments + @($Path)
    Invoke-RecordedCommand "$Name.inspect" $script:tools.ffprobe $arguments @{ inspectionTool = 'ffprobe'; explicitDemuxerRecorded = $true } | Out-Null
    # ffprobe emits the JSON to stdout; preserve it independently for evidence.
    $last = $script:commands[$script:commands.Count - 1]
    [System.IO.File]::WriteAllText($jsonPath, [string]$last.stdout, [System.Text.UTF8Encoding]::new($false))
    return (Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json)
}

function Get-PpmPayload([string] $Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $newlines = 0; $offset = 0
    while ($newlines -lt 3 -and $offset -lt $bytes.Length) { if ($bytes[$offset] -eq 10) { $newlines++ }; $offset++ }
    if ($newlines -ne 3) { throw "Invalid PPM header: $Path" }
    return $bytes[$offset..($bytes.Length - 1)]
}

function Assert-BytesEqual([byte[]] $Expected, [byte[]] $Actual, [string] $Message) {
    if ($Expected.Length -ne $Actual.Length) { throw "$Message Length expected $($Expected.Length), actual $($Actual.Length)." }
    for ($index = 0; $index -lt $Expected.Length; $index++) { if ($Expected[$index] -ne $Actual[$index]) { throw "$Message Byte mismatch at $index." } }
}

function Assert-MonotonicZeroBased([object] $Inspection, [string] $StreamType, [int] $ExpectedCount, [string] $Label) {
    $frames = @($Inspection.frames | Where-Object { $_.media_type -eq $StreamType })
    if ($ExpectedCount -gt 0 -and $frames.Count -ne $ExpectedCount) { throw "$Label expected $ExpectedCount $StreamType frames, found $($frames.Count)." }
    if ($ExpectedCount -eq 0 -and $frames.Count -eq 0) { throw "$Label did not expose any $StreamType frames for timestamp validation." }
    $timestamps = @($frames | ForEach-Object { [double]$_.best_effort_timestamp_time })
    if ([math]::Abs($timestamps[0]) -gt 0.0001) { throw "$Label timestamps do not start at zero." }
    for ($index = 1; $index -lt $timestamps.Count; $index++) { if ($timestamps[$index] -lt $timestamps[$index - 1]) { throw "$Label timestamps are not monotonic." } }
}

function Get-RgbPixel([byte[]] $Pixels, [int] $Width, [int] $FrameIndex, [int] $X, [int] $Y) {
    $offset = (($FrameIndex * $Width * 360) + ($Y * $Width) + $X) * 3
    return [byte[]]@($Pixels[$offset], $Pixels[$offset + 1], $Pixels[$offset + 2])
}

function Assert-ExactVideoTimestamps([object] $Inspection, [double[]] $ExpectedSeconds, [string] $Label) {
    $frames = @($Inspection.frames | Where-Object { $_.media_type -eq 'video' })
    if ($frames.Count -ne $ExpectedSeconds.Count) { throw "$Label expected $($ExpectedSeconds.Count) video frames, found $($frames.Count)." }
    for ($index = 0; $index -lt $ExpectedSeconds.Count; $index++) {
        $actual = [double]$frames[$index].best_effort_timestamp_time
        if ([math]::Abs($actual - $ExpectedSeconds[$index]) -gt 0.0001) { throw "$Label frame $index timestamp expected $($ExpectedSeconds[$index]) seconds, found $actual." }
    }
}

function Invoke-Capability([string] $Id, [scriptblock] $Body) {
    try { $detail = & $Body; return [ordered]@{ id = $Id; status = 'pass'; detail = $detail } }
    catch { return [ordered]@{ id = $Id; status = 'fail'; error = $_.Exception.Message } }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runtimeRootFull = Assert-RootedExistingDirectory $RuntimeRoot 'RuntimeRoot'
$fixtureRootFull = Assert-RootedExistingDirectory $FixtureRoot 'FixtureRoot'
$outputDirectory = Assert-NewEmptyOutputDirectory $OutputDirectory $repositoryRoot
$script:outputDirectory = $outputDirectory
$script:workingDirectory = Join-Path $outputDirectory '.working'
New-Item -ItemType Directory -Path $script:workingDirectory | Out-Null
$script:commands = [System.Collections.Generic.List[object]]::new()

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifests\p2-btbn-lgplv3-shared-windows-x64-20260820.json') -Raw | ConvertFrom-Json
$contract = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'semantic-proof-contract.json') -Raw | ConvertFrom-Json
$script:tools = Assert-P2Identity $runtimeRootFull $manifest
$fixtureReport = Assert-FixtureReport $fixtureRootFull
$truths = Get-Content -LiteralPath (Get-ApprovedFixtureInput 'expected-truths.json') -Raw | ConvertFrom-Json

$artifacts = [System.Collections.Generic.List[object]]::new()
$results = [System.Collections.Generic.List[object]]::new()

# Prepare the explicit FFV1/FLAC source used by the edit/timing proofs.
$f1List = Join-Path $script:workingDirectory 'f1-images.ffconcat'
$f1Frame0 = Get-ApprovedFixtureInput 'F1/f1-pattern-000.ppm'; $f1Frame1 = Get-ApprovedFixtureInput 'F1/f1-pattern-001.ppm'; $f1Frame2 = Get-ApprovedFixtureInput 'F1/f1-pattern-002.ppm'; $f1Audio = Get-ApprovedFixtureInput 'F1/f1-sync-440hz-880hz-48000-stereo.pcm'
[System.IO.File]::WriteAllText($f1List, "ffconcat version 1.0`nfile '$($f1Frame0 -replace "'", "'\\''")'`nduration 0.04`nfile '$($f1Frame1 -replace "'", "'\\''")'`nduration 0.04`nfile '$($f1Frame2 -replace "'", "'\\''")'`nduration 0.04`nfile '$($f1Frame2 -replace "'", "'\\''")'`n", [System.Text.UTF8Encoding]::new($false))
$f1Temporary = Join-Path $script:workingDirectory 'f1-source.partial.mkv'
Invoke-RecordedCommand 'prepare-f1' $script:tools.ffmpeg @('-y','-f','concat','-safe','0','-i',$f1List,'-f','s16le','-ar','48000','-ac','2','-i',$f1Audio,'-map','0:v:0','-map','1:a:0','-vf','format=rgb24','-c:v','ffv1','-c:a','flac','-shortest','-f','matroska',$f1Temporary) @{ demuxers = @('concat','s16le'); decoders = @('ppm','pcm_s16le'); filters = @('format'); encoders = @('ffv1','flac'); muxer = 'matroska'; maps = @('0:v:0','1:a:0') } | Out-Null
$f1Path = Join-Path $outputDirectory 'f1-source.mkv'; $artifacts.Add((Move-Atomic $f1Temporary $f1Path))

$results.Add((Invoke-Capability 'Video.Frame.ExtractExact' {
    $temporary = Join-Path $script:workingDirectory 'frame-extract.partial.rgb'
    Invoke-RecordedCommand 'frame-extract' $script:tools.ffmpeg @('-y','-i',$f1Path,'-map','0:v:0','-vf','select=eq(n\,1),format=rgb24','-vsync','0','-frames:v','1','-c:v','rawvideo','-f','rawvideo',$temporary) @{ demuxer = 'matroska'; decoder = 'ffv1'; filters = @('select','format'); encoder = 'rawvideo'; muxer = 'rawvideo'; maps = @('0:v:0') } | Out-Null
    $final = Join-Path $outputDirectory 'frame-extract.rgb'; $artifact = Move-Atomic $temporary $final; $artifacts.Add($artifact)
    Assert-BytesEqual (Get-PpmPayload $f1Frame1) ([System.IO.File]::ReadAllBytes($final)) 'Frame extraction oracle failed.'
    [ordered]@{ artifact = $artifact; selectedFrameOrdinal = 1; oracle = 'decoded rgb24 equals authored f1-pattern-001; exactly one raw frame by byte length' }
}))

$results.Add((Invoke-Capability 'Timeline.Trim.Exact' {
    $temporary = Join-Path $script:workingDirectory 'trim.partial.mkv'
    Invoke-RecordedCommand 'trim' $script:tools.ffmpeg @('-y','-i',$f1Path,'-filter_complex','[0:v:0]trim=start_frame=1:end_frame=3,setpts=PTS-STARTPTS[v];[0:a:0]atrim=start_sample=1920:end_sample=5760,asetpts=PTS-STARTPTS[a]','-map','[v]','-map','[a]','-c:v','ffv1','-c:a','flac','-f','matroska',$temporary) @{ demuxer = 'matroska'; decoders = @('ffv1','flac'); filters = @('trim','atrim','setpts','asetpts'); encoders = @('ffv1','flac'); muxer = 'matroska'; maps = @('0:v:0','0:a:0') } | Out-Null
    $final = Join-Path $outputDirectory 'trim.mkv'; $artifact = Move-Atomic $temporary $final; $artifacts.Add($artifact)
    $inspection = Get-Inspection 'trim' $final; Assert-MonotonicZeroBased $inspection 'video' 2 'Trim'; Assert-MonotonicZeroBased $inspection 'audio' 0 'Trim'
    $videoRaw = Join-Path $script:workingDirectory 'trim-video.partial.rgb'; Invoke-RecordedCommand 'trim-decode-video' $script:tools.ffmpeg @('-y','-i',$final,'-map','0:v:0','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo',$videoRaw) @{ demuxer = 'matroska'; decoder = 'ffv1'; encoder = 'rawvideo'; muxer = 'rawvideo'; maps = @('0:v:0') } | Out-Null
    $audioRaw = Join-Path $script:workingDirectory 'trim-audio.partial.pcm'; Invoke-RecordedCommand 'trim-decode-audio' $script:tools.ffmpeg @('-y','-i',$final,'-map','0:a:0','-c:a','pcm_s16le','-f','s16le',$audioRaw) @{ demuxer = 'matroska'; decoder = 'flac'; encoder = 'pcm_s16le'; muxer = 's16le'; maps = @('0:a:0') } | Out-Null
    $expectedVideo = [byte[]]((Get-PpmPayload $f1Frame1) + (Get-PpmPayload $f1Frame2))
    Assert-BytesEqual $expectedVideo ([System.IO.File]::ReadAllBytes($videoRaw)) 'Trim frame oracle failed.'
    $sourceAudio = [System.IO.File]::ReadAllBytes($f1Audio); $expectedAudio = $sourceAudio[(1920 * 4)..((5760 * 4) - 1)]
    Assert-BytesEqual $expectedAudio ([System.IO.File]::ReadAllBytes($audioRaw)) 'Trim audio oracle failed.'
    [ordered]@{ artifact = $artifact; inspection = $inspection; oracle = 'frames 1,2; PCM samples [1920,5760); zero-based monotonic timestamps' }
}))

$results.Add((Invoke-Capability 'Timeline.Concat.NormalizeAndContinueTimestamps' {
    $temporary = Join-Path $script:workingDirectory 'concat.partial.mkv'
    $f2Portrait = Get-ApprovedFixtureInput 'F2/f2-portrait-360x640-30000_1001fps.ppm'; $f2Audio = Get-ApprovedFixtureInput 'F2/f2-48000-stereo-660hz.pcm'
    Invoke-RecordedCommand 'concat-normalized' $script:tools.ffmpeg @('-y','-i',$f1Path,'-loop','1','-framerate','30000/1001','-t','0.12','-i',$f2Portrait,'-f','s16le','-ar','48000','-ac','2','-t','0.12','-i',$f2Audio,'-filter_complex','[0:v:0]scale=640:360:force_original_aspect_ratio=decrease,pad=640:360:(ow-iw)/2:(oh-ih)/2,format=yuv420p,fps=25,setpts=PTS-STARTPTS[v0];[0:a:0]aresample=48000,aformat=channel_layouts=stereo,asetpts=PTS-STARTPTS[a0];[1:v:0]scale=640:360:force_original_aspect_ratio=decrease,pad=640:360:(ow-iw)/2:(oh-ih)/2,format=yuv420p,fps=25,setpts=PTS-STARTPTS[v1];[2:a:0]aresample=48000,aformat=channel_layouts=stereo,asetpts=PTS-STARTPTS[a1];[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]','-map','[v]','-map','[a]','-c:v','ffv1','-c:a','flac','-f','matroska',$temporary) @{ inputDemuxers = @('matroska','image2','s16le'); decoders = @('ffv1','flac','ppm','pcm_s16le'); filters = @('scale','pad','format','fps','aresample','aformat','setpts','asetpts','concat'); encoders = @('ffv1','flac'); muxer = 'matroska'; maps = @('0:v:0','0:a:0','1:v:0','2:a:0') } | Out-Null
    $final = Join-Path $outputDirectory 'concat-normalized.mkv'; $artifact = Move-Atomic $temporary $final; $artifacts.Add($artifact)
    $inspection = Get-Inspection 'concat-normalized' $final; Assert-MonotonicZeroBased $inspection 'video' 6 'Concat'; Assert-MonotonicZeroBased $inspection 'audio' 0 'Concat'
    $video = @($inspection.streams | Where-Object { $_.codec_type -eq 'video' })[0]; if ($video.width -ne 640 -or $video.height -ne 360 -or $video.r_frame_rate -ne '25/1') { throw 'Concat normalization output does not match target geometry/cadence.' }
    Assert-ExactVideoTimestamps $inspection ([double[]](0, 0.04, 0.08, 0.12, 0.16, 0.20)) 'Concat'
    $audioStream = @($inspection.streams | Where-Object { $_.codec_type -eq 'audio' })[0]
    if ([int]$audioStream.sample_rate -ne 48000 -or [int]$audioStream.channels -ne 2) { throw 'Concat normalization output does not match target audio properties.' }
    $audioFrames = @($inspection.frames | Where-Object { $_.media_type -eq 'audio' })
    $audioSampleCount = [int64](($audioFrames | Measure-Object -Property nb_samples -Sum).Sum)
    $audioStart = [double]$audioFrames[0].best_effort_timestamp_time
    $audioEnd = [double]$audioFrames[-1].best_effort_timestamp_time + ([double]$audioFrames[-1].nb_samples / 48000)
    if ([math]::Abs($audioStart) -gt 0.0001 -or $audioSampleCount -ne 11520 -or [math]::Abs($audioEnd - 0.24) -gt (1.0 / 48000.0 + 0.000001)) { throw "Concat audio timing/sample coverage is not the expected 0-240ms, 11520 samples per channel." }
    $crossesBoundary = @($audioFrames | Where-Object { ([double]$_.best_effort_timestamp_time -lt 0.12) -and (([double]$_.best_effort_timestamp_time + ([double]$_.nb_samples / 48000)) -gt 0.12) }).Count -gt 0
    if (-not $crossesBoundary) { throw 'Concat audio timing does not cover both sides of the 120ms composition boundary.' }
    $audioRaw = Join-Path $script:workingDirectory 'concat-normalized-audio.partial.pcm'
    Invoke-RecordedCommand 'concat-normalized-decode-audio' $script:tools.ffmpeg @('-y','-i',$final,'-map','0:a:0','-c:a','pcm_s16le','-f','s16le',$audioRaw) @{ demuxer = 'matroska'; decoder = 'flac'; encoder = 'pcm_s16le'; muxer = 's16le'; maps = @('0:a:0') } | Out-Null
    if ((Get-Item -LiteralPath $audioRaw).Length -ne (11520 * 2 * 2)) { throw 'Concat decoded audio byte count does not match 240ms of 48kHz stereo PCM.' }
    $raw = Join-Path $script:workingDirectory 'concat-normalized.partial.rgb'
    Invoke-RecordedCommand 'concat-normalized-decode-video' $script:tools.ffmpeg @('-y','-i',$final,'-map','0:v:0','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo',$raw) @{ demuxer = 'matroska'; decoder = 'ffv1'; encoder = 'rawvideo'; muxer = 'rawvideo'; maps = @('0:v:0') } | Out-Null
    $pixels = [System.IO.File]::ReadAllBytes($raw)
    if ($pixels.Length -ne (6 * 640 * 360 * 3)) { throw "Concat decoded output did not contain six 640x360 rgb24 frames: $($pixels.Length) bytes." }
    foreach ($frameIndex in 0..2) {
        $barSamples = @(20, 60, 100, 140, 180, 220, 260, 300 | ForEach-Object { (Get-RgbPixel $pixels 640 $frameIndex $_ 40) -join ',' } | Select-Object -Unique)
        $firstBar = Get-RgbPixel $pixels 640 $frameIndex 20 40
        if ($barSamples.Count -lt 6 -or $firstBar[0] -lt 220 -or $firstBar[1] -lt 220 -or $firstBar[2] -lt 220) { throw "Concat frame $frameIndex does not retain the expected F1 authored color-bar identity." }
    }
    foreach ($frameIndex in 3..5) {
        $letterbox = Get-RgbPixel $pixels 640 $frameIndex 20 180
        $portraitCenter = Get-RgbPixel $pixels 640 $frameIndex 320 180
        if ($letterbox[0] -gt 10 -or $letterbox[1] -gt 10 -or $letterbox[2] -gt 10 -or $portraitCenter[0] -gt 45 -or $portraitCenter[1] -lt 85 -or $portraitCenter[1] -gt 175 -or $portraitCenter[2] -lt 210) { throw "Concat frame $frameIndex does not retain the expected F2 portrait/letterbox identity." }
    }
    [ordered]@{ artifact = $artifact; inspection = $inspection; expectedVideoTimestampsSeconds = @(0, 0.04, 0.08, 0.12, 0.16, 0.20); segmentBoundarySeconds = 0.12; audioTiming = [ordered]@{ sampleRate = 48000; channels = 2; expectedSampleCountPerChannel = 11520; actualSampleCountPerChannel = $audioSampleCount; startSeconds = $audioStart; endSeconds = $audioEnd; crossesBoundary = $crossesBoundary; decodedPcmByteCount = (Get-Item -LiteralPath $audioRaw).Length }; identities = [ordered]@{ frames0To2 = 'F1 authored color bars'; frames3To5 = 'F2 authored portrait with letterbox' }; oracle = 'six exact 25fps timestamps, audio 0-240ms across 120ms boundary, F1 then F2 identities, 640x360/48k stereo normalization' }
}))

$results.Add((Invoke-Capability 'Audio.Mix.Deterministic' {
    $temporary = Join-Path $script:workingDirectory 'audio-mix.partial.wav'
    $f8AudioZero = Get-ApprovedFixtureInput 'F8/f8-audio-zero-440hz.pcm'; $f8AudioOne = Get-ApprovedFixtureInput 'F8/f8-audio-one-880hz.pcm'
    Invoke-RecordedCommand 'audio-mix' $script:tools.ffmpeg @('-y','-f','s16le','-ar','48000','-ac','1','-i',$f8AudioZero,'-f','s16le','-ar','48000','-ac','1','-i',$f8AudioOne,'-filter_complex','[0:a:0]aformat=sample_rates=48000:channel_layouts=mono[a0];[1:a:0]aformat=sample_rates=48000:channel_layouts=mono[a1];[a0][a1]amix=inputs=2:normalize=0[a]','-map','[a]','-c:a','pcm_s16le','-f','wav',$temporary) @{ demuxer = 's16le'; decoder = 'pcm_s16le'; filters = @('aformat','amix'); encoder = 'pcm_s16le'; muxer = 'wav'; maps = @('0:a:0','1:a:0') } | Out-Null
    $final = Join-Path $outputDirectory 'audio-mix.wav'; $artifact = Move-Atomic $temporary $final; $artifacts.Add($artifact)
    $raw = Join-Path $script:workingDirectory 'audio-mix.partial.pcm'; Invoke-RecordedCommand 'audio-mix-decode' $script:tools.ffmpeg @('-y','-i',$final,'-map','0:a:0','-c:a','pcm_s16le','-f','s16le',$raw) @{ demuxer = 'wav'; decoder = 'pcm_s16le'; encoder = 'pcm_s16le'; muxer = 's16le'; maps = @('0:a:0') } | Out-Null
    $first = [System.IO.File]::ReadAllBytes($f8AudioZero); $second = [System.IO.File]::ReadAllBytes($f8AudioOne); $actual = [System.IO.File]::ReadAllBytes($raw)
    if ($actual.Length -ne $first.Length -or $first.Length -ne $second.Length) { throw 'Audio mix sample count does not match sources.' }
    for ($i = 0; $i -lt $actual.Length; $i += 2) { $a = [BitConverter]::ToInt16($first, $i); $b = [BitConverter]::ToInt16($second, $i); $expected = [math]::Max([int16]::MinValue, [math]::Min([int16]::MaxValue, $a + $b)); $observed = [BitConverter]::ToInt16($actual, $i); if ([math]::Abs($expected - $observed) -gt 1) { throw "Audio mix oracle failed at sample $($i / 2)." } }
    [ordered]@{ artifact = $artifact; sampleCount = $actual.Length / 2; oracle = 'independent int16-clamped sum, tolerance 1' }
}))

$evidence = [ordered]@{
    schemaVersion = 1; proofProfileId = $contract.profileId; runtimeProfileId = $manifest.profileId; generatedAtUtc = [DateTimeOffset]::UtcNow
    statement = 'Gate 0 semantic proof evidence only; it is not a product or shipping-runtime approval.'
    runtimeIdentity = $script:tools; fixtureReportVerified = $true; fixtureReport = $fixtureReport
    commands = $script:commands; artifacts = $artifacts; capabilities = $results
}
$evidencePath = Join-Path $outputDirectory 'p2-edit-timing-proof.json'
$temporaryEvidence = Join-Path $script:workingDirectory 'p2-edit-timing-proof.partial.json'
[System.IO.File]::WriteAllText($temporaryEvidence, ($evidence | ConvertTo-Json -Depth 20), [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporaryEvidence -Destination $evidencePath
Write-Output "Gate 0 edit/timing proof evidence: $evidencePath"
if (@($results | Where-Object status -ne 'pass').Count -ne 0) { exit 1 }
