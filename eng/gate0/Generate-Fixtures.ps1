[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$FfmpegPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$FfprobePath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ApprovedRuntimeRoot,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    # TEST ONLY: Production proof must use the checked-in fixture-source-inventory.json default.
    [Parameter(HelpMessage = 'TEST ONLY. Overrides the checked-in inventory for negative generator tests; it is never an approved inventory.')]
    [string]$FixtureSourceInventoryPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ExplicitToolPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$RuntimeRoot
    )

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        throw "$Name must be an explicit rooted path. PATH fallback is prohibited."
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name does not exist at the explicit path '$Path'."
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $RuntimeRoot).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = "$resolvedRoot$([System.IO.Path]::DirectorySeparatorChar)"

    if (-not $resolvedPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must resolve under ApprovedRuntimeRoot. PATH fallback and mixed runtime pairs are prohibited."
    }

    return $resolvedPath
}

function Write-Ppm {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [Parameter(Mandatory)][byte[]]$Rgb
    )

    $header = [System.Text.Encoding]::ASCII.GetBytes("P6`n$Width $Height`n255`n")
    $bytes = New-Object byte[] ($header.Length + ($Width * $Height * 3))
    [System.Buffer]::BlockCopy($header, 0, $bytes, 0, $header.Length)
    for ($pixel = 0; $pixel -lt ($Width * $Height); $pixel++) {
        [System.Buffer]::BlockCopy($Rgb, 0, $bytes, $header.Length + ($pixel * 3), 3)
    }
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

function Write-F1PatternPpm {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$FrameNumber
    )

    $width = 320
    $height = 180
    $header = [System.Text.Encoding]::ASCII.GetBytes("P6`n$width $height`n255`n")
    $bytes = New-Object byte[] ($header.Length + ($width * $height * 3))
    [System.Buffer]::BlockCopy($header, 0, $bytes, 0, $header.Length)
    $bars = @(
        [byte[]](255, 255, 255), [byte[]](255, 255, 0), [byte[]](0, 255, 255), [byte[]](0, 255, 0),
        [byte[]](255, 0, 255), [byte[]](255, 0, 0), [byte[]](0, 0, 255), [byte[]](0, 0, 0)
    )
    $digits = @(
        @('11111', '10001', '10001', '10001', '10001', '10001', '11111'),
        @('00100', '01100', '00100', '00100', '00100', '00100', '01110'),
        @('11111', '00001', '00001', '11111', '10000', '10000', '11111')
    )
    $safeX = [int]($width * 0.1)
    $safeY = [int]($height * 0.1)
    $digitScale = 4
    $digitX = $width - (5 * $digitScale) - 12
    $digitY = $height - (7 * $digitScale) - 12

    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $barIndex = [Math]::Min(7, [int]($x * 8 / $width))
            $rgb = $bars[$barIndex]
            $onSafeBorder = (($x -ge $safeX -and $x -lt $safeX + 2) -or ($x -ge $width - $safeX - 2 -and $x -lt $width - $safeX)) -and $y -ge $safeY -and $y -lt $height - $safeY
            $onSafeBorder = $onSafeBorder -or ((($y -ge $safeY -and $y -lt $safeY + 2) -or ($y -ge $height - $safeY - 2 -and $y -lt $height - $safeY)) -and $x -ge $safeX -and $x -lt $width - $safeX)

            $inDigitField = $x -ge $digitX - 4 -and $x -lt $digitX + (5 * $digitScale) + 4 -and $y -ge $digitY - 4 -and $y -lt $digitY + (7 * $digitScale) + 4
            $onDigit = $false
            if ($x -ge $digitX -and $x -lt $digitX + (5 * $digitScale) -and $y -ge $digitY -and $y -lt $digitY + (7 * $digitScale)) {
                $digitColumn = [Math]::Floor(($x - $digitX) / $digitScale)
                $digitRow = [Math]::Floor(($y - $digitY) / $digitScale)
                $onDigit = $digits[$FrameNumber][$digitRow][$digitColumn] -eq '1'
            }

            if ($onSafeBorder -or $onDigit) {
                $rgb = [byte[]](255, 255, 255)
            }
            elseif ($inDigitField) {
                $rgb = [byte[]](0, 0, 0)
            }

            $offset = $header.Length + (($y * $width + $x) * 3)
            [System.Buffer]::BlockCopy($rgb, 0, $bytes, $offset, 3)
        }
    }
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

