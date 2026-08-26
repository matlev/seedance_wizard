[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FixtureRoot,
    [Parameter(Mandatory)] [string] $ContractPath,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-NewDirectory([string] $Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'OutputDirectory must be absolute.' }
    if (Test-Path -LiteralPath $Path) { throw 'OutputDirectory must be new.' }
    [IO.Directory]::CreateDirectory($Path) | Out-Null
}

function Assert-Source([string] $RelativePath,[string] $ExpectedSha256) {
    $root = [IO.Path]::GetFullPath($FixtureRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $path = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture source escaped FixtureRoot.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing fixture source: $RelativePath" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($hash -ne $ExpectedSha256) { throw "Fixture source hash mismatch: $RelativePath" }
    return $path
}

if (-not ('ReelForge.Gate0.Stage2AudioTruth' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;

namespace ReelForge.Gate0 {
  public sealed class AudioTruthTrack {
    public string Path { get; set; } = "";
    public int Channels { get; set; }
    public int StartSample { get; set; }
    public double Gain { get; set; }
    public double LeftFromLeft { get; set; }
    public double LeftFromRight { get; set; }
    public double RightFromLeft { get; set; }
    public double RightFromRight { get; set; }
  }

  public static class Stage2AudioTruth {
    public static void Render(string outputPath, int sampleCount, AudioTruthTrack[] tracks) {
      var sources = new List<short[]>();
      foreach (var track in tracks) {
        var bytes = File.ReadAllBytes(track.Path);
        if (bytes.Length == 0 || bytes.Length % (track.Channels * 2) != 0) throw new InvalidOperationException("Invalid PCM source closure.");
        var samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        sources.Add(samples);
      }

      using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      using var writer = new BinaryWriter(stream);
      for (var n = 0; n < sampleCount; n++) {
        double left = 0;
        double right = 0;
        for (var i = 0; i < tracks.Length; i++) {
          var track = tracks[i];
          if (n < track.StartSample) continue;
          var source = sources[i];
          var sourceFrames = source.Length / track.Channels;
          var sourceFrame = (n - track.StartSample) % sourceFrames;
          var sourceLeft = source[sourceFrame * track.Channels];
          var sourceRight = track.Channels == 1 ? sourceLeft : source[sourceFrame * track.Channels + 1];
          left += track.Gain * ((track.LeftFromLeft * sourceLeft) + (track.LeftFromRight * sourceRight));
          right += track.Gain * ((track.RightFromLeft * sourceLeft) + (track.RightFromRight * sourceRight));
        }
        writer.Write(ToInt16(left));
        writer.Write(ToInt16(right));
      }
    }

    private static short ToInt16(double value) {
      var rounded = Math.Round(value, MidpointRounding.ToEven);
      return (short)Math.Clamp(rounded, short.MinValue, short.MaxValue);
    }
  }
}
'@
}

Assert-NewDirectory $OutputDirectory
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.Path]::IsPathRooted($ContractPath) -or -not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) { throw 'ContractPath must name an existing absolute file.' }
$contractFullPath = [IO.Path]::GetFullPath($ContractPath)
$contract = Get-Content -Raw -LiteralPath $contractFullPath | ConvertFrom-Json
if ($contract.contractId -ne 'Gate0.G05.Stage2.Workloads.V1.Proposed') { throw 'Unexpected Stage 2 workload contract.' }

$sources = @{
    'f2-audio-660' = [pscustomobject]@{ Path = Assert-Source 'F2/f2-48000-stereo-660hz.pcm' 'E968B1F57ABEE65623E597ADDC847A33CF29FF442A1E9E191E8CE006EE9EB38B'; Channels = 2 }
    'f4-audio-1000' = [pscustomobject]@{ Path = Assert-Source 'F4/f4-stereo-48000-1000hz-opposed.pcm' 'C74C03F83E587027964B0480D52CB20ECE2EFDA79F71D3E8F2BC8B0FC6197FAE'; Channels = 2 }
    'f8-audio-440' = [pscustomobject]@{ Path = Assert-Source 'F8/f8-audio-zero-440hz.pcm' 'B3C7DAB38E8DE0958EB1A7C6F603407D103B3D1D414FED07942CD62F95B73942'; Channels = 1 }
    'f8-audio-880' = [pscustomobject]@{ Path = Assert-Source 'F8/f8-audio-one-880hz.pcm' 'C24C111CD44439A11A698C4AF64E53B5B5BAAF38AEAC0BB2B8EF5B20B4672828'; Channels = 1 }
}

