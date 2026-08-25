[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InputJpeg,
    [Parameter(Mandatory)] [string] $OutputJpeg,
    [Parameter(Mandatory)] [string] $ExpectedInputSha256,
    [ValidateSet(1, 2, 3, 4, 5, 6, 7, 8)] [int] $Orientation = 6
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [IO.Path]::IsPathRooted($InputJpeg) -or -not [IO.Path]::IsPathRooted($OutputJpeg)) { throw 'InputJpeg and OutputJpeg must be explicit rooted paths.' }
$input = [IO.Path]::GetFullPath($InputJpeg)
$output = [IO.Path]::GetFullPath($OutputJpeg)
if (-not (Test-Path -LiteralPath $input -PathType Leaf)) { throw "Input JPEG does not exist: $input" }
if (Test-Path -LiteralPath $output) { throw "Output JPEG must not already exist: $output" }
$parent = [IO.Path]::GetDirectoryName($output)
if ([string]::IsNullOrWhiteSpace($parent) -or -not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Output JPEG parent must already exist.' }

$source = [IO.File]::ReadAllBytes($input)
if ($source.Length -lt 4 -or $source[0] -ne 0xFF -or $source[1] -ne 0xD8 -or $source[$source.Length - 2] -ne 0xFF -or $source[$source.Length - 1] -ne 0xD9) {
    throw 'Input is not a bounded complete JPEG byte stream with SOI and EOI markers.'
}
$inputSha256 = (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash.ToUpperInvariant()
if ($inputSha256 -ne $ExpectedInputSha256.ToUpperInvariant()) { throw 'Input JPEG hash does not match ExpectedInputSha256.' }

# Reject rather than merge existing Exif APP1 metadata. The bounded writer owns
# exactly one new APP1 segment and must not reinterpret or overwrite metadata.
$scan = 2
while ($scan + 3 -lt $source.Length -and $source[$scan] -eq 0xFF) {
    $marker = $source[$scan + 1]
    if ($marker -eq 0xDA -or $marker -eq 0xD9) { break }
    if ($marker -in 0xD8, 0x01 -or ($marker -ge 0xD0 -and $marker -le 0xD7)) { $scan += 2; continue }
    $length = ($source[$scan + 2] * 256) + $source[$scan + 3]
    if ($length -lt 2 -or $scan + 2 + $length -gt $source.Length) { throw 'Input JPEG contains an invalid marker segment.' }
    if ($marker -eq 0xE1) { throw 'Input JPEG already contains an APP1 segment; bounded writer refuses to merge, replace, or add a second APP1 segment.' }
    $scan += 2 + $length
}

# APP1 length is 34 bytes: 6-byte Exif identifier plus a 26-byte little-endian TIFF payload.
# TIFF: II, 42, IFD0 at offset 8, one SHORT Orientation (0x0112) entry, no next IFD.
[byte[]] $app1 = @(
    0xFF, 0xE1, 0x00, 0x22,
    0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
    0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
    0x01, 0x00,
    0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
    [byte]$Orientation, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00
)
$result = [byte[]]::new($source.Length + $app1.Length)
[Array]::Copy($source, 0, $result, 0, 2)
[Array]::Copy($app1, 0, $result, 2, $app1.Length)
[Array]::Copy($source, 2, $result, 2 + $app1.Length, $source.Length - 2)

$partial = "$output.partial"
try {
    [IO.File]::WriteAllBytes($partial, $result)
    Move-Item -LiteralPath $partial -Destination $output -ErrorAction Stop
}
finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force } }

[ordered]@{
    inputSha256 = $inputSha256
    outputSha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToUpperInvariant()
    app1Length = $app1.Length
    orientation = $Orientation
    byteOrder = 'II'
    tiffTag = '0x0112'
    tiffType = 'SHORT'
    tiffCount = 1
    tiffValue = $Orientation
    tiffSegmentLength = 34
    placement = 'immediately after SOI'
    preservation = 'all input bytes after SOI remain byte-identical'
} | ConvertTo-Json -Compress
