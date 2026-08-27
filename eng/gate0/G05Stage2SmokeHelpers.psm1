Set-StrictMode -Version Latest

function Get-G05SmokeHash([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function New-G05SmokeSnapshotBinding([string] $SnapshotRoot, [hashtable] $Sources) {
    $bindings = [Collections.Generic.List[object]]::new()
    foreach ($entry in @($Sources.GetEnumerator() | Sort-Object Key)) {
        $source = [IO.Path]::GetFullPath([string]$entry.Value)
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Snapshot dependency is missing: $($entry.Key)." }
        $sourceHash = Get-G05SmokeHash $source
        $destination = Join-Path $SnapshotRoot ([string]$entry.Key)
        Copy-Item -LiteralPath $source -Destination $destination
        $copiedHash = Get-G05SmokeHash $destination
        if ($copiedHash -ne $sourceHash -or (Get-G05SmokeHash $source) -ne $sourceHash) { throw "Snapshot dependency changed while being bound: $($entry.Key)." }
        $bindings.Add([ordered]@{ name=[string]$entry.Key; size=(Get-Item -LiteralPath $destination).Length; sha256=$copiedHash })
    }
    @($bindings)
}

function Assert-G05SmokeSnapshotBinding([object[]] $Bindings, [hashtable] $Sources) {
    foreach ($binding in @($Bindings)) {
        if (-not $Sources.ContainsKey([string]$binding.name)) { throw "Snapshot binding source is unavailable: $($binding.name)." }
        if ((Get-G05SmokeHash ([string]$Sources[[string]$binding.name])) -ne [string]$binding.sha256) { throw "Snapshot source changed after binding: $($binding.name)." }
    }
}

function Get-G05SmokeSnapshotHash([object[]] $Bindings, [string] $Name) {
    $match = @($Bindings | Where-Object { $_.name -eq $Name })
    if ($match.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$match[0].sha256)) { throw "Snapshot binding is not unique and complete: $Name." }
    [string]$match[0].sha256
}

function Get-G05SmokeCombinedGraph([object] $Workload, [object] $Variant) {
    $substitutions = @{
        '{variant.width}' = [string] $Variant.width
        '{variant.height}' = [string] $Variant.height
        '{variant.pipWidth}' = [string] $Variant.pipWidth
        '{variant.pipHeight}' = [string] $Variant.pipHeight
        '{variant.pipX}' = [string] $Variant.pipX
        '{variant.pipY}' = [string] $Variant.pipY
    }
    $graph = ([string] $Workload.videoFilterGraph) + ';' + ([string] $Workload.audioFilterGraph)
    foreach ($key in $substitutions.Keys) { $graph = $graph.Replace($key, $substitutions[$key]) }
    $graph
}

function Assert-G05SmokeRoot([string] $Path, [string] $TrustedParent, [string] $Label) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw "$Label must be an absolute path." }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $parent = [IO.Path]::GetFullPath($TrustedParent).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $full.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "$Label escaped its trusted parent." }
    $cursor = Get-Item -LiteralPath $full -Force
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label traverses a reparse point." }
        if ($cursor.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($parent, [StringComparison]::OrdinalIgnoreCase)) { return $full }
        $cursor = $cursor.Parent
    }
    throw "$Label did not terminate at its trusted parent."
}

function Assert-G05SmokePath([string] $Root, [string] $Path, [string] $Label) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $pathFull = [IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "$Label escaped the scenario root." }
    $cursor = if (Test-Path -LiteralPath $pathFull) { Get-Item -LiteralPath $pathFull -Force } else { Get-Item -LiteralPath ([IO.Path]::GetDirectoryName($pathFull)) -Force }
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label traverses a reparse point." }
        if ($cursor.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($rootFull, [StringComparison]::OrdinalIgnoreCase)) { return $pathFull }
        $cursor = $cursor.Parent
    }
    throw "$Label did not terminate at its scenario root."
}

