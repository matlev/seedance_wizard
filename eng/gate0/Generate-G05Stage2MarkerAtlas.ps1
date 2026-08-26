[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be absolute.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'OutputDirectory must be new.' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

if (-not ('ReelForge.Gate0.Stage2MarkerAtlas' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

namespace ReelForge.Gate0 {
  public static class Stage2MarkerAtlas {
    public const int MarkerCount = 90000;
    public const int MarkersPerRow = 300;
    public const int BitsPerMarker = 17;
    public const int Width = MarkersPerRow * BitsPerMarker;
    public const int Height = MarkerCount / MarkersPerRow;

    public static void Write(string path) {
      using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
      var header = Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n");
      stream.Write(header, 0, header.Length);
      for (var marker = 0; marker < MarkerCount; marker++) {
        for (var bit = BitsPerMarker - 1; bit >= 0; bit--) {
          var value = ((marker >> bit) & 1) == 0 ? (byte)0 : (byte)255;
          stream.WriteByte(value);
          stream.WriteByte(value);
          stream.WriteByte(value);
        }
      }
    }
  }
}
'@
}

$path = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'g0.5-frame-index-atlas-90000x17bit.ppm'
[ReelForge.Gate0.Stage2MarkerAtlas]::Write($path)
$file = Get-Item -LiteralPath $path
$report = [ordered]@{
    schemaVersion = 1
    generatorId = 'Gate0.G05.Stage2MarkerAtlas.V1'
    status = 'proposed-owner-review-required-not-retained'
    filename = $file.Name
    size = $file.Length
    sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    media = [ordered]@{ format = 'ppm-p6-rgb24'; width = 5100; height = 300 }
    semantics = [ordered]@{
        markerCount = 90000
        markersPerRow = 300
        bitsPerMarker = 17
        bitOrder = 'most-significant to least-significant'
        zero = 'rgb 0 0 0'
        one = 'rgb 255 255 255'
        markerForFrameIndex = 'crop 17x1 at x=(index mod 300)*17 and y=floor(index/300)'
    }
}
$json = $report | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'g0.5-stage2-marker-atlas-report.json'), $json, [Text.UTF8Encoding]::new($false))
$json