function New-Track([string] $Path,[int] $Channels,[int] $StartMs,[double] $Gain,[double] $LL,[double] $LR,[double] $RL,[double] $RR) {
    $track = [ReelForge.Gate0.AudioTruthTrack]::new()
    $track.Path = $Path
    $track.Channels = $Channels
    $track.StartSample = 48 * $StartMs
    $track.Gain = $Gain
    $track.LeftFromLeft = $LL
    $track.LeftFromRight = $LR
    $track.RightFromLeft = $RL
    $track.RightFromRight = $RR
    return $track
}

function New-TrackFromRecipe($Recipe) {
    if (-not $Recipe.loop) { throw "Stage 2 audio truth requires a looping track: $($Recipe.id)" }
    if (-not $sources.ContainsKey([string]$Recipe.source)) { throw "Unknown Stage 2 audio source: $($Recipe.source)" }
    $source = $sources[[string]$Recipe.source]
    $matrix = switch ([string]$Recipe.pan) {
        'identity-stereo' { @(1.0, 0.0, 0.0, 1.0); break }
        'stereo|c0=c0|c1=0.25*c0' { @(1.0, 0.0, 0.25, 0.0); break }
        'stereo|c0=0.25*c0|c1=c0' { @(0.25, 0.0, 1.0, 0.0); break }
        'stereo|c0=c0|c1=0.20*c0' { @(1.0, 0.0, 0.20, 0.0); break }
        'stereo|c0=0.20*c0|c1=c0' { @(0.20, 0.0, 1.0, 0.0); break }
        'stereo|c0=0.35*c0|c1=c0' { @(0.35, 0.0, 1.0, 0.0); break }
        'stereo|c0=c0|c1=0.35*c1' { @(1.0, 0.0, 0.0, 0.35); break }
        'stereo|c0=c0|c1=0.35*c0' { @(1.0, 0.0, 0.35, 0.0); break }
        'stereo|c0=0.35*c0|c1=c1' { @(0.35, 0.0, 0.0, 1.0); break }
        default { throw "Unknown Stage 2 pan matrix: $($Recipe.pan)" }
    }
    return New-Track $source.Path $source.Channels ([int]$Recipe.startMs) ([double]$Recipe.gain) $matrix[0] $matrix[1] $matrix[2] $matrix[3]
}

function Get-WorkloadTracks([string] $WorkloadId,[int] $ExpectedCount) {
    $workload = @($contract.workloads | Where-Object id -eq $WorkloadId)
    if ($workload.Count -ne 1 -or $workload[0].audioTracks.Count -ne $ExpectedCount) { throw "Unexpected Stage 2 audio recipe: $WorkloadId" }
    return [ReelForge.Gate0.AudioTruthTrack[]]@($workload[0].audioTracks | ForEach-Object { New-TrackFromRecipe $_ })
}

$typical = Get-WorkloadTracks 'typical-2v4a' 4
$stress = Get-WorkloadTracks 'stress-4v8a' 8

$records = @()
foreach ($item in @(
    [pscustomobject]@{ Id = 'typical-2v4a-30s'; Tracks = $typical },
    [pscustomobject]@{ Id = 'stress-4v8a-30s'; Tracks = $stress }
)) {
    $path = Join-Path $output "$($item.Id).s16le"
    [ReelForge.Gate0.Stage2AudioTruth]::Render($path, 1440000, $item.Tracks)
    $file = Get-Item -LiteralPath $path
    $records += [ordered]@{
        id = $item.Id
        filename = $file.Name
        size = $file.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
        sampleRate = 48000
        channels = 2
        samplesPerChannel = 1440000
        rounding = 'IEEE-754 binary64 accumulation; Math.Round MidpointRounding.ToEven; clamp to signed 16-bit'
    }
}

$report = [ordered]@{
    schemaVersion = 1
    generatorId = 'Gate0.G05.Stage2AudioTruth.V1'
    contractStatus = 'proposed-owner-review-required'
    workloadContractSha256 = (Get-FileHash -LiteralPath $contractFullPath -Algorithm SHA256).Hash.ToUpperInvariant()
    outputs = $records
}
$json = $report | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $output 'g0.5-stage2-audio-truth-report.json'), $json, [Text.UTF8Encoding]::new($false))
$json