function ConvertTo-G05SmokePortableTokens([string[]] $Tokens, [hashtable] $Roots) {
    @($Tokens | ForEach-Object {
        $value = [string] $_
        foreach ($entry in $Roots.GetEnumerator()) {
            $root = [IO.Path]::GetFullPath([string] $entry.Value).TrimEnd([IO.Path]::DirectorySeparatorChar)
            if ($value.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { $value = "{$($entry.Key)}"; break }
            if ($value.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                $relative = [IO.Path]::GetRelativePath($root, $value).Replace('\', '/')
                $value = "{$($entry.Key)}/$relative"
                break
            }
        }
        $value
    })
}

function New-G05TypicalAudioTruth([string] $FixtureRoot, [object] $Workload, [string] $OutputPath) {
    Assert-G05SmokePath ([IO.Path]::GetDirectoryName($OutputPath)) $OutputPath 'Audio truth output' | Out-Null
    if (-not ('ReelForge.Gate0.SmokeTruth' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
namespace ReelForge.Gate0 {
  public static class SmokeTruth {
    public static void Render(string output, string[] paths, int[] channels, int[] starts, double[] gains, double[,] matrix) {
      short[][] sources = new short[paths.Length][];
      for (int i = 0; i < sources.Length; i++) {
        byte[] bytes = File.ReadAllBytes(paths[i]);
        if (bytes.Length == 0 || bytes.Length % (channels[i] * 2) != 0) throw new InvalidOperationException("Invalid smoke audio source closure.");
        sources[i] = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, sources[i], 0, bytes.Length);
      }
      using var writer = new BinaryWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None));
      for (int n = 0; n < 1440000; n++) {
        double left = 0, right = 0;
        for (int i = 0; i < sources.Length; i++) {
          if (n < starts[i]) continue;
          int sourceFrame = ((n - starts[i]) % (sources[i].Length / channels[i])) * channels[i];
          double sourceLeft = sources[i][sourceFrame];
          double sourceRight = channels[i] == 1 ? sourceLeft : sources[i][sourceFrame + 1];
          left += gains[i] * (matrix[i,0] * sourceLeft + matrix[i,1] * sourceRight);
          right += gains[i] * (matrix[i,2] * sourceLeft + matrix[i,3] * sourceRight);
        }
        writer.Write(ToInt16(left)); writer.Write(ToInt16(right));
      }
    }
    static short ToInt16(double value) => (short)Math.Clamp(Math.Round(value, MidpointRounding.ToEven), short.MinValue, short.MaxValue);
  }
}
'@
    }
    $sources = @{
        'f8-audio-440' = @('F8/f8-audio-zero-440hz.pcm', 1)
        'f2-audio-660' = @('F2/f2-48000-stereo-660hz.pcm', 2)
        'f8-audio-880' = @('F8/f8-audio-one-880hz.pcm', 1)
        'f4-audio-1000' = @('F4/f4-stereo-48000-1000hz-opposed.pcm', 2)
    }
    $paths = [Collections.Generic.List[string]]::new(); $channels = [Collections.Generic.List[int]]::new(); $starts = [Collections.Generic.List[int]]::new(); $gains = [Collections.Generic.List[double]]::new(); $matrix = New-Object 'double[,]' 4,4
    $index = 0
    foreach ($track in @($Workload.audioTracks)) {
        if (-not $sources.ContainsKey([string] $track.source)) { throw "Unknown frozen audio source: $($track.source)" }
        $source = $sources[[string] $track.source]
        $paths.Add((Join-Path $FixtureRoot $source[0])); $channels.Add([int] $source[1]); $starts.Add(48 * [int] $track.startMs); $gains.Add([double] $track.gain)
        $values = switch ([string] $track.pan) {
            'identity-stereo' { @(1.0, 0.0, 0.0, 1.0); break }
            'stereo|c0=c0|c1=0.25*c0' { @(1.0, 0.0, 0.25, 0.0); break }
            'stereo|c0=0.25*c0|c1=c0' { @(0.25, 0.0, 1.0, 0.0); break }
            default { throw "Unknown frozen pan recipe: $($track.pan)" }
        }
        for ($column = 0; $column -lt 4; $column++) { $matrix[$index,$column] = [double] $values[$column] }
        $index++
    }
    if ($index -ne 4) { throw 'Typical audio truth requires exactly four structured tracks.' }
    [ReelForge.Gate0.SmokeTruth]::Render($OutputPath, $paths.ToArray(), $channels.ToArray(), $starts.ToArray(), $gains.ToArray(), $matrix)
    if ((Get-Item -LiteralPath $OutputPath).Length -ne 5760000 -or (Get-G05SmokeHash $OutputPath) -ne '81B41CD4DB85568930C15282A7268E2CED2610D27D48C6CB258E1D1C5C1B8C5A') { throw 'Typical audio truth bytes do not match the frozen descriptor.' }
    $OutputPath
}

