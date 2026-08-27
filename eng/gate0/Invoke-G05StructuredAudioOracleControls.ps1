[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FixtureRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [string] $OracleContractPath = (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-contract.json'),
    [string] $AmendmentPath = (Join-Path $PSScriptRoot 'g0.5-lossy-audio-oracle-amendment-v4.json'),
    [string] $WorkloadContractPath = (Join-Path $PSScriptRoot 'g0.5-stage2-workload-contract.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }
function Require-AbsoluteExistingDirectory([string] $Path, [string] $Label) {
    if (-not [IO.Path]::IsPathRooted($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Label must be an existing absolute directory." }
    [IO.Path]::GetFullPath($Path)
}
function Require-AbsoluteNewDirectory([string] $Path, [string] $Label) {
    if (-not [IO.Path]::IsPathRooted($Path) -or (Test-Path -LiteralPath $Path)) { throw "$Label must be an absolute new directory." }
    [IO.Directory]::CreateDirectory($Path).FullName
}

$FixtureRoot = Require-AbsoluteExistingDirectory $FixtureRoot 'FixtureRoot'
$OutputDirectory = Require-AbsoluteNewDirectory $OutputDirectory 'OutputDirectory'
foreach ($path in @($OracleContractPath, $AmendmentPath, $WorkloadContractPath)) { if (-not [IO.Path]::IsPathRooted($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Every contract path must be an existing absolute file.' } }
$oracle = Get-Content -LiteralPath $OracleContractPath -Raw | ConvertFrom-Json -Depth 64
$amendment = Get-Content -LiteralPath $AmendmentPath -Raw | ConvertFrom-Json -Depth 64
$workloads = Get-Content -LiteralPath $WorkloadContractPath -Raw | ConvertFrom-Json -Depth 64
if ($amendment.extends.contractId -ne $oracle.contractId -or $amendment.extends.sha256 -ne (Get-Sha256 $OracleContractPath)) { throw 'V4 amendment does not bind the exact V3 contract.' }
$overlay = @($amendment.descriptorOverlays | Where-Object { $_.referenceDescriptorId -eq 'typical-2v4a-30s' })
if ($overlay.Count -ne 1 -or [int]$overlay[0].windowSamples -ne 960) { throw 'V4 amendment must contain exactly one 960-sample typical overlay.' }
$descriptor = @($oracle.referenceDescriptors | Where-Object { $_.id -eq 'typical-2v4a-30s' })
$workload = @($workloads.workloads | Where-Object { $_.id -eq 'typical-2v4a' })
if ($descriptor.Count -ne 1 -or $workload.Count -ne 1) { throw 'Frozen typical descriptor or workload is not unique.' }

Import-Module (Join-Path $PSScriptRoot 'G05Stage2SmokeHelpers.psm1') -Force
$truth = Join-Path $OutputDirectory 'typical-2v4a-30s.s16le'
New-G05TypicalAudioTruth $FixtureRoot $workload[0] $truth | Out-Null
if ((Get-Sha256 $truth) -ne [string]$descriptor[0].referencePcmSha256) { throw 'Regenerated typical truth does not match the exact V3 descriptor.' }

if (-not ('ReelForge.Gate0.StructuredAudioOracleControls' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
namespace ReelForge.Gate0 {
  public static class StructuredAudioOracleControls {
    public static short[] Scale(short[] source, double gain) { var result = new short[source.Length]; for (var i=0;i<source.Length;i++) result[i] = (short)Math.Clamp(Math.Round(source[i]*gain, MidpointRounding.ToEven), short.MinValue, short.MaxValue); return result; }
    public static short[] DropRight(short[] source, int start, int length) { var result=(short[])source.Clone(); for(var n=start;n<start+length;n++) result[n*2+1]=0; return result; }
    public static void Write(string path, short[] samples) { var bytes=new byte[samples.Length*2]; Buffer.BlockCopy(samples,0,bytes,0,bytes.Length); System.IO.File.WriteAllBytes(path,bytes); }
  }
}
'@
}

$truthBytes = [IO.File]::ReadAllBytes($truth); $truthSamples = [int16[]]::new($truthBytes.Length / 2); [Buffer]::BlockCopy($truthBytes, 0, $truthSamples, 0, $truthBytes.Length)
$controls = [ordered]@{
    'typical-identity' = $truthSamples
    'typical-panned-gain-95-percent' = [ReelForge.Gate0.StructuredAudioOracleControls]::Scale($truthSamples, 0.95)
    'typical-right-low-level-960-sample-dropout' = [ReelForge.Gate0.StructuredAudioOracleControls]::DropRight($truthSamples, 5000, 960)
    'typical-gain-75-percent' = [ReelForge.Gate0.StructuredAudioOracleControls]::Scale($truthSamples, 0.75)
    'typical-gain-125-percent' = [ReelForge.Gate0.StructuredAudioOracleControls]::Scale($truthSamples, 1.25)
}
$expected = @{ 'typical-identity'=$true; 'typical-panned-gain-95-percent'=$true; 'typical-right-low-level-960-sample-dropout'=$false; 'typical-gain-75-percent'=$false; 'typical-gain-125-percent'=$false }
$records = [Collections.Generic.List[object]]::new()
foreach ($entry in $controls.GetEnumerator()) {
    $path = Join-Path $OutputDirectory ($entry.Key + '.s16le')
    [ReelForge.Gate0.StructuredAudioOracleControls]::Write($path, $entry.Value)
    $result = Test-G05SmokeAudio $truth $path $oracle.qualityThresholds $descriptor[0] 0 $overlay[0]
    if ($result.passed -ne $expected[$entry.Key]) { throw "Structured synthetic control verdict mismatch: $($entry.Key)" }
    if ($entry.Key -eq 'typical-right-low-level-960-sample-dropout' -and -not (@($result.failures) -match 'reference-relative-window-rms-ratio')) { throw 'Low-level right-channel dropout did not fail through the required reference-relative window gate.' }
    $a0 = @($result.regions | Where-Object { $_.Id -eq 'a0-onset' })
    $a0Relative = @($result.referenceRelativeRegions | Where-Object { $_.Id -eq 'a0-onset' })
    if ($a0.Count -ne 1 -or $a0Relative.Count -ne 1 -or -not ([double]$a0[0].Channels[1].ActiveRmsFullScale -lt 0.05)) { throw 'The a0 panned-right channel was not demonstrated below the V3 absolute activity floor.' }
    if ($entry.Key -in @('typical-identity', 'typical-panned-gain-95-percent') -and -not $a0Relative[0].Passed) { throw 'A preserved low-level panned-right channel did not pass the reference-relative window gate.' }
    $records.Add([ordered]@{ id=$entry.Key; expectedPass=$expected[$entry.Key]; actualPass=$result.passed; filename=[IO.Path]::GetFileName($path); size=(Get-Item -LiteralPath $path).Length; sha256=(Get-Sha256 $path); result=$result })
}

$legacyOutput = Join-Path $OutputDirectory 'legacy-v3-controls'
$legacyScript = Join-Path $PSScriptRoot 'Invoke-G05LossyAudioOracleControls.ps1'
$legacyReference = Join-Path $FixtureRoot 'F1/f1-sync-440hz-880hz-48000-stereo.pcm'
$legacyJson = & pwsh -NoProfile -File $legacyScript -ReferencePcmPath $legacyReference -OutputDirectory $legacyOutput
if ($LASTEXITCODE -ne 0) { throw 'Exact V3 legacy controls failed to execute.' }
$legacy = $legacyJson | ConvertFrom-Json -Depth 64
$vectors = @{}; foreach ($vector in @($oracle.syntheticControlEvidence.vectors)) { $vectors[[string]$vector.id] = $vector }
if (@($legacy.controls).Count -ne 12 -or $vectors.Count -ne 12) { throw 'Exact V3 legacy control count changed.' }
foreach ($record in @($legacy.controls)) {
    $frozen = $vectors[[string]$record.id]
    if ($null -eq $frozen -or $record.actualPass -ne $frozen.expectedPass -or $record.sha256 -ne $frozen.sha256) { throw "Exact V3 legacy control changed: $($record.id)" }
}
$f1Descriptor = @($oracle.referenceDescriptors | Where-Object { $_.id -eq 'f1-loop-8s' })
if ($f1Descriptor.Count -ne 1) { throw 'Frozen F1 descriptor is not unique.' }
$legacyTruth = Join-Path $legacyOutput 'identity.s16le'
foreach ($record in @($legacy.controls)) {
    $effective = Test-G05SmokeAudio $legacyTruth (Join-Path $legacyOutput ([string]$record.filename)) $oracle.qualityThresholds $f1Descriptor[0] 0
    if ($effective.passed -ne $record.expectedPass) { throw "No-overlay V3 effective-oracle regression: $($record.id)" }
}

$report = [ordered]@{
    schemaVersion=1; controlSetId='Gate0.G05.LossyAudioOracle.Controls.V4.ReferenceRelativeTypical'; amendmentId=$amendment.amendmentId; v3Contract=[ordered]@{path='eng/gate0/g0.5-lossy-audio-oracle-contract.json';sha256=(Get-Sha256 $OracleContractPath);contractId=$oracle.contractId}; routeOutputsEvaluated=$false; routeReencodePerformed=$false; typicalTruth=[ordered]@{sha256=(Get-Sha256 $truth);size=(Get-Item -LiteralPath $truth).Length}; attenuatedPannedPreservationBasis='The 0.95 control attenuates the independently authored typical truth; its a0 source is already panned stereo|c0=c0|c1=0.25*c0, and the report retains the below-0.05 a0 right-channel V3 observation plus the reference-relative pass.'; structuredControls=@($records); legacyV3Controls=[ordered]@{count=@($legacy.controls).Count;allFrozenDispositionsAndHashesPreserved=$true;noOverlayEffectiveOracleDispositionsPreserved=$true};
}
$json = $report | ConvertTo-Json -Depth 64
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'g0.5-structured-audio-oracle-control-results.json'), $json, [Text.UTF8Encoding]::new($false))
$json
