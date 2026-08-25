Set-StrictMode -Version Latest

# G0.4 proof infrastructure only.  This module does not participate in import,
# persistence, rendering, packaging, or the product's media capability contract.

function Get-G04AuthoringValue {
    param([Parameter(Mandatory)][object]$Object, [Parameter(Mandatory)][string]$Name)
    if ($Object -is [Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-G04AuthoringSourcePath {
    param([Parameter(Mandatory)][string]$FixtureRoot, [Parameter(Mandatory)][string]$FileId)
    if ([IO.Path]::IsPathRooted($FileId) -or $FileId.Contains('..')) { throw "Fixture file id '$FileId' is not a contained contract path." }
    $path = Join-Path $FixtureRoot $FileId
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Contract source fixture is unavailable: $FileId" }
    return (Resolve-Path -LiteralPath $path).Path
}

function Get-G04AuthoringAudioInput {
    param([Parameter(Mandatory)][object]$Recipe, [Parameter(Mandatory)][object]$Audio)
    $source = @($Recipe.sourceArtifacts | Where-Object { $_.variantId -notmatch 'F1|F7|F3' }) | Select-Object -First 1
    if ($null -eq $source) { throw "Recipe $($Recipe.id) has no explicit audio source artifact." }
    $file = @($source.fileIds) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$file)) { throw "Recipe $($Recipe.id) has an empty audio source artifact." }
    $declared = Get-G04AuthoringValue $source 'declaredFormat'
    $rawRate = [int](Get-G04AuthoringValue $declared 'sampleRate')
    $rawLayout = [string](Get-G04AuthoringValue $declared 'channels')
    if ($rawRate -le 0 -or $rawLayout -notin @('mono','stereo')) { throw "Recipe $($Recipe.id) has no valid declared raw audio format." }
    $targetRate = [int]$Audio.sampleRate
    $targetLayout = [string]$Audio.channels
    if ($targetRate -le 0 -or $targetLayout -notin @('mono','stereo')) { throw "Case audio target is outside the exact contract." }
    $transforms = [string[]]@($source.transforms)
    if ($transforms -notcontains "aresample=$targetRate" -or $transforms -notcontains "channels=$targetLayout" -or $transforms -notcontains 'channel-layout=identity') { throw "Recipe $($Recipe.id) does not explicitly transform its declared audio source to the case target." }
    return [ordered]@{ fileId = [string]$file; rawSampleRate = $rawRate; rawChannels = if ($rawLayout -eq 'mono') { 1 } else { 2 }; targetSampleRate = $targetRate; targetChannels = if ($targetLayout -eq 'mono') { 1 } else { 2 } }
}

function Get-G04AuthoringVideoInput {
    param([Parameter(Mandatory)][object]$Recipe)
    $source = @($Recipe.sourceArtifacts | Where-Object { $_.variantId -match 'F1|F7' }) | Select-Object -First 1
    if ($null -eq $source) { throw "Recipe $($Recipe.id) has no explicit video source artifact." }
    $files = @($source.fileIds)
    if ($files.Count -eq 0) { throw "Recipe $($Recipe.id) has no video source files." }
    return [ordered]@{ variantId = [string]$source.variantId; fileIds = $files; transforms = @($source.transforms) }
}

function Get-G04AuthoringSourceRecord {
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$Path)
    $root = (Resolve-Path -LiteralPath ([string]$Context.FixtureRoot) -ErrorAction Stop).Path.TrimEnd('\','/')
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Source must be an existing explicit rooted file.' }
    $full = (Resolve-Path -LiteralPath $Path).Path
    if (-not $full.StartsWith("$root$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) { throw 'Source must be contained beneath FixtureRoot.' }
    $item = Get-Item -LiteralPath $full -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Source must not be a reparse-point.' }
    return [ordered]@{ path=[IO.Path]::GetRelativePath($root,$full).Replace('\','/'); length=[int64]$item.Length; sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToUpperInvariant() }
}

function Get-G04AuthoringCommonContext {
    param([Parameter(Mandatory)][hashtable]$Context)
    # The public authoring seam takes a hashtable; shared helpers operate on
    # object properties. Preserve the same mutable Commands collection.
    foreach ($name in @('Work','Media','Commands')) {
        if (-not $Context.ContainsKey($name) -or $null -eq $Context[$name]) { throw "Authoring context is missing shared-helper field $name." }
    }
    $logs = if ($Context.ContainsKey('Logs') -and $null -ne $Context['Logs']) { [string]$Context['Logs'] } else { Join-Path ([string]$Context['Work']) 'logs' }
    if (-not (Test-Path -LiteralPath $logs -PathType Container)) { New-Item -ItemType Directory -Path $logs -Force | Out-Null }
    return [pscustomobject]@{ Output=$Context['Media']; Work=$Context['Work']; Logs=$logs; Commands=$Context['Commands'] }
}

function Assert-G04AuthoringProducerTokens {
    param([Parameter(Mandatory)][object]$Recipe)
    # These are fixture-authoring tokens, deliberately distinct from native
    # decoder names under proof (notably libvpx/vp8 and libvorbis/vorbis).
    $allowed = @('aac','flac','h264_nvenc','libmp3lame','libopenh264','libopus','libvorbis','libvpx','libvpx-vp9','mjpeg','pcm_s16le','png')
    foreach ($producer in @($Recipe.producerEncoders)) {
        if ([string]$producer -notin $allowed) { throw "Recipe $($Recipe.id) names unapproved fixture producer '$producer'." }
    }
    if ((@($Recipe.producerEncoders) -contains 'libvpx') -and (@($Recipe.producerEncoders) -contains 'libvpx-vp9')) { throw "Recipe $($Recipe.id) cannot conflate VP8 libvpx and VP9 libvpx-vp9 producer modes." }
}

function Assert-G04AuthoringVideoEncoderOptions {
    param([Parameter(Mandatory)][object]$Recipe, [Parameter(Mandatory)][bool]$Required)
    $rawOptions = Get-G04AuthoringValue $Recipe 'encoderOptions'
    [string[]]$options = @()
    if ($null -ne $rawOptions) { $options = [string[]]@($rawOptions) }
    if ($options.Count -eq 0) {
        if ($Required) { throw "Recipe $($Recipe.id) must declare exact video/image encoderOptions." }
        return @('-c:v', [string]$Recipe.producerEncoders[0])
    }
    $encoderIndex = [array]::IndexOf($options, '-c:v')
    if ($encoderIndex -lt 0 -or $encoderIndex + 1 -ge $options.Count) { throw "Recipe $($Recipe.id) encoderOptions must explicitly select -c:v." }
    $encoder = [string]$options[$encoderIndex + 1]
    if ([string]::IsNullOrWhiteSpace($encoder) -or $encoder -notin @($Recipe.producerEncoders)) { throw "Recipe $($Recipe.id) encoderOptions do not match its authorized producer." }
    return $options
}

function Get-G04NvencEvidence {
    param([hashtable]$Context, [object]$Case, [string[]]$Arguments, [string[]]$RawSourcePaths, [string]$OutputPath)
    $os = try { Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop | Select-Object ProductName, DisplayVersion, CurrentBuild, UBR } catch { [ordered]@{ status = 'unavailable'; reason = $_.Exception.Message } }
    $gpu = try { @(Get-CimInstance Win32_VideoController -ErrorAction Stop | Select-Object Name, DriverVersion, PNPDeviceID, VideoProcessor) } catch { @([ordered]@{ status = 'unavailable'; reason = $_.Exception.Message }) }
    $identifiedGpu = @($gpu | Where-Object { -not [string]::IsNullOrWhiteSpace([string](Get-G04AuthoringValue $_ 'Name')) -and -not [string]::IsNullOrWhiteSpace([string](Get-G04AuthoringValue $_ 'DriverVersion')) })
    $gpuObservation = [ordered]@{ method='Win32_VideoController'; fallbackCommand=$null }
    if ($identifiedGpu.Count -eq 0) {
        $nvidiaSmi = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\nvidia-smi.exe'
        if (Test-Path -LiteralPath $nvidiaSmi -PathType Leaf) {
            $record = Invoke-G04RecordedCommand -Context (Get-G04AuthoringCommonContext $Context) -Name "observe-nvenc-gpu-$($Case.id)" -Executable $nvidiaSmi -Arguments @('--query-gpu=name,driver_version,pci.bus_id','--format=csv,noheader,nounits') -Components @{ purpose='NVENC fixture-producer GPU and driver provenance only'; semanticCapabilityProven=$false }
            $line = @(([string]$record.stdout -split "`r?`n") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)[0]
            $parts = @([string]$line -split ',' | ForEach-Object Trim)
            if ($parts.Count -eq 3 -and -not [string]::IsNullOrWhiteSpace($parts[0]) -and -not [string]::IsNullOrWhiteSpace($parts[1])) {
                $identifiedGpu = @([pscustomobject]@{ Name=$parts[0]; DriverVersion=$parts[1]; PNPDeviceID=$parts[2]; VideoProcessor=$parts[0] })
                $gpuObservation = [ordered]@{ method='explicit-rooted-nvidia-smi'; fallbackCommand=[ordered]@{ executable=$nvidiaSmi; sha256=(Get-FileHash -LiteralPath $nvidiaSmi -Algorithm SHA256).Hash.ToUpperInvariant(); arguments=@($record.arguments); stdoutPath=$record.stdoutPath; stderrPath=$record.stderrPath } }
            }
        }
    }
    if ($identifiedGpu.Count -eq 0) { throw 'blocked-fixture-provenance: required NVENC GPU and driver identity could not be recorded.' }
    return [ordered]@{
        p2RuntimeIdentity = $Context.RuntimeIdentity
        osIdentity = $os
        gpuIdentity = $identifiedGpu
        gpuIdentityObservation = $gpuObservation
        driverIdentity = @($identifiedGpu | ForEach-Object { Get-G04AuthoringValue $_ 'DriverVersion' })
        exactCommand = @($Context.Ffmpeg) + $Arguments
        rawSourceHashes = @($RawSourcePaths | Where-Object { $_ -notmatch '\.concat\.txt$' } | ForEach-Object { Get-G04AuthoringSourceRecord -Context $Context -Path $_ })
        outputHash = Get-G04ArtifactRecord -Context (Get-G04AuthoringCommonContext $Context) -Path $OutputPath
        profile = (@($Case.streams | Where-Object type -eq 'video') | Select-Object -First 1).profile
        level = (@($Case.streams | Where-Object type -eq 'video') | Select-Object -First 1).maximumLevel
        pixelFormat = (@($Case.streams | Where-Object type -eq 'video') | Select-Object -First 1).pixelFormat
        timingMetadata = $Case.timing
    }
}

function New-G04CaseArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Case,
        [Parameter(Mandatory)][object]$Recipe,
        [Parameter(Mandatory)][object]$Contract,
        [Parameter(Mandatory)][hashtable]$Context,
        [Parameter(Mandatory)][hashtable]$ArtifactsByCase
    )

    foreach ($name in @('Ffmpeg', 'FixtureRoot', 'Work', 'Media', 'Commands', 'RuntimeIdentity')) {
        if (-not $Context.ContainsKey($name) -or $null -eq $Context[$name]) { throw "Authoring context is missing $name." }
    }
    if ([string]$Recipe.status -eq 'unresolved-producer') {
        return [ordered]@{ status = 'blocked'; path = $null; producerEvidence = $null; reason = 'blocked-fixture-provenance: exact recipe has no approved deterministic producer; no execution or substitution occurred.' }
    }
    if ([string]$Recipe.status -ne 'resolved') {
        return [ordered]@{ status = 'blocked'; path = $null; producerEvidence = $null; reason = "blocked-fixture-provenance: recipe status '$($Recipe.status)' is not executable." }
    }
    Assert-G04AuthoringProducerTokens $Recipe
    if ([string](Get-G04AuthoringValue $Case.fixtureProduction 'remux') -eq 'stream-copy-only') {
        $sourceId = [string]$Case.fixtureProduction.sourceCaseId
        if ([string]::IsNullOrWhiteSpace($sourceId) -or -not $ArtifactsByCase.ContainsKey($sourceId)) { throw "Stream-copy source case '$sourceId' must be authored first." }
        $source = $ArtifactsByCase[$sourceId]
        if ([string]$source.status -ne 'authored' -or -not [bool](Get-G04AuthoringValue $source 'semanticProofPassed') -or -not (Test-Path -LiteralPath $source.path -PathType Leaf)) { throw "Stream-copy source case '$sourceId' has not passed its bound semantic proof." }
        $final = Join-Path $Context.Media ("$($Case.id).$($Recipe.artifactExtension)")
        $partial = "$final.partial"
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
        try {
            Invoke-G04RecordedCommand -Context (Get-G04AuthoringCommonContext $Context) -Name "author-$($Case.id)" -Executable $Context.Ffmpeg -Arguments @('-hide_banner','-xerror','-err_detect','explode','-i',$source.path,'-map','0','-c','copy','-f','matroska','-y',$partial) -Components @{ recipeId=$Recipe.id; producerEncoders=@(); muxer='matroska'; streamCopyOnly=$true } | Out-Null
            if (-not (Test-Path -LiteralPath $partial -PathType Leaf)) { throw "Stream-copy producer did not create $partial." }
            Move-Item -LiteralPath $partial -Destination $final -Force
        } catch { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }; throw }
        $result = [ordered]@{ status='authored'; path=$final; producerEvidence=[ordered]@{ type='stream-copy-only'; sourceCaseId=$sourceId; sourceArtifact=(Get-G04ArtifactRecord -Context (Get-G04AuthoringCommonContext $Context) -Path $source.path); outputArtifact=(Get-G04ArtifactRecord -Context (Get-G04AuthoringCommonContext $Context) -Path $final) }; reason=$null }
        $ArtifactsByCase[[string]$Case.id] = $result
        return $result
    }

    $image = @($Case.streams | Where-Object type -eq 'image') | Select-Object -First 1
    $video = @($Case.streams | Where-Object type -eq 'video') | Select-Object -First 1
    $audio = @($Case.streams | Where-Object type -eq 'audio') | Select-Object -First 1
    $final = Join-Path $Context.Media ("$($Case.id).$($Recipe.artifactExtension)")
    $partial = "$final.partial"
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    $sources = [Collections.Generic.List[string]]::new()
    $args = [Collections.Generic.List[string]]::new()
    $audioInputIndex = $null
    $isVfr = $false
    $args.AddRange([string[]]@('-hide_banner','-xerror','-err_detect','explode'))

    if ($null -ne $image) {
        $source = @($Recipe.sourceArtifacts | Select-Object -First 1)
        $file = @($source.fileIds | Select-Object -First 1)
        if ([string]::IsNullOrWhiteSpace([string]$file)) { throw "Image recipe $($Recipe.id) has no explicit source artifact." }
        $sourcePath = Get-G04AuthoringSourcePath $Context.FixtureRoot ([string]$file)
        $sources.Add($sourcePath)
        $transform = [string[]]@($Recipe.transforms)
        $scale = @($transform | Where-Object { $_ -match '^scale=\d+:\d+$' }) | Select-Object -First 1
        $format = @($transform | Where-Object { $_ -match '^format=' }) | Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace([string]$scale) -or [string]::IsNullOrWhiteSpace([string]$format)) { throw "Image recipe $($Recipe.id) must declare exact scale and pixel format transforms." }
        $filter = "$scale,$format"
        $options = Assert-G04AuthoringVideoEncoderOptions -Recipe $Recipe -Required $true
        if ($sourcePath.EndsWith('.rgba', [StringComparison]::OrdinalIgnoreCase)) {
            # F3 is an explicit 320x180 RGBA raw primitive, not an image sequence.
            $args.AddRange([string[]]@('-f','rawvideo','-c:v','rawvideo','-pixel_format','rgba','-video_size','320x180','-i',$sourcePath))
        } else {
            $args.AddRange([string[]]@('-f','image2','-c:v','ppm','-i',$sourcePath))
        }
        $args.AddRange([string[]]@('-frames:v','1','-vf',$filter))
        $args.AddRange([string[]]$options)
    }

    if ($null -ne $video) {
        $input = Get-G04AuthoringVideoInput $Recipe
        $sourcePaths = @($input.fileIds | ForEach-Object { Get-G04AuthoringSourcePath $Context.FixtureRoot $_ })
        $sources.AddRange([string[]]$sourcePaths)
        if ($input.variantId -eq 'F1-three-patterns') {
            # Contract's identity cycle is exactly three source images, repeated to two seconds.
            $pattern = Join-Path $Context.FixtureRoot 'F1\f1-pattern-%03d.ppm'
            $rate = if ([string]$Case.timing.kind -eq 'cfr') { [string]$Case.timing.frameRate } else { throw "F1 source cannot author non-CFR case $($Case.id)." }
            $args.AddRange([string[]]@('-stream_loop','-1','-framerate',$rate,'-f','image2','-c:v','ppm','-i',$pattern))
        } elseif ($input.variantId -eq 'F7-vfr-offset') {
            # F7 is intentionally assembled through a concat manifest: distinct presentation intervals,
            # signed non-zero PTS, terminal duration and the 2 s container duration are not approximated.
            $concat = Join-Path $Context.Work ("$($Case.id).f7.concat.txt")
            # F7 is authored on a fine 1/90000 filter base and normalized by
            # each container to exact millisecond presentation timestamps.
            $durations = @('0.040000','0.080000','0.010000','0.070000','0.800000')
            $lines = for ($i=0; $i -lt $sourcePaths.Count; $i++) { @("file '$($sourcePaths[$i].Replace("'", "''"))'", "duration $($durations[$i])") }
            [IO.File]::WriteAllLines($concat, [string[]]$lines, [Text.UTF8Encoding]::new($false))
            $sources.Add($concat)
            $args.AddRange([string[]]@('-copyts','-itsoffset','1.000000','-f','concat','-safe','0','-c:v','ppm','-i',$concat))
        } else { throw "Recipe $($Recipe.id) names unrecognized exact video source variant '$($input.variantId)'." }
    }
    if ($null -ne $audio) {
        $input = Get-G04AuthoringAudioInput $Recipe $audio
        $audioPath = Get-G04AuthoringSourcePath $Context.FixtureRoot $input.fileId
        $sources.Add($audioPath)
        $audioInputIndex = if ($null -ne $video) { '1' } else { '0' }
        $args.AddRange([string[]]@('-f','s16le','-c:a','pcm_s16le','-ar',[string]$input.rawSampleRate,'-ac',[string]$input.rawChannels,'-i',$audioPath))
    }
    if ($null -eq $image -and $null -eq $video -and $null -eq $audio) { throw "Resolved recipe $($Recipe.id) has neither a contract image, video, nor audio stream." }
    if ($null -ne $video) { $args.AddRange([string[]]@('-map','0:v:0')) }
    if ($null -ne $audio) { $args.AddRange([string[]]@('-map',"${audioInputIndex}:a:0")) }

    if ($null -ne $video) {
        $isVfr = [string]$Case.timing.kind -eq 'vfr-nonzero-pts'
        $vf = if ($isVfr) { "scale=$($video.width):$($video.height),format=$($video.pixelFormat),settb=1/90000,setpts=if(eq(N\,0)\,90000\,if(eq(N\,1)\,93600\,if(eq(N\,2)\,100800\,if(eq(N\,3)\,101700\,108000))))" } else { "scale=$($video.width):$($video.height),fps=$($Case.timing.frameRate),format=$($video.pixelFormat)" }
        $args.AddRange([string[]]@('-vf',$vf))
        if ($isVfr) { $args.AddRange([string[]]@('-fps_mode','passthrough','-enc_time_base:v','1/1000')) }
        $options = Assert-G04AuthoringVideoEncoderOptions -Recipe $Recipe -Required $false
        $args.AddRange([string[]]$options)
        if ($null -eq $audio) { $args.Add('-an') }
    }
    if ($null -ne $audio) {
        $rawAudioOptions = Get-G04AuthoringValue $Recipe 'audioEncoderOptions'
        [string[]]$audioOptions = @()
        if ($null -ne $rawAudioOptions) { $audioOptions = [string[]]@($rawAudioOptions) }
        if ($audioOptions.Count -eq 0 -or $audioOptions -notcontains '-c:a') { throw "Recipe $($Recipe.id) must declare exact audioEncoderOptions." }
        $audioEncoder = $audioOptions[($audioOptions.IndexOf('-c:a') + 1)]
        if ([string]::IsNullOrWhiteSpace([string]$audioEncoder) -or $audioEncoder -notin @($Recipe.producerEncoders)) { throw "Recipe $($Recipe.id) audioEncoderOptions do not match its authorized producer." }
        if ($null -ne $video) {
            $loopSamples = [int]($input.targetSampleRate / 2)
            $args.AddRange([string[]]@('-af',"aresample=$($input.targetSampleRate),aformat=channel_layouts=$($audio.channels),aloop=loop=-1:size=$loopSamples,apad=whole_dur=2,atrim=duration=2"))
        } else {
            $args.AddRange([string[]]@('-af',"aresample=$($input.targetSampleRate),aformat=channel_layouts=$($audio.channels)"))
        }
        $args.AddRange([string[]]$audioOptions)
    }
    if ($null -ne $video) { $args.AddRange([string[]]@('-t','2')) }
    if ($isVfr -and [string]$Recipe.muxer -eq 'mp4') { $args.AddRange([string[]]@('-video_track_timescale','1000')) }
    $args.AddRange([string[]]@('-f',[string]$Recipe.muxer,'-y',$partial))
    for ($argumentIndex = 0; $argumentIndex -lt $args.Count; $argumentIndex++) {
        if ([string]::IsNullOrEmpty($args[$argumentIndex])) { throw "Recipe $($Recipe.id) constructed an empty command argument at index $argumentIndex." }
    }
    try {
        $fixtureSourceDecoders = @()
        if ($null -ne $image -and $sources[0].EndsWith('.rgba', [StringComparison]::OrdinalIgnoreCase)) { $fixtureSourceDecoders += 'rawvideo' }
        if ($null -ne $image -and -not $sources[0].EndsWith('.rgba', [StringComparison]::OrdinalIgnoreCase)) { $fixtureSourceDecoders += 'ppm' }
        if ($null -ne $video) { $fixtureSourceDecoders += 'ppm' }
        if ($null -ne $audio) { $fixtureSourceDecoders += 'pcm_s16le' }
        Invoke-G04RecordedCommand -Context (Get-G04AuthoringCommonContext $Context) -Name "author-$($Case.id)" -Executable $Context.Ffmpeg -Arguments $args.ToArray() -Components @{ recipeId=$Recipe.id; fixtureSourceDecoders=@($fixtureSourceDecoders | Sort-Object -Unique); producerEncoders=@($Recipe.producerEncoders); muxer=$Recipe.muxer; transforms=@($Recipe.transforms); fixtureProductionOnly=$true } | Out-Null
        if (-not (Test-Path -LiteralPath $partial -PathType Leaf)) { throw "Fixture producer did not create $partial." }
        Move-Item -LiteralPath $partial -Destination $final -Force
    } catch { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }; throw }
    $workArtifacts = @($sources | Where-Object { $_ -match '\.concat\.txt$' } | ForEach-Object { $item=Get-Item -LiteralPath $_; [ordered]@{ path=[IO.Path]::GetRelativePath($Context.Work,$item.FullName).Replace('\','/'); length=[int64]$item.Length; sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant() } })
    $evidence = [ordered]@{ producerEncoders=@($Recipe.producerEncoders); sourceArtifacts=@($sources | Where-Object { $_ -notmatch '\.concat\.txt$' } | ForEach-Object { Get-G04AuthoringSourceRecord -Context $Context -Path $_ }); workArtifacts=$workArtifacts; outputArtifact=(Get-G04ArtifactRecord -Context (Get-G04AuthoringCommonContext $Context) -Path $final); exactTransforms=@($Recipe.transforms); exactEncoderOptions=@(Get-G04AuthoringValue $Recipe 'encoderOptions'); exactAudioEncoderOptions=@(Get-G04AuthoringValue $Recipe 'audioEncoderOptions') }
    if (@($Recipe.producerEncoders) -contains 'h264_nvenc') { $evidence.nvenc = Get-G04NvencEvidence $Context $Case $args.ToArray() $sources.ToArray() $final }
    $result = [ordered]@{ status='authored'; path=$final; producerEvidence=$evidence; reason=$null }
    $ArtifactsByCase[[string]$Case.id] = $result
    return $result
}