function Initialize-G05SmokeAudioOracle {
    if ('ReelForge.Gate0.SmokeAudioOracle' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
namespace ReelForge.Gate0 {
  public sealed class SmokeToneMetric { public int FrequencyHz { get; set; } public double ReferencePower { get; set; } public double OutputPower { get; set; } public double OutputToReferenceAmplitudeRatio { get; set; } }
  public sealed class SmokeAudioChannelMetric {
    public int Channel { get; set; } public double SignedCorrelation { get; set; } public double NormalizedRmsError { get; set; } public double SnrDb { get; set; }
    public double RmsRatio { get; set; } public double DcOffsetFullScale { get; set; } public double ActiveRmsFullScale { get; set; }
    public double MinimumWindowRmsFullScale { get; set; } public int NearClippedSampleCount { get; set; }
    public SmokeToneMetric[] ExpectedTones { get; set; } = Array.Empty<SmokeToneMetric>(); public double? MinimumExpectedToForbiddenTonePowerRatio { get; set; }
  }
  public sealed class SmokeAudioRegionMetric { public string Id { get; set; } = ""; public int StartSample { get; set; } public int EndSampleExclusive { get; set; } public SmokeAudioChannelMetric[] Channels { get; set; } = Array.Empty<SmokeAudioChannelMetric>(); public string[] Failures { get; set; } = Array.Empty<string>(); public bool Passed { get; set; } }
  public sealed class SmokeOnsetTimingMetric { public string Id { get; set; } = ""; public int ExpectedStartSample { get; set; } public int ObservedOffsetSamples { get; set; } public int ObservedStartSample { get; set; } public double BestMeanSquaredError { get; set; } public bool Passed { get; set; } public string Method { get; set; } = ""; }
  public static class SmokeAudioOracle {
    public static SmokeAudioRegionMetric Analyze(string id, short[] reference, short[] actual, int start, int end, int[] expectedLeft, int[] expectedRight, int[] forbiddenLeft, int[] forbiddenRight,
      double minCorrelation, double maxNrmse, double minSnr, double minRmsRatio, double maxRmsRatio, double maxDc, double minToneRatio, double maxToneRatio,
      double minToneDominance, double minActiveRms, int silenceWindow, double minWindowRms, int clipThreshold, int maxClipped) {
      if (reference.Length != actual.Length || reference.Length % 2 != 0) throw new InvalidOperationException("Audio oracle arrays must be equal stereo interleaved samples.");
      int frames = reference.Length / 2; if (start < 0 || end > frames || end <= start || end - start < silenceWindow) throw new InvalidOperationException("Audio oracle region is invalid.");
      var failures = new List<string>(); var channels = new SmokeAudioChannelMetric[2];
      for (int channel = 0; channel < 2; channel++) {
        int count = end - start; double sumX = 0, sumY = 0;
        for (int n = start; n < end; n++) { sumX += reference[n*2+channel]; sumY += actual[n*2+channel]; }
        double meanX = sumX/count, meanY = sumY/count, covariance = 0, energyX = 0, energyY = 0, error = 0; int clipped = 0;
        for (int n = start; n < end; n++) { double x=reference[n*2+channel], y=actual[n*2+channel], xc=x-meanX, yc=y-meanY, e=y-x; covariance+=xc*yc; energyX+=xc*xc; energyY+=yc*yc; error+=e*e; if (Math.Abs((int)actual[n*2+channel]) >= clipThreshold) clipped++; }
        double correlation = energyX == 0 || energyY == 0 ? double.NaN : covariance/Math.Sqrt(energyX*energyY), nrmse = energyX == 0 ? double.PositiveInfinity : Math.Sqrt(error/count)/Math.Sqrt(energyX/count), snr = error == 0 ? double.PositiveInfinity : 10*Math.Log10(energyX/error), rmsRatio = energyX == 0 ? double.PositiveInfinity : Math.Sqrt(energyY/energyX), activeRms = Math.Sqrt(energyY/count)/32768.0, minimumWindow = MinimumWindowRms(actual, channel, start, end, silenceWindow)/32768.0;
        int[] expected = channel == 0 ? expectedLeft : expectedRight, forbidden = channel == 0 ? forbiddenLeft : forbiddenRight; var tones = new List<SmokeToneMetric>(); double minimumDominance = double.PositiveInfinity;
        foreach (int hz in expected) { double referencePower=TonePower(reference,channel,hz,start,end,meanX),outputPower=TonePower(actual,channel,hz,start,end,meanY),amplitudeRatio=referencePower==0?double.PositiveInfinity:Math.Sqrt(outputPower/referencePower);tones.Add(new SmokeToneMetric{FrequencyHz=hz,ReferencePower=referencePower,OutputPower=outputPower,OutputToReferenceAmplitudeRatio=amplitudeRatio});foreach(int forbiddenHz in forbidden){double forbiddenPower=TonePower(actual,channel,forbiddenHz,start,end,meanY);minimumDominance=Math.Min(minimumDominance,forbiddenPower==0?double.PositiveInfinity:outputPower/forbiddenPower);}if(!(amplitudeRatio>=minToneRatio&&amplitudeRatio<=maxToneRatio))failures.Add($"{id}:channel-{channel}:tone-{hz}-amplitude-ratio"); }
        if(forbidden.Length>0&&!(minimumDominance>=minToneDominance))failures.Add($"{id}:channel-{channel}:tone-dominance");double dc=Math.Abs(meanY)/32768.0;
        if(!(correlation>=minCorrelation))failures.Add($"{id}:channel-{channel}:correlation");if(!(nrmse<=maxNrmse))failures.Add($"{id}:channel-{channel}:nrmse");if(!(snr>=minSnr))failures.Add($"{id}:channel-{channel}:snr");if(!(rmsRatio>=minRmsRatio&&rmsRatio<=maxRmsRatio))failures.Add($"{id}:channel-{channel}:rms-ratio");if(!(dc<=maxDc))failures.Add($"{id}:channel-{channel}:dc-offset");if(!(activeRms>=minActiveRms))failures.Add($"{id}:channel-{channel}:active-rms");if(!(minimumWindow>=minWindowRms))failures.Add($"{id}:channel-{channel}:active-window-silence");if(clipped>maxClipped)failures.Add($"{id}:channel-{channel}:near-clipping");
        channels[channel]=new SmokeAudioChannelMetric{Channel=channel,SignedCorrelation=correlation,NormalizedRmsError=nrmse,SnrDb=snr,RmsRatio=rmsRatio,DcOffsetFullScale=dc,ActiveRmsFullScale=activeRms,MinimumWindowRmsFullScale=minimumWindow,NearClippedSampleCount=clipped,ExpectedTones=tones.ToArray(),MinimumExpectedToForbiddenTonePowerRatio=forbidden.Length==0?null:minimumDominance};
      }
      return new SmokeAudioRegionMetric{Id=id,StartSample=start,EndSampleExclusive=end,Channels=channels,Failures=failures.ToArray(),Passed=failures.Count==0};
    }
    public static SmokeOnsetTimingMetric LocateTransition(string id, short[] reference, short[] actual, int start, int searchRadius, int windowRadius) {
      if(reference.Length!=actual.Length||reference.Length%2!=0)throw new InvalidOperationException("Onset arrays must be equal stereo interleaved samples.");
      if(start==0)return new SmokeOnsetTimingMetric{Id=id,ExpectedStartSample=0,ObservedOffsetSamples=0,ObservedStartSample=0,BestMeanSquaredError=0,Passed=true,Method="content-normalized-stream-start"};
      int frames=reference.Length/2;if(start-windowRadius<0||start+windowRadius+searchRadius>frames)throw new InvalidOperationException("Onset search window is out of range.");
      int bestOffset=int.MinValue;double best=double.PositiveInfinity;
      for(int offset=-searchRadius;offset<=searchRadius;offset++){double error=0;long count=0;for(int n=start-windowRadius;n<start+windowRadius;n++){for(int channel=0;channel<2;channel++){double difference=actual[(n+offset)*2+channel]-reference[n*2+channel];error+=difference*difference;count++;}}double mse=error/count;if(mse<best){best=mse;bestOffset=offset;}}
      return new SmokeOnsetTimingMetric{Id=id,ExpectedStartSample=start,ObservedOffsetSamples=bestOffset,ObservedStartSample=start+bestOffset,BestMeanSquaredError=best,Passed=bestOffset==0,Method="minimum-stereo-waveform-error-over-independent-reference-plus-or-minus-512-samples"};
    }
    static double TonePower(short[] samples,int channel,int hz,int start,int end,double mean){double re=0,im=0;int count=end-start;for(int n=start;n<end;n++){double value=samples[n*2+channel]-mean,phase=2*Math.PI*hz*n/48000;re+=value*Math.Cos(phase);im-=value*Math.Sin(phase);}return(re*re+im*im)/(count*(double)count);}
    static double MinimumWindowRms(short[] samples,int channel,int start,int end,int window){double sum=0;for(int n=start;n<start+window;n++){double y=samples[n*2+channel];sum+=y*y;}double minimum=sum;for(int n=start+window;n<end;n++){double added=samples[n*2+channel],removed=samples[(n-window)*2+channel];sum+=added*added-removed*removed;if(sum<minimum)minimum=sum;}return Math.Sqrt(Math.Max(0,minimum)/window);}
  }
}
'@
}

function Invoke-G05SmokeAudioRegion([string] $Id, [int16[]] $Reference, [int16[]] $Actual, [int] $Start, [int] $End, [object] $ExpectedFrequencies, [object] $ForbiddenFrequencies, [object] $Thresholds) {
    $leftExpected=[int[]]@($ExpectedFrequencies[0]|ForEach-Object{[int]$_});$rightExpected=[int[]]@($ExpectedFrequencies[1]|ForEach-Object{[int]$_});$leftForbidden=[int[]]::new(0);$rightForbidden=[int[]]::new(0);if($null-ne$ForbiddenFrequencies){$leftForbidden=[int[]]@($ForbiddenFrequencies[0]|ForEach-Object{[int]$_});$rightForbidden=[int[]]@($ForbiddenFrequencies[1]|ForEach-Object{[int]$_})}
    [ReelForge.Gate0.SmokeAudioOracle]::Analyze($Id,$Reference,$Actual,$Start,$End,$leftExpected,$rightExpected,$leftForbidden,$rightForbidden,[double]$Thresholds.minimumSignedNormalizedCrossCorrelationPerChannel,[double]$Thresholds.maximumNormalizedRmsErrorPerChannel,[double]$Thresholds.minimumSnrDbPerChannel,[double]$Thresholds.minimumOutputToReferenceRmsRatioPerChannel,[double]$Thresholds.maximumOutputToReferenceRmsRatioPerChannel,[double]$Thresholds.maximumAbsoluteDcOffsetFullScalePerChannel,[double]$Thresholds.minimumExpectedToneOutputToReferenceAmplitudeRatio,[double]$Thresholds.maximumExpectedToneOutputToReferenceAmplitudeRatio,[double]$Thresholds.minimumExpectedToForbiddenTonePowerRatioWhenDescriptorProvidesForbiddenTones,[double]$Thresholds.minimumActiveChannelRmsFullScale,[int]$Thresholds.silenceWindowSamples,[double]$Thresholds.minimumActiveReferenceWindowOutputRmsFullScale,[int]$Thresholds.nearClippingSampleAbsoluteThreshold,[int]$Thresholds.maximumUnexpectedNearClippedSamplesPerChannel)
}

function Test-G05SmokeAudio([string] $Reference, [string] $Actual, [object] $Thresholds, [object] $Descriptor, [int] $MaximumRawTailSamples = 1024) {
    Initialize-G05SmokeAudioOracle
    $referenceBytes=[IO.File]::ReadAllBytes($Reference);$actualBytes=[IO.File]::ReadAllBytes($Actual);$expectedBytes=[int64]$Descriptor.samplesPerChannel*[int]$Descriptor.channels*2
    if($referenceBytes.Length-ne$expectedBytes-or$actualBytes.Length-lt$expectedBytes-or$actualBytes.Length-gt$expectedBytes+($MaximumRawTailSamples*4)-or$actualBytes.Length%4-ne0){return[ordered]@{passed=$false;failures=@('content-normalized-sample-count-or-unapproved-tail');rawBytes=$actualBytes.Length;expectedContentBytes=$expectedBytes}}
    $referenceSamples=[int16[]]::new($referenceBytes.Length/2);[Buffer]::BlockCopy($referenceBytes,0,$referenceSamples,0,$referenceBytes.Length);$actualContentBytes=[byte[]]::new($expectedBytes);[Array]::Copy($actualBytes,$actualContentBytes,$expectedBytes);$actualSamples=[int16[]]::new($actualContentBytes.Length/2);[Buffer]::BlockCopy($actualContentBytes,0,$actualSamples,0,$actualContentBytes.Length)
    $forbiddenProperty=$Descriptor.PSObject.Properties['forbiddenFrequenciesHzByChannel'];$forbidden=if($null-eq$forbiddenProperty){$null}else{$forbiddenProperty.Value};$regions=[Collections.Generic.List[object]]::new();$regions.Add((Invoke-G05SmokeAudioRegion 'full-quality-region' $referenceSamples $actualSamples 2048 ([int]$Descriptor.samplesPerChannel-2048) $Descriptor.activeExpectedFrequenciesHzByChannel $forbidden $Thresholds))
    $onsetTiming=[Collections.Generic.List[object]]::new();foreach($window in @($Descriptor.trackOnsetWindows)){$regions.Add((Invoke-G05SmokeAudioRegion ([string]$window.id) $referenceSamples $actualSamples ([int]$window.startSample+512) ([int]$window.endSampleExclusive-512) $window.expectedFrequenciesHzByChannel $null $Thresholds));$onsetTiming.Add([ReelForge.Gate0.SmokeAudioOracle]::LocateTransition([string]$window.id,$referenceSamples,$actualSamples,[int]$window.startSample,512,2048))}
    $qualityFailures=@($regions|ForEach-Object{@($_.Failures)});$timingFailures=@($onsetTiming|Where-Object{-not$_.Passed}|ForEach-Object{"$($_.Id):onset-offset-$($_.ObservedOffsetSamples)"});$failures=@($qualityFailures)+@($timingFailures);[ordered]@{passed=$failures.Count-eq0;qualityPassed=$qualityFailures.Count-eq0;onsetTimingPassed=$timingFailures.Count-eq0;failures=$failures;regions=@($regions);onsetTiming=@($onsetTiming);rawPcm=[ordered]@{bytes=$actualBytes.Length;sha256=(Get-G05SmokeHash $Actual);samplesPerChannel=$actualBytes.Length/4;tailSamples=($actualBytes.Length-$expectedBytes)/4};contentNormalized=[ordered]@{selection='first descriptor.samplesPerChannel samples after decoder-applied skip/discard';bytes=$expectedBytes;sha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($actualContentBytes)));proofSideTrimmingPerformed=$false}}
}