function Write-Rgba {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [Parameter(Mandatory)][byte[]]$Rgba
    )

    $bytes = New-Object byte[] ($Width * $Height * 4)
    for ($pixel = 0; $pixel -lt ($Width * $Height); $pixel++) {
        [System.Buffer]::BlockCopy($Rgba, 0, $bytes, $pixel * 4, 4)
    }
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

function Write-F3ColorOraclePpm {
    param([Parameter(Mandatory)][string]$Path)

    # Four authored, full-frame regions deliberately separate the visual
    # semantics exercised by the approved P2 proof.  The values are not a UI
    # parameter model; they are stable source primitives for independent
    # brightness, contrast, and saturation observations.
    $width = 320
    $height = 180
    $header = [System.Text.Encoding]::ASCII.GetBytes("P6`n$width $height`n255`n")
    $bytes = New-Object byte[] ($header.Length + ($width * $height * 3))
    [System.Buffer]::BlockCopy($header, 0, $bytes, 0, $header.Length)
    [byte[][]]$regions = @(
        [byte[]](128, 128, 128), # brightness source
        [byte[]](64, 64, 64),    # contrast low source
        [byte[]](192, 192, 192), # contrast high source
        [byte[]](200, 100, 50)   # saturation source
    )
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $region = [Math]::Min(3, [int][Math]::Floor($x * 4 / $width))
            $offset = $header.Length + (($y * $width + $x) * 3)
            [System.Buffer]::BlockCopy($regions[$region], 0, $bytes, $offset, 3)
        }
    }
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

