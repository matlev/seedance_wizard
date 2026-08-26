[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ReferencePcmPath,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedReferenceSha256 = 'D6D0E575C834A0BFCF442713745EC409CC0774BF4D9356DA0B68A07D3F3F0E78'
if (-not [IO.Path]::IsPathRooted($ReferencePcmPath)) { throw 'ReferencePcmPath must be absolute.' }
if ((Get-FileHash -LiteralPath $ReferencePcmPath -Algorithm SHA256).Hash.ToUpperInvariant() -ne $expectedReferenceSha256) { throw 'Reference PCM hash mismatch.' }
if (-not [IO.Path]::IsPathRooted($OutputDirectory) -or (Test-Path -LiteralPath $OutputDirectory)) { throw 'OutputDirectory must be an absolute new path.' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

if (-not ('ReelForge.Gate0.LossyAudioControls' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;

namespace ReelForge.Gate0 {
  public sealed class ChannelMetrics {
    public int Channel { get; set; }
    public double SignedCorrelation { get; set; }
    public double NormalizedRmsError { get; set; }
    public double SnrDb { get; set; }
    public double RmsRatio { get; set; }
    public double DcOffsetFullScale { get; set; }
    public double ActiveRmsFullScale { get; set; }
    public double ExpectedTonePower { get; set; }
    public double ReferenceExpectedTonePower { get; set; }
    public double ExpectedToneOutputToReferenceAmplitudeRatio { get; set; }
    public double OtherTonePower { get; set; }
    public double ExpectedToOtherTonePowerRatio { get; set; }
    public double MinimumActiveWindowRmsFullScale { get; set; }
    public int NearClippedSampleCount { get; set; }
  }

  public sealed class OracleMetrics {
    public bool Passed { get; set; }
    public ChannelMetrics[] Channels { get; set; } = Array.Empty<ChannelMetrics>();
    public string[] Failures { get; set; } = Array.Empty<string>();
  }

  public static class LossyAudioControls {
    const int SampleRate = 48000;
    const int Channels = 2;
    const int SamplesPerChannel = 384000;
    const int Guard = 2048;

    public static short[] MakeReference(byte[] loopBytes) {
      if (loopBytes.Length == 0 || loopBytes.Length % 4 != 0) throw new InvalidOperationException("Invalid stereo reference loop.");
      var loop = new short[loopBytes.Length / 2];
      Buffer.BlockCopy(loopBytes, 0, loop, 0, loopBytes.Length);
      var output = new short[SamplesPerChannel * Channels];
      for (var n = 0; n < SamplesPerChannel; n++) {
        var source = (n % (loop.Length / Channels)) * Channels;
        output[n * Channels] = loop[source];
        output[n * Channels + 1] = loop[source + 1];
      }
      return output;
    }

    public static Dictionary<string, short[]> MakeControls(short[] reference) {
      var controls = new Dictionary<string, short[]>(StringComparer.Ordinal) {
        ["identity"] = (short[])reference.Clone(),
        ["gain-95-percent"] = Gain(reference, 0.95),
        ["noise-24db-snr"] = AddNoise(reference, 24, 0x5EED1234u),
        ["one-percent-crosstalk"] = Crosstalk(reference, 0.01),
        ["midstream-960-sample-dropout"] = Dropout(reference),
        ["gain-75-percent"] = Gain(reference, 0.75),
        ["noise-15db-snr"] = AddNoise(reference, 15, 0x15BAD5EDu),
        ["polarity-inversion"] = Gain(reference, -1.0),
        ["channel-swap"] = Swap(reference),
        ["clipping"] = Gain(reference, 3.0),
        ["silence"] = new short[reference.Length],
        ["frequency-offset"] = OffsetTones()
      };
      return controls;
    }

    public static OracleMetrics Analyze(short[] reference, short[] actual) {
      if (reference.Length != SamplesPerChannel * Channels || actual.Length != reference.Length) throw new InvalidOperationException("Structural sample-count gate failed.");
      var metrics = new ChannelMetrics[Channels];
      var failures = new List<string>();
      for (var channel = 0; channel < Channels; channel++) {
        double sumX=0,sumY=0;
        var count = SamplesPerChannel - (2 * Guard);
        for (var n=Guard;n<SamplesPerChannel-Guard;n++) { sumX += reference[n*2+channel]; sumY += actual[n*2+channel]; }
        var meanX=sumX/count; var meanY=sumY/count;
        double covariance=0,energyX=0,energyY=0,error=0;
        var clipped=0;
        for (var n=0;n<SamplesPerChannel;n++) if (Math.Abs((int)actual[n*2+channel]) >= 32760) clipped++;
        for (var n=Guard;n<SamplesPerChannel-Guard;n++) {
          var x=reference[n*2+channel]; var y=actual[n*2+channel];
          var xc=x-meanX; var yc=y-meanY; var e=y-x;
          covariance+=xc*yc; energyX+=xc*xc; energyY+=yc*yc; error+=e*e;
        }
        var rmsX=Math.Sqrt(energyX/count); var rmsY=Math.Sqrt(energyY/count); var rmsError=Math.Sqrt(error/count);
        var expectedHz=channel==0?440:880; var otherHz=channel==0?880:440;
        var expectedPower=TonePower(actual,channel,expectedHz,Guard,SamplesPerChannel-Guard);
        var referenceExpectedPower=TonePower(reference,channel,expectedHz,Guard,SamplesPerChannel-Guard);
        var otherPower=TonePower(actual,channel,otherHz,Guard,SamplesPerChannel-Guard);
        var minimumActiveWindowRms=MinimumWindowRms(actual,channel,Guard,SamplesPerChannel-Guard,960);
        var cm=new ChannelMetrics {
          Channel=channel,
          SignedCorrelation=covariance/Math.Sqrt(energyX*energyY),
          NormalizedRmsError=rmsError/rmsX,
          SnrDb=error==0?double.PositiveInfinity:10*Math.Log10(energyX/error),
          RmsRatio=rmsY/rmsX,
          DcOffsetFullScale=Math.Abs(meanY)/32768.0,
          ActiveRmsFullScale=rmsY/32768.0,
          ExpectedTonePower=expectedPower,
          ReferenceExpectedTonePower=referenceExpectedPower,
          ExpectedToneOutputToReferenceAmplitudeRatio=Math.Sqrt(expectedPower/referenceExpectedPower),
          OtherTonePower=otherPower,
          ExpectedToOtherTonePowerRatio=otherPower==0?double.PositiveInfinity:expectedPower/otherPower,
          MinimumActiveWindowRmsFullScale=minimumActiveWindowRms/32768.0,
          NearClippedSampleCount=clipped
        };
        metrics[channel]=cm;
        if (!(cm.SignedCorrelation>=0.995)) failures.Add($"channel-{channel}:correlation");
        if (!(cm.NormalizedRmsError<=0.10)) failures.Add($"channel-{channel}:nrmse");
        if (!(cm.SnrDb>=20.0)) failures.Add($"channel-{channel}:snr");
        if (!(cm.RmsRatio>=0.90&&cm.RmsRatio<=1.10)) failures.Add($"channel-{channel}:rms-ratio");
        if (!(cm.DcOffsetFullScale<=0.005)) failures.Add($"channel-{channel}:dc-offset");
        if (!(cm.ActiveRmsFullScale>=0.05)) failures.Add($"channel-{channel}:silence");
        if (!(cm.MinimumActiveWindowRmsFullScale>=0.05)) failures.Add($"channel-{channel}:active-window-silence");
        if (!(cm.ExpectedToneOutputToReferenceAmplitudeRatio>=0.90&&cm.ExpectedToneOutputToReferenceAmplitudeRatio<=1.10)) failures.Add($"channel-{channel}:tone-amplitude-ratio");
        if (!(cm.ExpectedToOtherTonePowerRatio>=100.0)) failures.Add($"channel-{channel}:tone-dominance");
        if (cm.NearClippedSampleCount!=0) failures.Add($"channel-{channel}:near-clipping");
      }
      return new OracleMetrics { Passed=failures.Count==0, Channels=metrics, Failures=failures.ToArray() };
    }

    public static void Write(string path, short[] samples) {
      var bytes=new byte[samples.Length*2]; Buffer.BlockCopy(samples,0,bytes,0,bytes.Length); File.WriteAllBytes(path,bytes);
    }

    static short[] Gain(short[] source,double gain) { var r=new short[source.Length]; for(var i=0;i<r.Length;i++) r[i]=ToInt16(source[i]*gain); return r; }
    static short[] Crosstalk(short[] source,double amount) { var r=new short[source.Length]; for(var n=0;n<SamplesPerChannel;n++){var l=source[n*2];var rr=source[n*2+1];r[n*2]=ToInt16(l+amount*rr);r[n*2+1]=ToInt16(rr+amount*l);}return r; }
    static short[] Swap(short[] source) { var r=new short[source.Length]; for(var n=0;n<SamplesPerChannel;n++){r[n*2]=source[n*2+1];r[n*2+1]=source[n*2];}return r; }
    static short[] Dropout(short[] source) { var r=(short[])source.Clone();var start=(SamplesPerChannel-960)/2;for(var n=start;n<start+960;n++){r[n*2]=0;r[n*2+1]=0;}return r; }
    static short[] OffsetTones() { var r=new short[SamplesPerChannel*2];for(var n=0;n<SamplesPerChannel;n++){r[n*2]=ToInt16(12000*Math.Sin(2*Math.PI*450*n/SampleRate));r[n*2+1]=ToInt16(12000*Math.Sin(2*Math.PI*890*n/SampleRate));}return r; }

    static short[] AddNoise(short[] source,double snrDb,uint seed) {
      var noise=new double[source.Length];var state=seed;
      for(var i=0;i<noise.Length;i++){state^=state<<13;state^=state>>17;state^=state<<5;noise[i]=((state/(double)uint.MaxValue)*2)-1;}
      var scales=new double[2];
      for(var c=0;c<2;c++){double signal=0,raw=0;var count=SamplesPerChannel-2*Guard;for(var n=Guard;n<SamplesPerChannel-Guard;n++){var x=source[n*2+c];signal+=x*x;var z=noise[n*2+c];raw+=z*z;}scales[c]=Math.Sqrt((signal/count)/Math.Pow(10,snrDb/10)/(raw/count));}
      var r=new short[source.Length];for(var n=0;n<SamplesPerChannel;n++)for(var c=0;c<2;c++)r[n*2+c]=ToInt16(source[n*2+c]+noise[n*2+c]*scales[c]);return r;
    }

    static double TonePower(short[] samples,int channel,int hz,int start,int end) {
      double mean=0;var count=end-start;for(var n=start;n<end;n++)mean+=samples[n*2+channel];mean/=count;
      double re=0,im=0;for(var n=start;n<end;n++){var value=samples[n*2+channel]-mean;var phase=2*Math.PI*hz*n/SampleRate;re+=value*Math.Cos(phase);im-=value*Math.Sin(phase);}return (re*re+im*im)/(count*(double)count);
    }
    static double MinimumWindowRms(short[] samples,int channel,int start,int end,int window) {
      if(end-start<window)throw new InvalidOperationException("Active region is shorter than the silence window.");
      double sum=0;for(var n=start;n<start+window;n++){var y=(double)samples[n*2+channel];sum+=y*y;}
      var minimum=sum;
      for(var n=start+window;n<end;n++){var added=(double)samples[n*2+channel];var removed=(double)samples[(n-window)*2+channel];sum+=(added*added)-(removed*removed);if(sum<minimum)minimum=sum;}
      return Math.Sqrt(minimum/window);
    }
    static short ToInt16(double value) { return (short)Math.Clamp(Math.Round(value,MidpointRounding.ToEven),short.MinValue,short.MaxValue); }
  }
}
'@
}

$loop = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($ReferencePcmPath))
$reference = [ReelForge.Gate0.LossyAudioControls]::MakeReference($loop)
$controls = [ReelForge.Gate0.LossyAudioControls]::MakeControls($reference)
$expected = @{
    'identity' = $true; 'gain-95-percent' = $true; 'noise-24db-snr' = $true; 'one-percent-crosstalk' = $true
    'midstream-960-sample-dropout' = $false
    'gain-75-percent' = $false; 'noise-15db-snr' = $false; 'polarity-inversion' = $false; 'channel-swap' = $false
    'clipping' = $false; 'silence' = $false; 'frequency-offset' = $false
}
$records = @()
foreach ($id in $controls.Keys | Sort-Object) {
    $path = Join-Path $OutputDirectory "$id.s16le"
    [ReelForge.Gate0.LossyAudioControls]::Write($path, $controls[$id])
    $metrics = [ReelForge.Gate0.LossyAudioControls]::Analyze($reference, $controls[$id])
    if ($metrics.Passed -ne $expected[$id]) { throw "Synthetic control verdict mismatch: $id" }
    $records += [ordered]@{
        id = $id
        expectedPass = $expected[$id]
        actualPass = $metrics.Passed
        filename = [IO.Path]::GetFileName($path)
        size = (Get-Item -LiteralPath $path).Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
        metrics = $metrics
    }
}
$report = [ordered]@{
    schemaVersion = 1
    controlSetId = 'Gate0.G05.LossyAudioOracle.Controls.V2.AmplitudeRatio'
    oracleContractId = 'Gate0.G05.LossyAudioOracle.V3.Frozen.20260826'
    referenceExpansion = 'Repeat the retained 5,760-sample-per-channel F1 loop to exactly 384,000 samples per channel.'
    sampleRate = 48000
    channels = 2
    samplesPerChannel = 384000
    qualityRegion = [ordered]@{ startSample = 2048; endSampleExclusive = 381952; window = 'rectangular'; dcRemoval = 'per-channel arithmetic mean over the quality region'; alignment = 'none' }
    prng = [ordered]@{ algorithm = 'xorshift32'; noise24SeedHex = '5EED1234'; noise15SeedHex = '15BAD5ED'; uniformMapping = '(state / UInt32.MaxValue) * 2 - 1'; perChannelRmsNormalization = $true }
    quantization = 'Math.Round MidpointRounding.ToEven then clamp to signed 16-bit'
    controls = $records
}
$json = $report | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'g0.5-lossy-audio-control-results.json'), $json, [Text.UTF8Encoding]::new($false))
$json