function Convert-G05SmokeTicks([int64] $Ticks, [string] $TimeBase) {
    if($TimeBase-notmatch'^(-?\d+)/(\d+)$'-or[int64]$Matches[2]-eq0){throw'Invalid rational time base.'};[decimal]$numerator=[decimal]$Ticks*[int64]$Matches[1]*1000;[decimal]$denominator=[int64]$Matches[2];if(($numerator%$denominator)-ne0){throw'Timestamp cannot be represented exactly in the 1/1000 comparison time base.'};[int64]($numerator/$denominator)
}

function Get-G05SmokeDemuxer([string] $Muxer) { switch($Muxer){'mp4'{'mov,mp4,m4a,3gp,3g2,mj2'}'webm'{'matroska,webm'}default{throw'Unsupported frozen output demuxer.'}} }

function Initialize-G05SmokeVisualOracle {
    if('ReelForge.Gate0.SmokeVisualOracle'-as[type]){return};Add-Type -TypeDefinition @'
using System;using System.IO;
namespace ReelForge.Gate0 { public sealed class SmokeVisualResult { public double[] FrameMeanAbsoluteErrors {get;set;}=Array.Empty<double>();public double MaximumFrameMeanAbsoluteError{get;set;}public int Frames{get;set;}public bool ImmediateEof{get;set;} }
public static class SmokeVisualOracle { public static SmokeVisualResult Compare(Stream stream,string f0,string f1,string f2,string alpha){byte[][] sources={ReadPpm(f0),ReadPpm(f1),ReadPpm(f2)};byte[] overlay=File.ReadAllBytes(alpha);if(overlay.Length!=320*180*4)throw new InvalidOperationException("F3 RGBA geometry mismatch.");byte[][] expected={Compose(sources[0],overlay),Compose(sources[1],overlay),Compose(sources[2],overlay)};byte[] actual=new byte[1920*1080*3];double[] maes=new double[750];double maximum=0;for(int frame=0;frame<750;frame++){ReadExactly(stream,actual);byte[] truth=expected[frame%3];long sum=0;for(int i=0;i<actual.Length;i++)sum+=Math.Abs(actual[i]-truth[i]);double mae=sum/(double)actual.Length;maes[frame]=mae;if(mae>maximum)maximum=mae;}bool eof=stream.ReadByte()==-1;if(!eof)throw new InvalidOperationException("RGB stream contained bytes after frame 749.");return new SmokeVisualResult{FrameMeanAbsoluteErrors=maes,MaximumFrameMeanAbsoluteError=maximum,Frames=750,ImmediateEof=eof};}
static void ReadExactly(Stream stream,byte[] buffer){int offset=0;while(offset<buffer.Length){int read=stream.Read(buffer,offset,buffer.Length-offset);if(read==0)throw new InvalidOperationException("RGB stream ended before frame 749.");offset+=read;}}
static byte[] Compose(byte[] source,byte[] overlay){byte[] output=new byte[1920*1080*3];for(int y=0;y<1080;y++)for(int x=0;x<1920;x++)for(int c=0;c<3;c++)output[(y*1920+x)*3+c]=Scale(source,x,y,c);for(int y=660;y<1020;y++)for(int x=1320;x<1800;x++){int sx=(x-1320)*80/480,sy=(y-660)*60/360,overlayOffset=(sy*320+sx)*4,outputOffset=(y*1920+x)*3,alphaValue=overlay[overlayOffset+3];for(int c=0;c<3;c++)output[outputOffset+c]=(byte)((overlay[overlayOffset+c]*alphaValue+output[outputOffset+c]*(255-alphaValue)+127)/255);}return output;}
static byte Scale(byte[] source,int x,int y,int channel){double sx=(x+.5)*320.0/1920-.5,sy=(y+.5)*180.0/1080-.5;int floorX=(int)Math.Floor(sx),floorY=(int)Math.Floor(sy),x0=Math.Clamp(floorX,0,319),y0=Math.Clamp(floorY,0,179),x1=Math.Clamp(floorX+1,0,319),y1=Math.Clamp(floorY+1,0,179);double fx=sx-floorX,fy=sy-floorY,top=(1-fx)*source[(y0*320+x0)*3+channel]+fx*source[(y0*320+x1)*3+channel],bottom=(1-fx)*source[(y1*320+x0)*3+channel]+fx*source[(y1*320+x1)*3+channel];return(byte)Math.Clamp(Math.Round((1-fy)*top+fy*bottom,MidpointRounding.ToEven),0,255);}
static byte[] ReadPpm(string path){byte[] bytes=File.ReadAllBytes(path);int offset=0;string magic=Token(bytes,ref offset),width=Token(bytes,ref offset),height=Token(bytes,ref offset),max=Token(bytes,ref offset);if(magic!="P6"||width!="320"||height!="180"||max!="255")throw new InvalidOperationException("F1 PPM header mismatch.");while(offset<bytes.Length&&char.IsWhiteSpace((char)bytes[offset]))offset++;byte[] pixels=new byte[320*180*3];if(bytes.Length-offset!=pixels.Length)throw new InvalidOperationException("F1 PPM payload mismatch.");Buffer.BlockCopy(bytes,offset,pixels,0,pixels.Length);return pixels;}
static string Token(byte[] bytes,ref int offset){while(true){while(offset<bytes.Length&&char.IsWhiteSpace((char)bytes[offset]))offset++;if(offset<bytes.Length&&bytes[offset]=='#'){while(offset<bytes.Length&&bytes[offset]!='\n')offset++;continue;}break;}int start=offset;while(offset<bytes.Length&&!char.IsWhiteSpace((char)bytes[offset]))offset++;return System.Text.Encoding.ASCII.GetString(bytes,start,offset-start);} } }
'@
}