function Write-SinePcm16Le {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$SampleRate,
        [Parameter(Mandatory)][int]$Channels,
        [Parameter(Mandatory)][double[]]$ToneHz,
        [double[]]$PhaseRadians = @(),
        [Parameter(Mandatory)][int]$DurationMilliseconds
    )

    $sampleCount = [int]($SampleRate * $DurationMilliseconds / 1000)
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    try {
        $writer = New-Object System.IO.BinaryWriter($stream)
        try {
            for ($sample = 0; $sample -lt $sampleCount; $sample++) {
                for ($channel = 0; $channel -lt $Channels; $channel++) {
                    $tone = $ToneHz[$channel % $ToneHz.Length]
                    $phase = if ($PhaseRadians.Length -eq 0) { 0 } else { $PhaseRadians[$channel % $PhaseRadians.Length] }
                    $value = [int16][Math]::Round(12000 * [Math]::Sin((2 * [Math]::PI * $tone * $sample / $SampleRate) + $phase))
                    $writer.Write($value)
                }
            }
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ContainedRelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = "$resolvedRoot$([System.IO.Path]::DirectorySeparatorChar)"
    if (-not $resolvedPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description must resolve under '$resolvedRoot'."
    }

    $relativePath = [System.IO.Path]::GetRelativePath($resolvedRoot, $resolvedPath).Replace('\', '/')
    if ([System.IO.Path]::IsPathRooted($relativePath) -or $relativePath -eq '..' -or $relativePath.StartsWith('../', [System.StringComparison]::Ordinal)) {
        throw "$Description has an unsafe relative path '$relativePath'."
    }

    return $relativePath
}

function Get-OutputFileInventory {
    param([Parameter(Mandatory)][string]$Root)

    $files = Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName
    foreach ($file in $files) {
        if ($file.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
            throw "Generated output must not contain reparse-point files: '$($file.FullName)'."
        }
    }

    return @($files | ForEach-Object {
        [ordered]@{
            path = Get-ContainedRelativePath -Root $Root -Path $_.FullName -Description 'Generated output file'
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        }
    })
}

function Read-ApprovedInventory {
    param([Parameter(Mandatory)][string]$Path)

    if (-not [System.IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'FixtureSourceInventoryPath must be an existing explicit rooted file path.'
    }

    $inventory = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    if ($inventory.schemaVersion -ne 1 -or $inventory.inventoryVersion -ne 1) {
        throw 'Fixture source inventory schemaVersion and inventoryVersion must both be 1.'
    }
    if ($inventory.profileId -ne 'P2.BtbnLgplShared.WindowsX64.20260820') {
        throw 'Fixture source inventory profileId does not match the approved Gate 0 profile.'
    }
    if ($null -eq $inventory.files -or $inventory.files.Count -eq 0) {
        throw 'Fixture source inventory must define at least one expected file.'
    }

    $paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($file in $inventory.files) {
        $path = [string]$file.path
        if ([string]::IsNullOrWhiteSpace($path) -or [System.IO.Path]::IsPathRooted($path) -or $path.Contains('\') -or $path -eq '..' -or $path.StartsWith('../', [System.StringComparison]::Ordinal) -or $path.Contains('/../')) {
            throw "Fixture source inventory contains unsafe path '$path'."
        }
        if ($file.length -lt 0 -or [string]::IsNullOrWhiteSpace([string]$file.sha256) -or ([string]$file.sha256) -notmatch '^[A-F0-9]{64}$') {
            throw "Fixture source inventory has invalid metadata for '$path'."
        }
        if (-not $paths.Add($path)) {
            throw "Fixture source inventory contains duplicate path '$path'."
        }
    }

    return $inventory
}

function Assert-ApprovedInventoryMatch {
    param(
        [Parameter(Mandatory)][object]$ApprovedInventory,
        [Parameter(Mandatory)][object[]]$ActualFiles
    )

    $expectedByPath = @{}
    foreach ($expected in $ApprovedInventory.files) { $expectedByPath[[string]$expected.path] = $expected }
    $actualByPath = @{}
    foreach ($actual in $ActualFiles) { $actualByPath[[string]$actual.path] = $actual }

    $missing = @($expectedByPath.Keys | Where-Object { -not $actualByPath.ContainsKey($_) } | Sort-Object)
    $additional = @($actualByPath.Keys | Where-Object { -not $expectedByPath.ContainsKey($_) } | Sort-Object)
    $drifted = @($expectedByPath.Keys | Where-Object {
        $actualByPath.ContainsKey($_) -and (($expectedByPath[$_].length -ne $actualByPath[$_].length) -or ($expectedByPath[$_].sha256 -ne $actualByPath[$_].sha256))
    } | Sort-Object)

    if ($missing.Count -gt 0 -or $additional.Count -gt 0 -or $drifted.Count -gt 0) {
        throw "Generated fixture output does not match the approved inventory. Missing: $($missing -join ', '). Additional: $($additional -join ', '). Drifted: $($drifted -join ', ')."
    }
}

function Assert-SafeOutputDirectoryPreWrite {
    param(
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    # Walk from the requested path to the nearest existing ancestor, then inspect
    # every existing ancestor before any output directory or file is created.
    $existingAncestor = $OutputPath
    while (-not (Test-Path -LiteralPath $existingAncestor)) {
        $parent = Split-Path -Parent $existingAncestor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($existingAncestor, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "OutputDirectory has no resolvable existing ancestor: '$OutputPath'."
        }
        $existingAncestor = $parent
    }

    $ancestor = $existingAncestor
    while ($true) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if ($item.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
            throw "OutputDirectory ancestor '$ancestor' is a reparse point. Reparse-point output paths are prohibited."
        }

        $parent = Split-Path -Parent $ancestor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($ancestor, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $ancestor = $parent
    }

    $resolvedExistingAncestor = (Resolve-Path -LiteralPath $existingAncestor).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $repositoryPrefix = "$RepositoryRoot$([System.IO.Path]::DirectorySeparatorChar)"
    if ($resolvedExistingAncestor.Equals($RepositoryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or $resolvedExistingAncestor.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDirectory resolves through an existing ancestor inside the repository: '$resolvedExistingAncestor'."
    }
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
$manifestPath = Join-Path $scriptDirectory 'fixture-manifest.json'
$truthPath = Join-Path $scriptDirectory 'expected-truths.json'
$defaultInventoryPath = Join-Path $scriptDirectory 'fixture-source-inventory.json'
if ([string]::IsNullOrWhiteSpace($FixtureSourceInventoryPath)) {
    $FixtureSourceInventoryPath = $defaultInventoryPath
}
$usesTestOnlyInventoryOverride = -not [System.IO.Path]::GetFullPath($FixtureSourceInventoryPath).Equals([System.IO.Path]::GetFullPath($defaultInventoryPath), [System.StringComparison]::OrdinalIgnoreCase)
$approvedInventory = Read-ApprovedInventory -Path $FixtureSourceInventoryPath

if (-not [System.IO.Path]::IsPathRooted($ApprovedRuntimeRoot) -or -not (Test-Path -LiteralPath $ApprovedRuntimeRoot -PathType Container)) {
    throw 'ApprovedRuntimeRoot must be an existing explicit rooted directory.'
}
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    throw 'OutputDirectory must be an explicit rooted path outside the repository.'
}

$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory '..\..')).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$outputEqualsRepository = $resolvedOutput.Equals($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)
$repositoryPrefix = "$repositoryRoot$([System.IO.Path]::DirectorySeparatorChar)"
$outputIsInsideRepository = $resolvedOutput.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)
if ($outputEqualsRepository -or $outputIsInsideRepository) {
    throw 'OutputDirectory must be outside the repository so generated media cannot be committed.'
}
Assert-SafeOutputDirectoryPreWrite -OutputPath $resolvedOutput -RepositoryRoot $repositoryRoot

$resolvedFfmpegPath = Resolve-ExplicitToolPath -Path $FfmpegPath -Name 'ffmpeg.exe' -RuntimeRoot $ApprovedRuntimeRoot
$resolvedFfprobePath = Resolve-ExplicitToolPath -Path $FfprobePath -Name 'ffprobe.exe' -RuntimeRoot $ApprovedRuntimeRoot

if (Test-Path -LiteralPath $resolvedOutput) {
    if (-not (Test-Path -LiteralPath $resolvedOutput -PathType Container)) {
        throw 'OutputDirectory must identify a directory.'
    }

    if (Get-ChildItem -LiteralPath $resolvedOutput -Force | Select-Object -First 1) {
        throw 'OutputDirectory must be new or empty so fixture evidence cannot include stale files.'
    }
}
else {
    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
}

foreach ($fixture in 'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8') {
    New-Item -ItemType Directory -Path (Join-Path $resolvedOutput $fixture) -Force | Out-Null
}

# F1: authored color bars, safe-area marker, frame-number glyphs, and synchronized stereo tones.
Write-F1PatternPpm (Join-Path $resolvedOutput 'F1\f1-pattern-000.ppm') 0
Write-F1PatternPpm (Join-Path $resolvedOutput 'F1\f1-pattern-001.ppm') 1
Write-F1PatternPpm (Join-Path $resolvedOutput 'F1\f1-pattern-002.ppm') 2
Write-SinePcm16Le (Join-Path $resolvedOutput 'F1\f1-sync-440hz-880hz-48000-stereo.pcm') 48000 2 ([double[]](440, 880)) ([double[]](0, 0)) 120

# F2: deliberately mismatched geometry/cadence and audio properties.
Write-Ppm (Join-Path $resolvedOutput 'F2\f2-landscape-640x360-25fps.ppm') 640 360 ([byte[]](255, 128, 0))
Write-Ppm (Join-Path $resolvedOutput 'F2\f2-portrait-360x640-30000_1001fps.ppm') 360 640 ([byte[]](0, 128, 255))
Write-SinePcm16Le (Join-Path $resolvedOutput 'F2\f2-44100-mono-330hz.pcm') 44100 1 ([double[]](330)) ([double[]](0)) 250
Write-SinePcm16Le (Join-Path $resolvedOutput 'F2\f2-48000-stereo-660hz.pcm') 48000 2 ([double[]](660, 660)) ([double[]](0, 0)) 250

# F3: alpha source, basic-color oracle, and inventory-bound text proof inputs.
# These are authored outside Git by this deterministic generator.  The text
# proof consumes the logical/layout/ASS primitives rather than synthesising
# an unrecorded subtitle document at execution time.
Write-Rgba (Join-Path $resolvedOutput 'F3\f3-alpha-magenta-50pct.rgba') 320 180 ([byte[]](255, 0, 255, 128))
Write-F3ColorOraclePpm (Join-Path $resolvedOutput 'F3\f3-basic-color-oracle.ppm')
Write-Ppm (Join-Path $resolvedOutput 'F3\f3-text-background.ppm') 320 180 ([byte[]](24, 31, 42))
$f3TextSpecification = [ordered]@{
    fixtureId = 'F3'
    unicodeText = 'ReelForge — 你好 — مرحبا'
    titleText = 'ReelForge — 你好 — مرحبا'
    captionText = 'A reproducible caption with diacritics: café — 你好 — مرحبا'
    optionalBlockedColorEmoji = '🎬'
    fontPrerequisite = [ordered]@{
        id = 'Font.Licensed.UnicodeTestFont'
        status = 'approved-artifacts-ready-for-proof'
        systemFontFallback = 'prohibited'
    }
}
[System.IO.File]::WriteAllText((Join-Path $resolvedOutput 'F3\f3-unicode-text.json'), ($f3TextSpecification | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
$f3LayoutSpecification = [ordered]@{
    fixtureId = 'F3'
    canvas = [ordered]@{ width = 320; height = 180; safeInsetPixels = 18 }
    title = [ordered]@{ region = 'top-safe'; anchor = 'top-center'; x = 160; y = 24; maximumWidth = 284; expectedLineBands = 1 }
    caption = [ordered]@{ region = 'bottom-safe'; anchor = 'bottom-center'; x = 160; y = 156; maximumWidth = 264; expectedLineBands = 2 }
    textRuns = @(
        [ordered]@{ family = 'Noto Sans'; text = 'ReelForge — '; role = 'latin-punctuation-diacritics' },
        [ordered]@{ family = 'Noto Sans CJK SC'; text = '你好'; role = 'simplified-chinese' },
        [ordered]@{ family = 'Noto Sans'; text = ' — '; role = 'latin-punctuation' },
        [ordered]@{ family = 'Noto Sans Arabic'; text = 'مرحبا'; role = 'arabic' }
    )
    titleText = 'ReelForge — 你好 — مرحبا'
    captionText = 'A reproducible caption with diacritics: café — 你好 — مرحبا'
}
[System.IO.File]::WriteAllText((Join-Path $resolvedOutput 'F3\f3-text-layout.json'), ($f3LayoutSpecification | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))
$f3Ass = @"
[Script Info]
ScriptType: v4.00+
PlayResX: 320
PlayResY: 180
WrapStyle: 0
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding
Style: Title,Noto Sans,24,&H00FFFFFF,&H000000FF,&H0018202A,&H0018202A,0,0,0,0,100,100,0,0,1,1,0,8,18,18,18,1
Style: Caption,Noto Sans,18,&H00FFFFFF,&H000000FF,&H0018202A,&H0018202A,0,0,0,0,100,100,0,0,1,1,0,2,28,28,18,1

[Events]
Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text
Dialogue: 0,0:00:00.00,0:00:01.00,Title,,0,0,0,,{\an8\pos(160,24)\q0}{\fnNoto Sans}ReelForge — {\fnNoto Sans CJK SC}你好{\fnNoto Sans} — {\fnNoto Sans Arabic}مرحبا
Dialogue: 0,0:00:00.00,0:00:01.00,Caption,,0,0,0,,{\an2\pos(160,156)\q0}{\fnNoto Sans}A reproducible caption with diacritics: café — {\fnNoto Sans CJK SC}你好{\fnNoto Sans} — {\fnNoto Sans Arabic}مرحبا
"@
[System.IO.File]::WriteAllText((Join-Path $resolvedOutput 'F3\f3-unicode-proof.ass'), $f3Ass.TrimStart([Environment]::NewLine.ToCharArray()), [System.Text.UTF8Encoding]::new($false))
$f3ArabicAss = @"
[Script Info]
ScriptType: v4.00+
PlayResX: 320
PlayResY: 180

[V4+ Styles]
Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding
Style: Arabic,Noto Sans Arabic,28,&H00FFFFFF,&H000000FF,&H0018202A,&H0018202A,0,0,0,0,100,100,0,0,1,1,0,5,18,18,18,1

[Events]
Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text
Dialogue: 0,0:00:00.00,0:00:01.00,Arabic,,0,0,0,,{\an5\pos(160,90)}{\fnNoto Sans Arabic}مرحبا
"@
[System.IO.File]::WriteAllText((Join-Path $resolvedOutput 'F3\f3-arabic-shaping-oracle.ass'), $f3ArabicAss.TrimStart([Environment]::NewLine.ToCharArray()), [System.Text.UTF8Encoding]::new($false))

# F4 and F8: distinguishable PCM primitives.
Write-SinePcm16Le (Join-Path $resolvedOutput 'F4\f4-mono-32000-1000hz.pcm') 32000 1 ([double[]](1000)) ([double[]](0)) 500
Write-SinePcm16Le (Join-Path $resolvedOutput 'F4\f4-mono-44100-1000hz.pcm') 44100 1 ([double[]](1000)) ([double[]](0)) 500
Write-SinePcm16Le (Join-Path $resolvedOutput 'F4\f4-stereo-48000-1000hz-opposed.pcm') 48000 2 ([double[]](1000, 1000)) ([double[]](0, [Math]::PI)) 500

# F5: an intentionally video-only opaque source.
Write-Ppm (Join-Path $resolvedOutput 'F5\f5-silent-yellow.ppm') 320 180 ([byte[]](255, 255, 0))
Write-SinePcm16Le (Join-Path $resolvedOutput 'F5\f5-digital-silence-48000-mono.pcm') 48000 1 ([double[]](0)) ([double[]](0)) 250

# F6: the long-form fixture is a compact repeat recipe, not an hour of committed/generated media.
$longFormRecipe = [ordered]@{
    schemaVersion = 1
    fixtureId = 'F6'
    sourceFixtureId = 'F1'
    repeatCount = 30000
    segmentDurationMilliseconds = 120
    expectedDurationSeconds = 3600
}
[System.IO.File]::WriteAllText((Join-Path $resolvedOutput 'F6\f6-long-form-recipe.json'), ($longFormRecipe | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))

# F7: frame identity in intended presentation order, with non-zero VFR timestamps.
Write-Ppm (Join-Path $resolvedOutput 'F7\f7-red.ppm') 320 180 ([byte[]](255, 0, 0))
Write-Ppm (Join-Path $resolvedOutput 'F7\f7-green.ppm') 320 180 ([byte[]](0, 255, 0))
Write-Ppm (Join-Path $resolvedOutput 'F7\f7-blue.ppm') 320 180 ([byte[]](0, 0, 255))
Write-Ppm (Join-Path $resolvedOutput 'F7\f7-white.ppm') 320 180 ([byte[]](255, 255, 255))
Write-Ppm (Join-Path $resolvedOutput 'F7\f7-black.ppm') 320 180 ([byte[]](0, 0, 0))

# F8: two video and two audio sources that must be addressed by explicit stream map.
Write-Ppm (Join-Path $resolvedOutput 'F8\f8-video-zero-red.ppm') 320 180 ([byte[]](255, 0, 0))
Write-Ppm (Join-Path $resolvedOutput 'F8\f8-video-one-green.ppm') 160 90 ([byte[]](0, 255, 0))
Write-SinePcm16Le (Join-Path $resolvedOutput 'F8\f8-audio-zero-440hz.pcm') 48000 1 ([double[]](440)) ([double[]](0)) 250
Write-SinePcm16Le (Join-Path $resolvedOutput 'F8\f8-audio-one-880hz.pcm') 48000 1 ([double[]](880)) ([double[]](0)) 250

[System.IO.File]::Copy($manifestPath, (Join-Path $resolvedOutput 'fixture-manifest.json'), $true)
[System.IO.File]::Copy($truthPath, (Join-Path $resolvedOutput 'expected-truths.json'), $true)

$sourceFiles = Get-OutputFileInventory -Root $resolvedOutput
Assert-ApprovedInventoryMatch -ApprovedInventory $approvedInventory -ActualFiles $sourceFiles

$sourceSet = @($PSCommandPath, $manifestPath, $truthPath, $FixtureSourceInventoryPath | ForEach-Object {
    [ordered]@{
        path = if ([System.IO.Path]::GetFullPath($_).StartsWith("$repositoryRoot$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::OrdinalIgnoreCase)) {
            Get-ContainedRelativePath -Root $repositoryRoot -Path $_ -Description 'Generator source'
        } else {
            "external/$([System.IO.Path]::GetFileName($_))"
        }
        length = (Get-Item -LiteralPath $_).Length
        sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToUpperInvariant()
    }
})

$generationReport = [ordered]@{
    schemaVersion = 1
    generatorVersion = 1
    profileId = 'P2.BtbnLgplShared.WindowsX64.20260820'
    contextOnly = [ordered]@{
        approvedRuntimeRoot = (Resolve-Path -LiteralPath $ApprovedRuntimeRoot).Path
        ffmpegPath = $resolvedFfmpegPath
        ffprobePath = $resolvedFfprobePath
    }
    externalMediaCommandsExecuted = $false
    approvedInventory = [ordered]@{
        approvalStatus = if ($usesTestOnlyInventoryOverride) { 'test-only override; not approved for Gate 0 proof' } else { 'checked-in approved inventory' }
        testOnlyOverride = $usesTestOnlyInventoryOverride
        schemaVersion = $approvedInventory.schemaVersion
        inventoryVersion = $approvedInventory.inventoryVersion
        path = if ([System.IO.Path]::GetFullPath($FixtureSourceInventoryPath).StartsWith("$repositoryRoot$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::OrdinalIgnoreCase)) {
            Get-ContainedRelativePath -Root $repositoryRoot -Path $FixtureSourceInventoryPath -Description 'Fixture source inventory'
        } else {
            "external/$([System.IO.Path]::GetFileName($FixtureSourceInventoryPath))"
        }
        sha256 = (Get-FileHash -LiteralPath $FixtureSourceInventoryPath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
    generatorSourceSet = $sourceSet
    sourceFiles = $sourceFiles
}
[System.IO.File]::WriteAllText(
    (Join-Path $resolvedOutput 'generated-fixture-report.json'),
    ($generationReport | ConvertTo-Json -Depth 6),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Generated deterministic Gate 0 source primitives at '$resolvedOutput'. No FFmpeg command was executed."
