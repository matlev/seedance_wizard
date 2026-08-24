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
    [string]$OutputDirectory
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

$scriptDirectory = Split-Path -Parent $PSCommandPath
$manifestPath = Join-Path $scriptDirectory 'fixture-manifest.json'
$truthPath = Join-Path $scriptDirectory 'expected-truths.json'

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

# F3: alpha source only. Text remains blocked until a licensed, pinned test font exists.
Write-Rgba (Join-Path $resolvedOutput 'F3\f3-alpha-magenta-50pct.rgba') 320 180 ([byte[]](255, 0, 255, 128))
$f3TextSpecification = [ordered]@{
    fixtureId = 'F3'
    unicodeText = 'ReelForge — 你好 — مرحبا — 🎬'
    fontPrerequisite = [ordered]@{
        id = 'Font.Licensed.UnicodeTestFont'
        status = 'blocked'
        systemFontFallback = 'prohibited'
    }
}
[System.IO.File]::WriteAllText((Join-Path $resolvedOutput 'F3\f3-unicode-text.json'), ($f3TextSpecification | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))

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

$sourceFiles = @(Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse |
    Where-Object Name -ne 'generated-fixture-report.json' |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($resolvedOutput, $_.FullName).Replace('\', '/')
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
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
    sourceFiles = $sourceFiles
}
[System.IO.File]::WriteAllText(
    (Join-Path $resolvedOutput 'generated-fixture-report.json'),
    ($generationReport | ConvertTo-Json -Depth 6),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Generated deterministic Gate 0 source primitives at '$resolvedOutput'. No FFmpeg command was executed."