function Test-G05SmokeVisual([string]$Ffmpeg,[string]$Demuxer,[string]$VideoDecoder,[string]$Output,[string]$FixtureRoot,[string]$LogPath,[string]$MetricsPath,[string[]]$ProcessArguments=$null,[string]$ProcessReadyPath=$null){Initialize-G05SmokeVisualOracle;$startInfo=[Diagnostics.ProcessStartInfo]::new($Ffmpeg);$startInfo.UseShellExecute=$false;$startInfo.RedirectStandardOutput=$true;$startInfo.RedirectStandardError=$true;$startInfo.CreateNoWindow=$true;$arguments=if($null-eq$ProcessArguments){@('-v','error','-xerror','-err_detect','explode','-f',$Demuxer,'-c:v',$VideoDecoder,'-i',$Output,'-map','0:v:0','-an','-fps_mode','passthrough','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo','pipe:1')}else{$ProcessArguments};foreach($token in $arguments){[void]$startInfo.ArgumentList.Add($token)};$process=[Diagnostics.Process]::Start($startInfo);$stderrTask=$process.StandardError.ReadToEndAsync();if(-not[string]::IsNullOrWhiteSpace($ProcessReadyPath)){[Diagnostics.Stopwatch]$readyClock=[Diagnostics.Stopwatch]::StartNew();while(-not(Test-Path -LiteralPath $ProcessReadyPath -PathType Leaf)-and-not$process.HasExited-and$readyClock.ElapsedMilliseconds-lt5000){Start-Sleep -Milliseconds 25};if(-not(Test-Path -LiteralPath $ProcessReadyPath -PathType Leaf)){try{$process.Kill($true);$process.WaitForExit(10000)|Out-Null}catch{};throw'Visual decoder test seam did not become ready.'}};$comparisonFailure=$null;try{$result=[ReelForge.Gate0.SmokeVisualOracle]::Compare($process.StandardOutput.BaseStream,(Join-Path $FixtureRoot 'F1/f1-pattern-000.ppm'),(Join-Path $FixtureRoot 'F1/f1-pattern-001.ppm'),(Join-Path $FixtureRoot 'F1/f1-pattern-002.ppm'),(Join-Path $FixtureRoot 'F3/f3-alpha-magenta-50pct.rgba'))}catch{$comparisonFailure=$_;if(-not$process.HasExited){try{$process.Kill($true)}catch{};if(-not$process.WaitForExit(10000)){throw'Visual decoder could not be reaped after an oracle failure.'}}}finally{if(-not$process.HasExited-and-not$process.WaitForExit(10000)){try{$process.Kill($true);$process.WaitForExit(10000)|Out-Null}catch{}};$stderr=$stderrTask.GetAwaiter().GetResult();[IO.File]::WriteAllText($LogPath,$stderr,[Text.UTF8Encoding]::new($false))};if($null-ne$comparisonFailure){throw$comparisonFailure};if($process.ExitCode-ne0){throw'Strict streaming RGB decode failed.'};$records=for($index=0;$index-lt$result.FrameMeanAbsoluteErrors.Length;$index++){[ordered]@{frameIndex=$index;meanAbsoluteError=$result.FrameMeanAbsoluteErrors[$index];maximumPermitted=18;passed=$result.FrameMeanAbsoluteErrors[$index]-le18}};[IO.File]::WriteAllLines($MetricsPath,@($records|ForEach-Object{$_|ConvertTo-Json -Compress}),[Text.UTF8Encoding]::new($false));[ordered]@{passed=@($records|Where-Object{-not$_.passed}).Count-eq0;frames=$result.Frames;immediateEof=$result.ImmediateEof;maximumFrameMeanAbsoluteError=$result.MaximumFrameMeanAbsoluteError;threshold=18;perFrameMetrics=[IO.Path]::GetFileName($MetricsPath);rawFramesRetained=$false;processTree=[ordered]@{rootExited=$process.HasExited;orphanFree=$process.HasExited}}}

