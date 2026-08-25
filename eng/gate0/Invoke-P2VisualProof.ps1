[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$FixtureRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This is Gate 0 proof infrastructure only. It uses a reviewed paired runtime
# and declared primitive inputs; it neither chooses a shipping runtime nor
# establishes a product media contract.
function Require-OutsideRepositoryEmptyDirectory([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'OutputDirectory must be an explicit rooted path outside the repository.' }
    $repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith("$repository\", [StringComparison]::OrdinalIgnoreCase)) {
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

function Normalize-InventoryPath([string]$Path, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) { throw "$Description must be a non-rooted relative path." }
    $normalized = $Path.Replace('\\', '/')
    $segments = $normalized.Split('/', [StringSplitOptions]::None)
    if ($segments.Count -eq 0 -or @($segments | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -ne 0) { throw "$Description contains an invalid path segment." }
    return $normalized
}

function Assert-ContainedNonReparseFile([string]$Root, [string]$RelativePath, [string]$Description) {
    $normalized = Normalize-InventoryPath $RelativePath $Description
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $candidate = [IO.Path]::GetFullPath((Join-Path $rootFull ($normalized.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    if (-not $candidate.StartsWith("$rootFull$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) { throw "$Description escapes FixtureRoot." }
    $current = $rootFull
    if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'FixtureRoot must not be a reparse point.' }
    foreach ($segment in $normalized.Split('/')) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) { throw "$Description is missing: $normalized" }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Description contains a reparse point: $normalized" }
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "$Description is not a file: $normalized" }
    return $candidate
}

function Get-FixtureFileSet([string]$Root, [string]$RelativePrefix = '') {
    $directory = Assert-ContainedNonReparseDirectory $Root $RelativePrefix
    $files = [Collections.Generic.List[string]]::new()
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        $relative = if ([string]::IsNullOrEmpty($RelativePrefix)) { $item.Name } else { "$RelativePrefix/$($item.Name)" }
        $normalized = Normalize-InventoryPath $relative 'Fixture file path'
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Fixture output contains a reparse point: $normalized" }
        if ($item.PSIsContainer) { foreach ($child in Get-FixtureFileSet $Root $normalized) { $files.Add($child) } }
        else { $files.Add($normalized) }
    }
    return $files
}

function Assert-ContainedNonReparseDirectory([string]$Root, [string]$RelativePath) {
    if ([string]::IsNullOrEmpty($RelativePath)) {
        $rootItem = Get-Item -LiteralPath $Root -Force
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'FixtureRoot must not be a reparse point.' }
        return $rootItem.FullName
    }
    $normalized = Normalize-InventoryPath $RelativePath 'Fixture directory path'
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $candidate = [IO.Path]::GetFullPath((Join-Path $rootFull ($normalized.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    if (-not $candidate.StartsWith("$rootFull$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $candidate -PathType Container)) { throw "Fixture directory is missing or escapes FixtureRoot: $normalized" }
    $current = $rootFull
    foreach ($segment in $normalized.Split('/')) {
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Fixture directory contains a reparse point: $normalized" }
    }
    return $candidate
}

function Test-FixtureReport([string]$Root, [string]$InventoryPath) {
    $reportPath = Join-Path $Root 'generated-fixture-report.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'FixtureRoot must contain generated-fixture-report.json.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
    if ($report.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820' -or $report.externalMediaCommandsExecuted) {
        throw 'Fixture report does not describe approved deterministic P2 source primitives.'
    }
    $inventoryHash = (Get-FileHash -LiteralPath $InventoryPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($null -eq $report.approvedInventory -or $report.approvedInventory.schemaVersion -ne $inventory.schemaVersion -or $report.approvedInventory.inventoryVersion -ne $inventory.inventoryVersion -or $report.approvedInventory.path -ne 'eng/gate0/fixture-source-inventory.json' -or $report.approvedInventory.sha256 -ne $inventoryHash) {
        throw 'Fixture report approvedInventory does not match the checked-in fixture source inventory.'
    }
    if ($inventory.schemaVersion -ne 1 -or $inventory.profileId -ne $report.profileId) { throw 'Checked-in fixture source inventory has an unsupported schema or profile.' }
    $expected = @{}
    foreach ($entry in @($inventory.files)) {
        $normalized = Normalize-InventoryPath ([string]$entry.path) 'Checked-in inventory path'
        if ($expected.ContainsKey($normalized)) { throw "Checked-in fixture source inventory duplicates '$normalized'." }
        $expected[$normalized] = $entry
    }
    $reported = @{}
    foreach ($entry in @($report.sourceFiles)) {
        $normalized = Normalize-InventoryPath ([string]$entry.path) 'Generated fixture report path'
        if ($reported.ContainsKey($normalized)) { throw "Generated fixture report duplicates '$normalized'." }
        $reported[$normalized] = $entry
    }
    if ($expected.Count -ne $reported.Count -or @($expected.Keys | Where-Object { -not $reported.ContainsKey($_) }).Count -ne 0 -or @($reported.Keys | Where-Object { -not $expected.ContainsKey($_) }).Count -ne 0) { throw 'Generated fixture report file set does not exactly match the checked-in fixture source inventory.' }
    $actualFiles = @(Get-FixtureFileSet $Root | Where-Object { $_ -ne 'generated-fixture-report.json' })
    if ($actualFiles.Count -ne $expected.Count -or @($expected.Keys | Where-Object { $_ -notin $actualFiles }).Count -ne 0 -or @($actualFiles | Where-Object { -not $expected.ContainsKey($_) }).Count -ne 0) { throw 'FixtureRoot file set does not exactly match the checked-in fixture source inventory.' }
    foreach ($path in $expected.Keys) {
        $expectedEntry = $expected[$path]; $reportedEntry = $reported[$path]
        if ($reportedEntry.length -ne $expectedEntry.length -or $reportedEntry.sha256 -ne $expectedEntry.sha256) { throw "Generated fixture report does not match the checked-in inventory: $path" }
        $fullPath = Assert-ContainedNonReparseFile $Root $path 'Fixture source'
        $file = Get-Item -LiteralPath $fullPath
        $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($file.Length -ne [int64]$expectedEntry.length -or $hash -ne $expectedEntry.sha256) { throw "Fixture source hash/length mismatch against checked-in inventory: $path" }
    }
    return [PSCustomObject]@{ Report=$report; InventoriedPaths=$expected.Keys }
}

function Assert-ConsumedInputsInventoried([string[]]$Paths, [object]$FixtureValidation) {
    foreach ($path in $Paths) {
        $normalized = Normalize-InventoryPath $path 'Consumed fixture input'
        if ($normalized -notin $FixtureValidation.InventoriedPaths) { throw "Consumed fixture input is not independently inventoried: $normalized" }
    }
}

function Invoke-Tool([string]$Tool, [string[]]$Arguments, [string]$Step) {
    $stdoutFile = Join-Path $logs "$Step.stdout.txt"; $stderrFile = Join-Path $logs "$Step.stderr.txt"
    & $Tool @Arguments 1> $stdoutFile 2> $stderrFile
    $record = [ordered]@{ step=$Step; executable=$Tool; arguments=$Arguments; exitCode=$LASTEXITCODE; stdout=(Get-Content $stdoutFile -Raw); stderr=(Get-Content $stderrFile -Raw) }
    $commands.Add($record)
    if ($record.exitCode -ne 0) { throw "Step '$Step' failed with exit code $($record.exitCode)." }
    return $record
}

function New-AtomicRaw([string]$Name, [string[]]$Arguments) {
    $final = Join-Path $media $Name; $partial = "$final.partial"
    Invoke-Tool $ffmpeg ($Arguments + @('-y', $partial)) "encode-$Name" | Out-Null
    if (-not (Test-Path -LiteralPath $partial -PathType Leaf) -or (Get-Item -LiteralPath $partial).Length -le 0) { throw "Atomic output '$Name' was not created or is empty." }
    Move-Item -LiteralPath $partial -Destination $final
    return $final
}

function New-AtomicMedia([string]$Name, [string[]]$Arguments) {
    $final = Join-Path $media $Name; $partial = "$final.partial"
    Invoke-Tool $ffmpeg ($Arguments + @('-y', $partial)) "encode-$Name" | Out-Null
    if (-not (Test-Path -LiteralPath $partial -PathType Leaf) -or (Get-Item -LiteralPath $partial).Length -le 0) { throw "Atomic output '$Name' was not created or is empty." }
    Move-Item -LiteralPath $partial -Destination $final
    return $final
}

function DecodeTimestampedFfv1([string]$Path, [string]$Name) {
    return New-AtomicRaw $Name @('-v','error','-f','matroska','-c:v','ffv1','-i',$Path,'-map','0:v:0','-vf','format=rgb24','-fps_mode','passthrough','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo')
}

function ProbeVideoTimestamps([string]$Path, [string]$Name, [int]$ExpectedCount, [int]$ExpectedDurationMilliseconds) {
    $record = Invoke-Tool $ffprobe @('-v','error','-f','matroska','-show_streams','-show_frames','-of','json',$Path) "probe-$Name"
    $probe = ([string]$record.stdout | ConvertFrom-Json)
    $stream = @($probe.streams | Where-Object { $_.codec_type -eq 'video' })
    if ($stream.Count -ne 1 -or $stream[0].codec_name -ne 'ffv1') { throw "$Name did not retain exactly one explicitly selected FFV1 video stream." }
    $frames = @($probe.frames | Where-Object { $_.media_type -eq 'video' })
    if ($frames.Count -ne $ExpectedCount) { throw "$Name frame-count oracle failed: expected $ExpectedCount, got $($frames.Count)." }
    $milliseconds = @($frames | ForEach-Object { [int][Math]::Round([double]$_.pts_time * 1000) })
    if ($milliseconds[0] -ne 0) { throw "$Name timestamp oracle failed: first frame must start at zero." }
    for ($i=1; $i -lt $milliseconds.Count; $i++) {
        if ($milliseconds[$i] -le $milliseconds[$i-1]) { throw "$Name timestamp oracle failed: timestamps are not monotonic." }
        if ($milliseconds[$i] - $milliseconds[$i-1] -ne 40) { throw "$Name timestamp oracle failed: cadence is not exactly 25 fps." }
    }
    if ($milliseconds[$milliseconds.Count - 1] -ne ($ExpectedDurationMilliseconds - 40)) { throw "$Name timestamp oracle failed: final presentation timestamp is incorrect." }
    return [PSCustomObject]@{ FrameCount=$frames.Count; PresentationTimestampsMilliseconds=$milliseconds; TimeBase=$stream[0].time_base }
}

function Read-RgbFrames([string]$Path, [int]$Width, [int]$Height) {
    $bytes = [IO.File]::ReadAllBytes($Path); $frameBytes = $Width * $Height * 3
    if ($bytes.Length -eq 0 -or $bytes.Length % $frameBytes -ne 0) { throw "Raw RGB output '$Path' has invalid geometry." }
    $frames = [Collections.Generic.List[byte[]]]::new()
    for ($offset = 0; $offset -lt $bytes.Length; $offset += $frameBytes) { $frame = [byte[]]::new($frameBytes); [Buffer]::BlockCopy($bytes, $offset, $frame, 0, $frameBytes); $frames.Add($frame) }
    return $frames
}

function Pixel([byte[]]$Frame, [int]$Width, [int]$X, [int]$Y) {
    $offset = (($Y * $Width) + $X) * 3
    return [PSCustomObject]@{ R=[int]$Frame[$offset]; G=[int]$Frame[$offset + 1]; B=[int]$Frame[$offset + 2] }
}
function Assert-RgbNear([object[]]$Actual, [int[]]$Expected, [int]$Tolerance, [string]$Name) {
    $actualChannels = @($Actual.R, $Actual.G, $Actual.B)
    for ($i=0; $i -lt 3; $i++) { if ([Math]::Abs([int]$actualChannels[$i] - $Expected[$i]) -gt $Tolerance) { throw "$Name did not match its RGB oracle." } }
}
function Test-RgbExact([object]$Rgb, [int[]]$Expected) { return $Rgb.R -eq $Expected[0] -and $Rgb.G -eq $Expected[1] -and $Rgb.B -eq $Expected[2] }
function Get-ArtifactBinding([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $outputRoot = [IO.Path]::GetFullPath($output).TrimEnd('\', '/')
    if (-not $full.StartsWith("$outputRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Artifact must be an existing file contained by OutputDirectory: $Path" }
    $item = Get-Item -LiteralPath $full
    return [ordered]@{ path=[IO.Path]::GetRelativePath($outputRoot, $full).Replace('\','/'); length=$item.Length; sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToUpperInvariant() }
}
function Get-ArtifactBindings() {
    return @(Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.Name -ne 'visual-proof-evidence.json' } | Sort-Object FullName | ForEach-Object { Get-ArtifactBinding $_.FullName })
}
function Get-ExecutionFailureClassification([Exception]$Exception) {
    if ($Exception.Message -match 'oracle') { return 'invalid-oracle' }
    return 'execution-failed'
}
function Write-CapabilityProof([string]$CapabilityId, [string]$Status, [string]$Summary, [int]$CommandStart, [object]$Details) {
    $proof = [ordered]@{ schemaVersion=1; capabilityId=$CapabilityId; status=$Status; executedSemanticProof=($Status -eq 'passed'); summary=$Summary; commands=@($commands | Select-Object -Skip $CommandStart); details=$Details }
    $safe = $CapabilityId.Replace('.', '-')
    [IO.File]::WriteAllText((Join-Path $proofDirectory "$safe.json"), ($proof | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    $proofs.Add($proof)
}

$output = Require-OutsideRepositoryEmptyDirectory $OutputDirectory
if (-not [IO.Path]::IsPathRooted($RuntimeRoot) -or -not (Test-Path -LiteralPath $RuntimeRoot -PathType Container)) { throw 'RuntimeRoot must be an existing explicit rooted directory.' }
if (-not [IO.Path]::IsPathRooted($FixtureRoot) -or -not (Test-Path -LiteralPath $FixtureRoot -PathType Container)) { throw 'FixtureRoot must be an existing explicit rooted directory from Generate-Fixtures.ps1.' }
$runtime = (Resolve-Path -LiteralPath $RuntimeRoot).Path; $fixtures = (Resolve-Path -LiteralPath $FixtureRoot).Path
$semanticContractPath = Join-Path $PSScriptRoot 'semantic-proof-contract.json'
if (-not (Test-Path -LiteralPath $semanticContractPath -PathType Leaf)) { throw 'The approved semantic-proof contract is required.' }
$contract = Get-Content -LiteralPath $semanticContractPath -Raw | ConvertFrom-Json
$capabilities = @($contract.capabilities)
foreach ($id in @('Video.Composite.TransformAlphaAndColor','Video.Transition.CrossDissolveAndBlack','Audio.Waveform.Generate')) { if (-not ($capabilities.id -contains $id)) { throw "Approved semantic-proof contract does not define '$id'." } }
$ffmpeg = Require-Tool (Join-Path $runtime 'bin\ffmpeg.exe') 'ffmpeg.exe' $runtime
$ffprobe = Require-Tool (Join-Path $runtime 'bin\ffprobe.exe') 'ffprobe.exe' $runtime
$work = Join-Path $output 'work'; $media = Join-Path $output 'media'; $logs = Join-Path $work 'logs'; $proofDirectory = Join-Path $output 'capabilities'
New-Item -ItemType Directory -Path $work, $media, $logs, $proofDirectory | Out-Null
$commands = [Collections.Generic.List[object]]::new(); $proofs = [Collections.Generic.List[object]]::new(); $executionFailures = [Collections.Generic.List[string]]::new()

# Identity and fixture integrity are prerequisites; neither is semantic capability proof.
$identityPath = Join-Path $output 'runtime-identity.json'
& (Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1') -RuntimeRoot $runtime -EvidencePath $identityPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $identityPath -PathType Leaf)) { throw 'Approved paired runtime identity validation failed; no visual proof was run.' }
$fixtureInventoryPath = Join-Path $PSScriptRoot 'fixture-source-inventory.json'
if (-not (Test-Path -LiteralPath $fixtureInventoryPath -PathType Leaf)) { throw 'The checked-in fixture source inventory is required.' }
$fixtureValidation = Test-FixtureReport $fixtures $fixtureInventoryPath
Assert-ConsumedInputsInventoried @(
    'F4/f4-stereo-48000-1000hz-opposed.pcm',
    'F5/f5-digital-silence-48000-mono.pcm',
    'F7/f7-red.ppm',
    'F7/f7-green.ppm',
    'F7/f7-black.ppm'
) $fixtureValidation

# Composite deliberately remains unexecuted. The owner has not approved a complete LGPLv3-path
# brightness/contrast/saturation mapping, so proving a convenient subset would be misleading.
$start = $commands.Count
$compositeContract = @($capabilities | Where-Object { $_.id -eq 'Video.Composite.TransformAlphaAndColor' })[0]
if ($compositeContract.status -ne 'blocked') { throw 'Composite proof must not execute without an explicit owner-approved full basic-color mapping.' }
Write-CapabilityProof 'Video.Composite.TransformAlphaAndColor' 'blocked' 'The authoritative contract blocks this capability pending an owner-approved complete basic-color mapping; no composite command was executed.' $start @{ blockedBy=@($compositeContract.blockedBy); approvedFilters=@($compositeContract.components.approvedFilters); candidateMappingPendingOwnerApproval=$compositeContract.candidateMappingPendingOwnerApproval; streamSelectors=@($compositeContract.components.streamSelectors) }

# Transition: declared 25 fps source inputs and xfade endpoints/intermediate, then a two-stage dip via a literal black source.
$start = $commands.Count
try {
    $crossContainer = New-AtomicMedia 'cross-dissolve.mkv' @('-hide_banner','-loop','1','-framerate','25','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F7\f7-red.ppm'),'-loop','1','-framerate','25','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F7\f7-green.ppm'),'-filter_complex','[0:v]trim=duration=0.16,setpts=PTS-STARTPTS,format=rgb24[a];[1:v]trim=duration=0.16,setpts=PTS-STARTPTS,format=rgb24[b];[a][b]xfade=transition=fade:duration=0.08:offset=0.08,format=rgb24[out]','-map','[out]','-c:v','ffv1','-f','matroska')
    $crossTiming = ProbeVideoTimestamps $crossContainer 'cross-dissolve' 6 240
    $cross = DecodeTimestampedFfv1 $crossContainer 'cross-dissolve.rgb'
    $crossFrames = Read-RgbFrames $cross 320 180
    $crossPixels = @($crossFrames | ForEach-Object { Pixel $_ 320 10 10 })
    if (-not (Test-RgbExact $crossPixels[0] @(255,0,0)) -or -not (Test-RgbExact $crossPixels[$crossPixels.Count-1] @(0,255,0)) -or -not ($crossPixels | Where-Object { -not (Test-RgbExact $_ @(255,0,0)) -and -not (Test-RgbExact $_ @(0,255,0)) } | Select-Object -First 1)) { throw 'Cross-dissolve did not contain both endpoints and an intermediate blend.' }
    $dipContainer = New-AtomicMedia 'dip-to-black.mkv' @('-hide_banner','-loop','1','-framerate','25','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F7\f7-red.ppm'),'-loop','1','-framerate','25','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F7\f7-black.ppm'),'-loop','1','-framerate','25','-f','image2','-c:v','ppm','-i',(Join-Path $fixtures 'F7\f7-green.ppm'),'-filter_complex','[0:v]trim=duration=0.16,setpts=PTS-STARTPTS,format=rgb24[a];[1:v]trim=duration=0.16,setpts=PTS-STARTPTS,format=rgb24[b];[2:v]trim=duration=0.16,setpts=PTS-STARTPTS,format=rgb24[c];[a][b]xfade=transition=fade:duration=0.08:offset=0.08[ab];[ab][c]xfade=transition=fade:duration=0.08:offset=0.16,format=rgb24[out]','-map','[out]','-c:v','ffv1','-f','matroska')
    $dipTiming = ProbeVideoTimestamps $dipContainer 'dip-to-black' 8 320
    $dip = DecodeTimestampedFfv1 $dipContainer 'dip-to-black.rgb'
    $dipPixels = @((Read-RgbFrames $dip 320 180) | ForEach-Object { Pixel $_ 320 10 10 })
    $blackIndex = [Array]::FindIndex([object[]]$dipPixels, [Predicate[object]]{ param($pixel) Test-RgbExact $pixel @(0,0,0) })
    $preBlackIntermediate = @($dipPixels[0..($blackIndex - 1)] | Where-Object { -not (Test-RgbExact $_ @(255,0,0)) -and -not (Test-RgbExact $_ @(0,0,0)) })
    $postBlackIntermediate = @($dipPixels[($blackIndex + 1)..($dipPixels.Count - 1)] | Where-Object { -not (Test-RgbExact $_ @(0,255,0)) -and -not (Test-RgbExact $_ @(0,0,0)) })
    if (-not (Test-RgbExact $dipPixels[0] @(255,0,0)) -or -not (Test-RgbExact $dipPixels[$dipPixels.Count-1] @(0,255,0)) -or $blackIndex -le 0 -or $blackIndex -ge ($dipPixels.Count - 1) -or $preBlackIntermediate.Count -eq 0 -or $postBlackIntermediate.Count -eq 0) { throw 'Dip-to/from-black did not prove ordered red-to-intermediate-to-black-to-intermediate-to-green behavior.' }
    Write-CapabilityProof 'Video.Transition.CrossDissolveAndBlack' 'passed' 'Explicit image2/PPM inputs with format/setpts/xfade produced lossless timestamped 25 fps evidence plus ordered cross-dissolve and dip-to/from-black pixel behavior.' $start @{ fixtures=@('F7'); componentSelection=@{demuxer='image2';decoder='ppm';filters=@('format','setpts','xfade');encoder='ffv1';muxer='matroska';decodedOutputEncoder='rawvideo';decodedOutputMuxer='rawvideo';crossInputSelectors=@('0:v:0','1:v:0');dipInputSelectors=@('0:v:0','1:v:0','2:v:0')}; outputs=@($crossContainer,$dipContainer,$cross,$dip); crossTiming=$crossTiming; dipTiming=$dipTiming; crossFrameCount=$crossFrames.Count; dipFrameCount=$dipPixels.Count; blackFrameIndex=$blackIndex }
} catch {
    $classification = Get-ExecutionFailureClassification $_.Exception
    $executionFailures.Add("Video.Transition.CrossDissolveAndBlack: $classification")
    Write-CapabilityProof 'Video.Transition.CrossDissolveAndBlack' $classification 'Transition execution did not complete; this is not an approved contract block.' $start @{ classification=$classification; error=$_.Exception.Message; requiredFilters=@('format','setpts','xfade') }
}

# Waveform: execute twice against the exact PCM primitive and compare raw RGB outputs byte-for-byte by SHA-256.
$start = $commands.Count
try {
    $waveArguments = @('-hide_banner','-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','2','-i',(Join-Path $fixtures 'F4\f4-stereo-48000-1000hz-opposed.pcm'),'-filter_complex','[0:a]aformat=sample_rates=48000:channel_layouts=stereo,showwavespic=s=320x120:colors=white,format=rgb24[v]','-map','[v]','-frames:v','1','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo')
    $waveA = New-AtomicRaw 'waveform-a.rgb' $waveArguments; $waveB = New-AtomicRaw 'waveform-b.rgb' $waveArguments
    $expectedLength = 320 * 120 * 3; $bytes = [IO.File]::ReadAllBytes($waveA)
    if ($bytes.Length -ne $expectedLength) { throw "Waveform geometry oracle failed: expected $expectedLength bytes, got $($bytes.Length)." }
    $values = [Collections.Generic.HashSet[string]]::new(); for ($i=0; $i -lt $bytes.Length; $i+=3) { [void]$values.Add("$($bytes[$i]),$($bytes[$i+1]),$($bytes[$i+2])") }
    if ($values.Count -lt 2) { throw 'Waveform did not contain both background and waveform pixels.' }
    $toneWavePixels = @($bytes | Where-Object { $_ -gt 16 }).Count
    if ($toneWavePixels -lt 100) { throw 'Known 1000 Hz tone did not produce the minimum independently bounded waveform content.' }
    $silenceArguments = @('-hide_banner','-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','1','-i',(Join-Path $fixtures 'F5\f5-digital-silence-48000-mono.pcm'),'-filter_complex','[0:a]aformat=sample_rates=48000:channel_layouts=mono,showwavespic=s=320x120:colors=white,format=rgb24[v]','-map','[v]','-frames:v','1','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo')
    $silenceWave = New-AtomicRaw 'waveform-silence.rgb' $silenceArguments
    $silenceBytes = [IO.File]::ReadAllBytes($silenceWave)
    if ($silenceBytes.Length -ne $expectedLength) { throw 'Digital-silence waveform geometry oracle failed.' }
    $silenceWavePixels = @($silenceBytes | Where-Object { $_ -gt 16 }).Count
    if ($silenceWavePixels -ne 0) { throw 'Digital-silence waveform contained a wave excursion.' }
    if ([Linq.Enumerable]::SequenceEqual[byte]($bytes, $silenceBytes)) { throw 'Known tone waveform did not differ from digital silence.' }
    $waveAHash = (Get-FileHash -LiteralPath $waveA -Algorithm SHA256).Hash.ToUpperInvariant(); $waveBHash = (Get-FileHash -LiteralPath $waveB -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($waveAHash -ne $waveBHash) { throw 'Repeated waveform generation was not deterministic.' }
    Write-CapabilityProof 'Audio.Waveform.Generate' 'passed' 'Explicit PCM tone and digital-silence inputs proved source-sensitive waveform amplitude bounds, distinct output, geometry, and repeat SHA-256 determinism.' $start @{ componentSelection=@{demuxer='s16le';decoder='pcm_s16le';filters=@('aformat','showwavespic','format');encoder='rawvideo';muxer='rawvideo';streamSelector='0:a:0'}; outputs=@($waveA,$waveB,$silenceWave); expectedBytes=$expectedLength; distinctPixels=$values.Count; toneWavePixels=$toneWavePixels; silenceWavePixels=$silenceWavePixels; sha256=$waveAHash }
} catch {
    $classification = Get-ExecutionFailureClassification $_.Exception
    $executionFailures.Add("Audio.Waveform.Generate: $classification")
    Write-CapabilityProof 'Audio.Waveform.Generate' $classification 'Waveform execution did not complete; this is not an approved contract block.' $start @{ classification=$classification; error=$_.Exception.Message; requiredFilters=@('aformat','showwavespic','format') }
}
finally {
    $evidence = [ordered]@{ schemaVersion=1; profileId='P2.BtbnLgplShared.WindowsX64.20260820'; succeeded=($executionFailures.Count -eq 0); executionFailures=$executionFailures; semanticProofContractProfileId=$contract.profileId; runtimeIdentityEvidence='runtime-identity.json'; fixtureReportVerified=$true; fixtureReportPath=$FixtureRoot; componentPresence='See runtime-identity.json; presence is not semantic proof.'; commands=$commands; semanticProofs=$proofs; artifacts=(Get-ArtifactBindings) }
    [IO.File]::WriteAllText((Join-Path $output 'visual-proof-evidence.json'), ($evidence | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
}
if ($executionFailures.Count -ne 0) { throw "Visual proof execution failed: $($executionFailures -join '; ')" }