function Initialize-G05SmokeProcessInterop { if('ReelForge.Gate0.SmokeProcessIo'-as[type]){return};Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;namespace ReelForge.Gate0{[StructLayout(LayoutKind.Sequential)]public struct SmokeIoCounters{public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}public static class SmokeProcessIo{[DllImport("kernel32.dll",SetLastError=true)]public static extern bool GetProcessIoCounters(IntPtr process,out SmokeIoCounters counters);}}
'@ }
function Get-G05SmokeProcessSample([Diagnostics.Process]$Process,[Diagnostics.Stopwatch]$Clock){Initialize-G05SmokeProcessInterop;$Process.Refresh();$io=New-Object ReelForge.Gate0.SmokeIoCounters;if(-not[ReelForge.Gate0.SmokeProcessIo]::GetProcessIoCounters($Process.Handle,[ref]$io)){throw'GetProcessIoCounters failed.'};[ordered]@{monotonicMilliseconds=$Clock.ElapsedMilliseconds;totalProcessorMilliseconds=[int64]$Process.TotalProcessorTime.TotalMilliseconds;workingSetBytes=[int64]$Process.WorkingSet64;privateMemoryBytes=[int64]$Process.PrivateMemorySize64;readOperations=[uint64]$io.ReadOperationCount;writeOperations=[uint64]$io.WriteOperationCount;readBytes=[uint64]$io.ReadTransferCount;writeBytes=[uint64]$io.WriteTransferCount}}
function Invoke-G05SmokeObservedProcess([string]$Executable,[string[]]$Arguments,[string]$WorkingDirectory,[string]$StdoutPath,[string]$StderrPath){
    $startInfo=[Diagnostics.ProcessStartInfo]::new($Executable);$startInfo.UseShellExecute=$false;$startInfo.RedirectStandardOutput=$true;$startInfo.RedirectStandardError=$true;$startInfo.CreateNoWindow=$true;$startInfo.WorkingDirectory=$WorkingDirectory;foreach($token in $Arguments){[void]$startInfo.ArgumentList.Add($token)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$startInfo;$clock=[Diagnostics.Stopwatch]::StartNew();$startedUtc=[DateTimeOffset]::UtcNow;$samples=[Collections.Generic.List[object]]::new();$childIds=[Collections.Generic.HashSet[int]]::new();$stdoutTask=$null;$stderrTask=$null;$rootPid=$null
    try{
        if(-not$process.Start()){throw'Observed process did not start.'};$rootPid=$process.Id;$stdoutTask=$process.StandardOutput.ReadToEndAsync();$stderrTask=$process.StandardError.ReadToEndAsync()
        while(-not$process.HasExited){try{$samples.Add((Get-G05SmokeProcessSample $process $clock))}catch{if(-not$process.HasExited){throw}};foreach($child in @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $rootPid" -ErrorAction Stop)){[void]$childIds.Add([int]$child.ProcessId)};Start-Sleep -Milliseconds 250}
        $process.WaitForExit()
    }finally{
        if($null-ne$rootPid-and-not$process.HasExited){try{$process.Kill($true);$process.WaitForExit(10000)|Out-Null}catch{}}
        $clock.Stop()
        $stdout=if($null-ne$stdoutTask){$stdoutTask.GetAwaiter().GetResult()}else{''};$stderr=if($null-ne$stderrTask){$stderrTask.GetAwaiter().GetResult()}else{''}
        [IO.File]::WriteAllText($StdoutPath,$stdout,[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText($StderrPath,$stderr,[Text.UTF8Encoding]::new($false))
    }
    $orphans=@($childIds|Where-Object{Get-Process -Id $_ -ErrorAction SilentlyContinue});$first=if($samples.Count){$samples[0]}else{$null};$last=if($samples.Count){$samples[$samples.Count-1]}else{$null};$summary=[ordered]@{wallClockMilliseconds=$clock.ElapsedMilliseconds;sampleCount=$samples.Count;observedLogicalProcessors=[Environment]::ProcessorCount;peakWorkingSetBytes=if($samples.Count){($samples|ForEach-Object workingSetBytes|Measure-Object -Maximum).Maximum}else{$null};peakPrivateMemoryBytes=if($samples.Count){($samples|ForEach-Object privateMemoryBytes|Measure-Object -Maximum).Maximum}else{$null};meanNormalizedCpuPercent=if($samples.Count-ge2-and$last.monotonicMilliseconds-gt$first.monotonicMilliseconds){100.0*($last.totalProcessorMilliseconds-$first.totalProcessorMilliseconds)/(($last.monotonicMilliseconds-$first.monotonicMilliseconds)*[Environment]::ProcessorCount)}else{$null};readTransferBytes=if($samples.Count-ge2){[uint64]$last.readBytes-[uint64]$first.readBytes}else{$null};writeTransferBytes=if($samples.Count-ge2){[uint64]$last.writeBytes-[uint64]$first.writeBytes}else{$null}};[ordered]@{exitCode=$process.ExitCode;rootPid=$rootPid;startedUtc=$startedUtc;completedUtc=[DateTimeOffset]::UtcNow;summary=$summary;samples=@($samples);processTree=[ordered]@{observedChildPids=@($childIds);activeObservedChildrenAtClose=@($orphans|ForEach-Object Id);rootExited=$process.HasExited;orphanFree=$orphans.Count-eq0}}
}

Export-ModuleMember -Function Get-G05SmokeHash,New-G05SmokeSnapshotBinding,Assert-G05SmokeSnapshotBinding,Get-G05SmokeSnapshotHash,Get-G05SmokeCombinedGraph,Assert-G05SmokeRoot,Assert-G05SmokePath,ConvertTo-G05SmokePortableTokens,New-G05TypicalAudioTruth,Test-G05SmokeAudio,Convert-G05SmokeTicks,Get-G05SmokeDemuxer,Test-G05SmokeVisual,Invoke-G05SmokeObservedProcess
