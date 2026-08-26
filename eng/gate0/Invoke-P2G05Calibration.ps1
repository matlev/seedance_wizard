[CmdletBinding()]
param(
    [string] $RuntimeRoot,
    [string] $FixtureRoot,
    [string] $ArtifactRoot,
    [string] $OutputDirectory,
    [switch] $ContractOnly,
    [switch] $AppendRetention
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Gate 0 proof infrastructure only. This does not exercise application rendering,
# select a shipping runtime, establish a hardware minimum, or authorize Stage 2.
# Exact retained executable paths are required; PATH fallback is prohibited.
# Structured -progress output is measurement input; human-oriented FFmpeg stats
# are diagnostic only and are retained separately as stderr evidence.
# The contract selects libopenh264/aac MP4 and libvpx-vp9/libopus WebM; this
# runner reads those selections rather than permitting ambient encoder choice.
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$contractPath = Join-Path $PSScriptRoot 'g0.5-calibration-contract.json'
$retentionValidator = Join-Path $PSScriptRoot 'Test-Gate0ArtifactRetention.ps1'
$runtimeValidator = Join-Path $PSScriptRoot 'Validate-P2Runtime.ps1'
$retentionAppender = Join-Path $PSScriptRoot 'Add-Gate0RetainedProof.ps1'

function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }
function To-Portable([string] $Path) { $Path.Replace('\', '/') }
function New-RunSuffix { '{0:yyyyMMddTHHmmssfffZ}-{1}' -f [DateTimeOffset]::UtcNow, ([Guid]::NewGuid().ToString('N').Substring(0, 8)) }
function Write-JsonAtomic([string] $Path, [object] $Value) {
    $partial = "$Path.partial"
    try { [IO.File]::WriteAllText($partial, ($Value | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false)); Move-Item -LiteralPath $partial -Destination $Path -Force }
    finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force } }
}
function Assert-DirectDirectory([string] $Path, [string] $Label, [bool] $MustExist = $true, [string] $TrustedAncestor = '') {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathRooted($Path)) { throw "$Label must be an explicit rooted path." }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($MustExist -and -not (Test-Path -LiteralPath $full -PathType Container)) { throw "$Label does not exist or is not a directory: $full" }
    $boundary = if([string]::IsNullOrWhiteSpace($TrustedAncestor)){''}else{[IO.Path]::GetFullPath($TrustedAncestor).TrimEnd([IO.Path]::DirectorySeparatorChar)}
    if(-not[string]::IsNullOrEmpty($boundary)-and-not($full.StartsWith("$boundary$([IO.Path]::DirectorySeparatorChar)",[StringComparison]::OrdinalIgnoreCase))){throw "$Label is outside its approved trust boundary."}
    $cursor = if (Test-Path -LiteralPath $full) { Get-Item -LiteralPath $full -Force } else { Get-Item -LiteralPath ([IO.Path]::GetDirectoryName($full)) -Force }
    while ($null -ne $cursor) { if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label traverses a reparse-point: $($cursor.FullName)" };if(-not[string]::IsNullOrEmpty($boundary)-and$cursor.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($boundary,[StringComparison]::OrdinalIgnoreCase)){break}; $cursor = $cursor.Parent }
    if (Test-Path -LiteralPath $full) { foreach($item in @(Get-ChildItem -LiteralPath $full -Force -Recurse)) { if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "$Label contains a reparse-point: $($item.FullName)"} } }
    return $full
}
function Get-FileEvidence([string] $Path,[string] $Root) {
    $full=[IO.Path]::GetFullPath($Path);$rootFull=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if(-not $full.StartsWith("$rootFull$([IO.Path]::DirectorySeparatorChar)",[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence file escaped its required root.'}
    $item=Get-Item -LiteralPath $full -Force;[ordered]@{path=(To-Portable ([IO.Path]::GetRelativePath($rootFull,$full)));size=[int64]$item.Length;sha256=(Get-Sha256 $full)}
}
function Test-FixtureClosure([string] $Root) {
    $inventoryPath=Join-Path $PSScriptRoot 'fixture-source-inventory.json';$reportPath=Join-Path $Root 'generated-fixture-report.json'
    if(-not(Test-Path -LiteralPath $inventoryPath -PathType Leaf)-or-not(Test-Path -LiteralPath $reportPath -PathType Leaf)){throw 'Fixture inventory and generated fixture report are required.'}
    $inventory=Get-Content -LiteralPath $inventoryPath -Raw|ConvertFrom-Json -Depth 32;$report=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json -Depth 32
    if($report.profileId-ne'P2.BtbnLgplShared.WindowsX64.20260820'-or$report.externalMediaCommandsExecuted){throw 'Fixture report does not bind the approved P2 fixture generation boundary.'}
    $expected=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($entry in @($inventory.files)){$relative=[string]$entry.path;[void]$expected.Add($relative);$file=Assert-ContainedFile $Root $relative 'Fixture inventory entry';$item=Get-Item -LiteralPath $file -Force;if($item.Length-ne[int64]$entry.length-or(Get-Sha256 $file)-ne([string]$entry.sha256)){throw "Fixture hash/size mismatch: $relative"}}
    foreach($item in @(Get-ChildItem -LiteralPath $Root -Force -File -Recurse)){$relative=To-Portable ([IO.Path]::GetRelativePath($Root,$item.FullName));if($relative-ne'generated-fixture-report.json'-and-not$expected.Contains($relative)){throw "Fixture root contains an unapproved file: $relative"}}
    [ordered]@{inventory=(Get-FileEvidence $inventoryPath $repositoryRoot);report=(Get-FileEvidence $reportPath $Root)}
}
function Assert-ContainedFile([string] $Root, [string] $Relative, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Replace('\\','/').Split('/') | Where-Object { $_ -in @('', '.', '..') }) { throw "$Label is unsafe." }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $path = [IO.Path]::GetFullPath((Join-Path $rootFull $Relative))
    if (-not $path.StartsWith("$rootFull$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$Label is missing or escapes its root: $Relative" }
    if (((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label is a reparse-point: $Relative" }
    return $path
}
function Get-Property([object] $Object, [string] $Name, $Default = $null) { $p = $Object.PSObject.Properties[$Name]; if ($null -eq $p) { $Default } else { $p.Value } }
function New-Matrix([object] $Contract) {
    $rows = [Collections.Generic.List[object]]::new(); $logical = [Environment]::ProcessorCount
    foreach ($route in @($Contract.routes)) { foreach ($resolution in @($Contract.calibration.resolutions)) { foreach ($policy in @($Contract.calibration.threadPolicies)) {
        $codecThreads = switch ([string]$policy.codecThreads) { 'ceil(observedLogicalProcessors/2)' { [Math]::Ceiling($logical / 2) }; 'observedLogicalProcessors' { $logical }; default { [int]$policy.codecThreads } }
        $filterThreads = if ([string]$policy.filterThreads -eq 'ceil(observedLogicalProcessors/2)') { [Math]::Ceiling($logical / 2) } elseif ([string]$policy.filterThreads -eq 'observedLogicalProcessors') { $logical } elseif ([string]$policy.filterThreads -eq 'omit-use-runtime-default') { $null } else { [int]$policy.filterThreads }
        foreach ($kind in @('warmup','measured','measured')) { $rows.Add([ordered]@{ routeId=$route.id; resolutionId=$resolution.id; width=[int]$resolution.width; height=[int]$resolution.height; threadPolicyId=$policy.id; codecThreads=$codecThreads; filterThreads=$filterThreads; repetitionKind=$kind; repetitionOrdinal=(@($rows | Where-Object { $_.routeId -eq $route.id -and $_.resolutionId -eq $resolution.id -and $_.threadPolicyId -eq $policy.id -and $_.repetitionKind -eq $kind }).Count + 1) }) }
    } } }
    if ($rows.Count -ne 48) { throw "G0.5 contract matrix must expand to exactly 48 rows, observed $($rows.Count)." }
    return @($rows)
}
function New-ProcessIoInterop {
    if (-not ('ReelForge.Gate0.NativeIoCounters' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ReelForge.Gate0 {
  [StructLayout(LayoutKind.Sequential)] public struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
  public static class NativeIoCounters { [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS counters); }
}
'@
    }
}
function Get-ProcessSample([Diagnostics.Process] $Process, [Diagnostics.Stopwatch] $Clock) {
    $Process.Refresh(); $io = New-Object ReelForge.Gate0.IO_COUNTERS
    if (-not [ReelForge.Gate0.NativeIoCounters]::GetProcessIoCounters($Process.Handle, [ref]$io)) { throw 'GetProcessIoCounters failed.' }
    [ordered]@{ monotonicTimestampMilliseconds=$Clock.ElapsedMilliseconds; processId=$Process.Id; processStartIdentity=$Process.StartTime.ToUniversalTime().ToString('O'); totalProcessorTimeMilliseconds=[int64]$Process.TotalProcessorTime.TotalMilliseconds; workingSetBytes=[int64]$Process.WorkingSet64; privateMemoryBytes=[int64]$Process.PrivateMemorySize64; readOperationCount=[uint64]$io.ReadOperationCount; writeOperationCount=[uint64]$io.WriteOperationCount; otherOperationCount=[uint64]$io.OtherOperationCount; readTransferBytes=[uint64]$io.ReadTransferCount; writeTransferBytes=[uint64]$io.WriteTransferCount; otherTransferBytes=[uint64]$io.OtherTransferCount; rootProcessCount=1; observedProcessCount=$null; descendantProcessObservation='not-measured' }
}
function Get-Summary([object[]] $Samples, [int] $Logical, [int] $WallMilliseconds, [int] $ExitCode) {
    if (@($Samples).Count -lt 2) { return [ordered]@{ exitCode=$ExitCode; wallClockMilliseconds=$WallMilliseconds; observedLogicalProcessorCount=$Logical; sampleCount=@($Samples).Count; summaryDisposition='insufficient-samples' } }
    $cpu = [Collections.Generic.List[double]]::new(); $readRates=[Collections.Generic.List[double]]::new(); $writeRates=[Collections.Generic.List[double]]::new()
    for ($i=1; $i -lt $Samples.Count; $i++) { $a=$Samples[$i-1];$b=$Samples[$i];$dt=[double]($b.monotonicTimestampMilliseconds-$a.monotonicTimestampMilliseconds);if($dt -le 0){throw 'Process sample clock was not monotonic.'};$cpu.Add(100.0 * ([double]($b.totalProcessorTimeMilliseconds-$a.totalProcessorTimeMilliseconds)) / ($dt*$Logical));$readRates.Add(1000.0*([double]($b.readTransferBytes-$a.readTransferBytes))/$dt);$writeRates.Add(1000.0*([double]($b.writeTransferBytes-$a.writeTransferBytes))/$dt) }
    $first=$Samples[0];$last=$Samples[$Samples.Count-1]
    $sampledMilliseconds=[double]($last.monotonicTimestampMilliseconds-$first.monotonicTimestampMilliseconds);$cpuDelta=[double]($last.totalProcessorTimeMilliseconds-$first.totalProcessorTimeMilliseconds);$readDelta=[uint64]$last.readTransferBytes-[uint64]$first.readTransferBytes;$writeDelta=[uint64]$last.writeTransferBytes-[uint64]$first.writeTransferBytes
    [ordered]@{ exitCode=$ExitCode;wallClockMilliseconds=$WallMilliseconds;sampledWallClockMilliseconds=$sampledMilliseconds;observedLogicalProcessorCount=$Logical;sampleCount=$Samples.Count;cpuNormalizationFormula='100 * processCpuTimeDeltaMilliseconds / (sampleWallTimeDeltaMilliseconds * observedLogicalProcessorCount)';meanNormalizedCpuPercent=(100.0*$cpuDelta/($sampledMilliseconds*$Logical));peakNormalizedCpuPercent=($cpu|Measure-Object -Maximum).Maximum;peakWorkingSetBytes=($Samples|ForEach-Object workingSetBytes|Measure-Object -Maximum).Maximum;peakPrivateMemoryBytes=($Samples|ForEach-Object privateMemoryBytes|Measure-Object -Maximum).Maximum;readOperationDelta=([uint64]$last.readOperationCount-[uint64]$first.readOperationCount);writeOperationDelta=([uint64]$last.writeOperationCount-[uint64]$first.writeOperationCount);otherOperationDelta=([uint64]$last.otherOperationCount-[uint64]$first.otherOperationCount);readTransferByteDelta=$readDelta;writeTransferByteDelta=$writeDelta;otherTransferByteDelta=([uint64]$last.otherTransferBytes-[uint64]$first.otherTransferBytes);meanReadBytesPerSecond=(1000.0*[double]$readDelta/$sampledMilliseconds);peakIntervalReadBytesPerSecond=($readRates|Measure-Object -Maximum).Maximum;meanWriteBytesPerSecond=(1000.0*[double]$writeDelta/$sampledMilliseconds);peakIntervalWriteBytesPerSecond=($writeRates|Measure-Object -Maximum).Maximum }
}
function Get-PpmPixels([string] $Path) {
    $bytes=[IO.File]::ReadAllBytes($Path);$tokens=[Collections.Generic.List[string]]::new();$offset=0
    while($tokens.Count-lt 4){while($offset-lt$bytes.Length-and$bytes[$offset]-in 9,10,13,32){$offset++};$start=$offset;while($offset-lt$bytes.Length-and$bytes[$offset]-notin 9,10,13,32){$offset++};$tokens.Add([Text.Encoding]::ASCII.GetString($bytes,$start,$offset-$start))}
    if($tokens[0]-ne'P6'-or$tokens[1]-ne'320'-or$tokens[2]-ne'180'-or$tokens[3]-ne'255'){throw 'F1 PPM geometry oracle failed.'};while($offset-lt$bytes.Length-and$bytes[$offset]-in 9,10,13,32){$offset++};$pixels=[byte[]]::new($bytes.Length-$offset);[Array]::Copy($bytes,$offset,$pixels,0,$pixels.Length);$pixels
}
function Get-Mae([byte[]] $Expected,[byte[]] $Actual){if($Expected.Length-ne$Actual.Length){throw 'Visual oracle bytes have unequal lengths.'};[double]$sum=0;for($i=0;$i-lt$Expected.Length;$i++){$sum+=[Math]::Abs([int]$Expected[$i]-[int]$Actual[$i])};$sum/$Expected.Length}
function Get-TonePower([byte[]] $Bytes,[int] $Frequency,[int] $Channel){$samples=[int]($Bytes.Length/4);[double]$re=0;[double]$im=0;for($n=0;$n-lt$samples;$n++){[int16]$v=[BitConverter]::ToInt16($Bytes,(($n*2+$Channel)*2));$a=2*[Math]::PI*$Frequency*$n/48000;$re+=$v*[Math]::Cos($a);$im-=$v*[Math]::Sin($a)};$re*$re+$im*$im}
function Assert-IndexedFrameTimestamps([object[]] $Frames,[int64] $TimeBaseNumerator,[int64] $TimeBaseDenominator) { if($Frames.Count-ne200){throw "Frame-count oracle failed: expected 200, observed $($Frames.Count)."};for($index=0;$index-lt200;$index++){$pts=[int64]$Frames[$index].best_effort_timestamp;$expectedMilliseconds=[int64]$index*40;if(($pts*1000*$TimeBaseNumerator)-ne($expectedMilliseconds*$TimeBaseDenominator)){throw "Frame timestamp oracle failed at frame ${index}: integer/rational PTS does not equal $expectedMilliseconds ms."}} }
function New-AudioOracleInterop {
    if (-not ('ReelForge.Gate0.AudioOracle' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
namespace ReelForge.Gate0 {
  public static class AudioOracle {
    public static int AssertLoopingStereoS16(byte[] expectedLoop, byte[] actual, int expectedSamplesPerChannel, int maximumAbsoluteDelta) {
      if (expectedLoop == null || expectedLoop.Length == 0 || expectedLoop.Length % 4 != 0) throw new InvalidOperationException("Retained PCM truth must contain complete stereo s16le sample frames.");
      if (actual == null || actual.Length != checked(expectedSamplesPerChannel * 4)) throw new InvalidOperationException($"Audio sample-count oracle failed: expected exactly {expectedSamplesPerChannel} samples per channel, observed {(actual == null ? 0 : actual.Length / 4)}.");
      int sourceSamples = expectedLoop.Length / 4, observedMaximum = 0;
      for (int sample = 0; sample < expectedSamplesPerChannel; sample++) {
        int expectedFrameOffset = (sample % sourceSamples) * 4, actualFrameOffset = sample * 4;
        for (int channel = 0; channel < 2; channel++) {
          short expected = BitConverter.ToInt16(expectedLoop, expectedFrameOffset + channel * 2);
          short observed = BitConverter.ToInt16(actual, actualFrameOffset + channel * 2);
          int delta = Math.Abs((int)observed - expected);
          if (delta > observedMaximum) observedMaximum = delta;
          if (delta > maximumAbsoluteDelta) throw new InvalidOperationException($"Audio waveform oracle failed at sample {sample}, channel {channel}: absolute delta {delta} exceeds {maximumAbsoluteDelta}.");
        }
      }
      return observedMaximum;
    }
  }
}
'@
    }
}
function Assert-AudioWaveform([byte[]] $ExpectedLoop,[byte[]] $Actual,[int] $ExpectedSamplesPerChannel=384000,[int] $MaximumAbsoluteDelta=3072) { New-AudioOracleInterop;[ReelForge.Gate0.AudioOracle]::AssertLoopingStereoS16($ExpectedLoop,$Actual,$ExpectedSamplesPerChannel,$MaximumAbsoluteDelta) }
function Assert-StreamDescriptor([object] $Video,[object] $Audio,[object] $Row,[object] $Route) {
    if([string]$Video.avg_frame_rate-ne'25/1'-or[string]$Video.r_frame_rate-ne'25/1'){throw "Frame-rate oracle failed: expected avg_frame_rate and r_frame_rate 25/1, observed $($Video.avg_frame_rate) and $($Video.r_frame_rate)."}
    if($Route.videoEncoder-eq'libopenh264'-and-not([string]$Video.profile).Equals('Constrained Baseline',[StringComparison]::OrdinalIgnoreCase)){throw "H.264 constrained-baseline profile oracle failed: observed $($Video.profile)."}
    if($Route.audioEncoder-eq'aac'-and$Audio.profile-ne'LC'){throw 'AAC-LC profile oracle failed.'}
    if([int]$Audio.sample_rate-ne48000-or[int]$Audio.channels-ne2){throw "Audio descriptor oracle failed: expected 48000 Hz stereo, observed $($Audio.sample_rate) Hz and $($Audio.channels) channels."}
    $true
}
function Get-CancellationEvidenceDisposition([object] $Cancellation) {
    if($Cancellation.forcedTermination-and$Cancellation.terminationTrigger-eq'requested-cancellation'-and$null-ne$Cancellation.activeProgressMonotonicMs-and$null-ne$Cancellation.requestMonotonicMs){return 'forced-termination-evidence'}
    switch([string]$Cancellation.terminationTrigger){'active-progress-deadline'{return 'blocked-startup'};'hard-wall-clock'{return 'blocked-hard-wall-clock'};'output-size-ceiling'{return 'blocked-output-size-ceiling'};'none'{return 'not-exercised'};default{return 'failed-unexpected-termination'}}
}
function Get-FinalCancellationRecordStatus([object] $Record) {
    if($Record.status-eq'failed'-or$null-eq$Record.cancellation){return [string]$Record.status}
    Get-CancellationEvidenceDisposition $Record.cancellation
}
function New-FfmpegArguments([object] $Row, [object] $Route, [object] $Contract, [string] $FixtureRoot, [string] $Output, [int] $Duration) {
    $pattern=Assert-ContainedFile $FixtureRoot 'F1/f1-pattern-000.ppm' 'F1 source'; $sequence=Join-Path (Split-Path -Parent $pattern) 'f1-pattern-%03d.ppm'; $pcm=Assert-ContainedFile $FixtureRoot 'F1/f1-sync-440hz-880hz-48000-stereo.pcm' 'F1 source'
    $args=[Collections.Generic.List[string]]::new(); @('-nostdin','-hide_banner','-progress','pipe:1','-stats_period','0.5','-stream_loop','-1','-framerate','25','-f','image2','-c:v','ppm','-i',$sequence,'-stream_loop','-1','-f','s16le','-c:a','pcm_s16le','-ar','48000','-ac','2','-i',$pcm) | ForEach-Object { $args.Add($_) }; if($null -ne $Row.filterThreads){$args.Add('-filter_threads');$args.Add([string]$Row.filterThreads)};$args.Add('-filter:v');$args.Add("scale=$($Row.width):$($Row.height):flags=bilinear,format=yuv420p,setpts=PTS-STARTPTS");$args.Add('-filter:a');$args.Add('aresample=48000,aformat=sample_fmts=s16:sample_rates=48000:channel_layouts=stereo,asetpts=PTS-STARTPTS');@('-map','0:v:0','-map','1:a:0','-threads:v',[string]$Row.codecThreads,'-c:v',$Route.videoEncoder) | ForEach-Object {$args.Add($_)}; foreach($option in @($Route.videoOptions)){$args.Add([string]$option)};@('-c:a',$Route.audioEncoder) | ForEach-Object {$args.Add($_)};foreach($option in @($Route.audioOptions)){$args.Add([string]$option)};foreach($option in @($Route.muxerOptions)){$args.Add([string]$option)};@('-t',[string]$Duration,'-f',$Route.muxer,'-y',$Output) | ForEach-Object {$args.Add($_)};@($args)
}
function Invoke-ObservedFfmpeg([string] $Ffmpeg, [string[]] $Arguments, [string] $LogPrefix, [string] $OutputPath, [bool] $CancellationProbe, [object] $Contract) {
    New-ProcessIoInterop; $si=[Diagnostics.ProcessStartInfo]::new();$si.FileName=$Ffmpeg;$si.UseShellExecute=$false;$si.RedirectStandardOutput=$true;$si.RedirectStandardError=$true;foreach($a in $Arguments){[void]$si.ArgumentList.Add($a)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$si;$stdout=[Collections.Generic.List[string]]::new();$stderr=[Collections.Generic.List[string]]::new();$progress=[Collections.Generic.List[object]]::new();$samples=[Collections.Generic.List[object]]::new();$clock=[Diagnostics.Stopwatch]::StartNew();$lastSample=-500;$logical=[Environment]::ProcessorCount;$startedUtc=[DateTimeOffset]::UtcNow;$requestUtc=$null;$requestMonotonic=$null;$activeProgressMonotonicMs=$null;$exitUtc=$null;$exitMonotonic=$null;$escalationUtc=$null;$terminationReason='none';$killed=$false;$exitCode=$null;$started=$false
    try {
        if(-not $process.Start()){throw 'Could not start exact FFmpeg executable.'};$started=$true;$outTask=$process.StandardOutput.ReadLineAsync();$errTask=$process.StandardError.ReadLineAsync()
        while($null-ne$outTask-or$null-ne$errTask-or-not$process.HasExited){
            if($null-ne$outTask-and$outTask.IsCompleted){$line=$outTask.GetAwaiter().GetResult();if($null-eq$line){$outTask=$null}else{[void]$stdout.Add($line);$entry=[ordered]@{monotonicTimestampMilliseconds=$clock.ElapsedMilliseconds;line=$line};if($line -match '^[A-Za-z0-9_]+='){[void]$progress.Add($entry);if($line -match '^out_time_ms=' -and $null-eq$activeProgressMonotonicMs){$activeProgressMonotonicMs=$clock.ElapsedMilliseconds}};$outTask=$process.StandardOutput.ReadLineAsync()}}
            if($null-ne$errTask-and$errTask.IsCompleted){$line=$errTask.GetAwaiter().GetResult();if($null-eq$line){$errTask=$null}else{[void]$stderr.Add($line);$errTask=$process.StandardError.ReadLineAsync()}}
            if(-not$process.HasExited-and($clock.ElapsedMilliseconds-$lastSample-ge500)){try{$samples.Add((Get-ProcessSample $process $clock));$lastSample=$clock.ElapsedMilliseconds}catch{if(-not$process.HasExited){throw}} }
            if($CancellationProbe-and-not$process.HasExited){
                $stop=$false;if($null-ne$activeProgressMonotonicMs-and$null-eq$requestUtc-and($clock.ElapsedMilliseconds-$activeProgressMonotonicMs-ge[int]$Contract.cancellation.requestAfterConfirmedProgressMilliseconds)){$requestUtc=[DateTimeOffset]::UtcNow;$requestMonotonic=$clock.ElapsedMilliseconds;$terminationReason='requested-cancellation';$stop=$true}
                elseif($null-eq$activeProgressMonotonicMs-and$clock.Elapsed.TotalSeconds-ge[int]$Contract.cancellation.activeProgressDeadlineSeconds){$terminationReason='active-progress-deadline';$stop=$true}
                elseif($clock.Elapsed.TotalSeconds-ge[int]$Contract.cancellation.hardWallClockLimitSeconds){$terminationReason='hard-wall-clock';$stop=$true}
                elseif((Test-Path -LiteralPath $OutputPath -PathType Leaf) -and ((Get-Item -LiteralPath $OutputPath -Force).Length -ge [int64]$Contract.cancellation.maximumOutputBytes)){$terminationReason='output-size-ceiling';$stop=$true}
                if($stop-and-not$killed){$killed=$true;$escalationUtc=[DateTimeOffset]::UtcNow;$process.Kill($true)}
            }
            if(-not $process.HasExited){Start-Sleep -Milliseconds 20}
        }
        $process.WaitForExit();$exitCode=$process.ExitCode;$exitMonotonic=$clock.ElapsedMilliseconds;$exitUtc=[DateTimeOffset]::UtcNow
    } catch {
        [void]$stderr.Add("PROCESS_OBSERVATION_FAILURE: $($_.Exception.ToString())")
        throw
    } finally {
        if($started-and$null-eq$exitCode-and$process.HasExited){$exitCode=$process.ExitCode;$exitMonotonic=$clock.ElapsedMilliseconds;$exitUtc=[DateTimeOffset]::UtcNow}
        [IO.File]::WriteAllLines("$LogPrefix.stdout.txt",@($stdout),[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllLines("$LogPrefix.progress.ndjson",@($progress|ForEach-Object{$_|ConvertTo-Json -Compress}),[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllLines("$LogPrefix.stderr.txt",@($stderr),[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllLines("$LogPrefix.samples.ndjson",@($samples|ForEach-Object{$_|ConvertTo-Json -Compress}),[Text.UTF8Encoding]::new($false));$process.Dispose()
    }
    [ordered]@{exitCode=$exitCode;startedAtUtc=$startedUtc;exitAtUtc=$exitUtc;progress=@($progress);samples=@($samples);summary=(Get-Summary @($samples) $logical $clock.ElapsedMilliseconds $exitCode);childProcessObservation=[ordered]@{rootProcessCount=1;observedProcessCount=$null;descendants='not-measured; root metrics authoritative'};cancellation=[ordered]@{activeProgressMonotonicMs=$activeProgressMonotonicMs;requestMonotonicMs=$requestMonotonic;exitMonotonicMs=$exitMonotonic;requestUtc=$requestUtc;exitUtc=$exitUtc;requestToExitMilliseconds=if($null-ne$requestMonotonic-and$null-ne$exitMonotonic){$exitMonotonic-$requestMonotonic}else{$null};escalationUtc=$escalationUtc;terminationTrigger=$terminationReason;forcedTermination=$killed;alreadyCompletedDisposition=if($CancellationProbe-and-not$killed-and$terminationReason-eq'none'){'not-exercised'}else{$null};neverActiveDisposition=if($CancellationProbe-and$terminationReason-eq'active-progress-deadline'){'blocked-startup'}else{$null}}}
}
function Invoke-IndependentOracle([string] $Ffmpeg, [string] $Ffprobe, [string] $Path, [object] $Row, [object] $Route, [string] $Work, [Parameter(Mandatory)][string] $FixtureRoot) {
    # Fresh explicit demux/decode evidence. This proof checks every decoded frame;
    # media payload comparisons intentionally remain outside product services.
    $forcedDemuxer=if($Route.container-eq'mp4'){'mp4'}else{'webm'};$probeArguments=@('-v','error','-f',$forcedDemuxer,'-show_streams','-show_frames','-show_packets','-show_format','-of','json',$Path);$probe=& $Ffprobe @probeArguments 2>&1;if($LASTEXITCODE -ne 0){throw 'Independent explicit ffprobe inspection failed.'};$parsed=$probe|Out-String|ConvertFrom-Json;$video=@($parsed.streams|Where-Object codec_type -eq 'video');$audio=@($parsed.streams|Where-Object codec_type -eq 'audio');if($video.Count-ne 1-or$audio.Count-ne 1-or$video[0].codec_name-ne$Route.outputDecoders[0]-or$audio[0].codec_name-ne$Route.outputDecoders[1]-or[int]$video[0].width-ne$Row.width-or[int]$video[0].height-ne$Row.height){throw 'Independent stream/container oracle failed.'};Assert-StreamDescriptor $video[0] $audio[0] $Row $Route|Out-Null;$frames=@($parsed.frames|Where-Object media_type -eq 'video');$parts=([string]$video[0].time_base).Split('/');if($parts.Count-ne2){throw 'Video time-base oracle failed.'};Assert-IndexedFrameTimestamps $frames ([int64]$parts[0]) ([int64]$parts[1]);if([Math]::Abs(([double]$parsed.format.duration*1000)-8000)-gt60){throw 'Container presentation-end duration oracle failed.'}
    $videoRaw=Join-Path $Work 'oracle-video.rgb';$videoDecodeArguments=@('-v','error','-f',$forcedDemuxer,'-c:v',$Route.outputDecoders[0],'-i',$Path,'-map','0:v:0','-an','-vf','scale=320:180:flags=bilinear,format=rgb24','-f','rawvideo','-y',$videoRaw);& $Ffmpeg @videoDecodeArguments;if($LASTEXITCODE-ne 0){throw 'Independent video complete-decode failed.'};$all=[IO.File]::ReadAllBytes($videoRaw);$frameBytes=172800;if($all.Length-ne(200*$frameBytes)){throw 'Full frame identity decode length oracle failed.'};$expected=@((Get-PpmPixels (Assert-ContainedFile $FixtureRoot 'F1/f1-pattern-000.ppm' 'F1 identity')),(Get-PpmPixels (Assert-ContainedFile $FixtureRoot 'F1/f1-pattern-001.ppm' 'F1 identity')),(Get-PpmPixels (Assert-ContainedFile $FixtureRoot 'F1/f1-pattern-002.ppm' 'F1 identity')));$maes=[Collections.Generic.List[double]]::new();for($index=0;$index-lt 200;$index++){$actual=[byte[]]::new($frameBytes);[Array]::Copy($all,$index*$frameBytes,$actual,0,$frameBytes);$matching=Get-Mae $expected[$index%3] $actual;$other=@(0..2|Where-Object{$_-ne($index%3)}|ForEach-Object{Get-Mae $expected[$_] $actual}|Measure-Object -Minimum).Minimum;if($matching-gt 18-or$matching-ge$other){throw "Frame identity-cycle oracle failed at decoded frame $index."};$maes.Add($matching)}
    $raw=Join-Path $Work 'oracle-audio.s16le';$audioDecodeArguments=@('-v','error','-f',$forcedDemuxer,'-c:a',$Route.outputDecoders[1],'-i',$Path,'-map','0:a:0','-vn','-f','s16le','-y',$raw);& $Ffmpeg @audioDecodeArguments;if($LASTEXITCODE-ne 0){throw 'Independent audio complete-decode failed.'};$audioBytes=[IO.File]::ReadAllBytes($raw);$sampleCount=[int]($audioBytes.Length/4);$pcmTruth=[IO.File]::ReadAllBytes((Assert-ContainedFile $FixtureRoot 'F1/f1-sync-440hz-880hz-48000-stereo.pcm' 'F1 audio truth'));$maximumAudioSampleDelta=Assert-AudioWaveform $pcmTruth $audioBytes 384000 3072;$l440=Get-TonePower $audioBytes 440 0;$l880=Get-TonePower $audioBytes 880 0;$r440=Get-TonePower $audioBytes 440 1;$r880=Get-TonePower $audioBytes 880 1;if($l440-le$l880-or$r880-le$r440){throw 'Audio left/right tone identity oracle failed.'};[ordered]@{passed=$true;artifactSha256=(Get-Sha256 $Path);artifactBytes=(Get-Item $Path).Length;container=$Route.container;forcedDemuxer=$forcedDemuxer;contractOutputDemuxer=$Route.outputDemuxer;explicitStreams=@("0:v:0:$($Route.outputDecoders[0])","1:a:0:$($Route.outputDecoders[1])");dimensions="$($Row.width)x$($Row.height)";frameRate='25/1';frameCount=200;firstTimestampTick=0;finalTimestampTick=7960;allFrameIdentityCycle=$true;meanAbsoluteErrorPerFrame=@($maes);strictCompleteDecode=$true;audioSampleRate=[int]$audio[0].sample_rate;audioChannels=[int]$audio[0].channels;audioSamplesPerChannel=$sampleCount;maximumAllowedAbsoluteAudioSampleDelta=3072;observedMaximumAbsoluteAudioSampleDelta=$maximumAudioSampleDelta;left440Power=$l440;left880Power=$l880;right440Power=$r440;right880Power=$r880}
}
function Get-PartialOutputDisposition([string] $Path,[string] $Reason) {
    if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){return [ordered]@{disposition='not-created';reason=$Reason}}
    $before=Get-FileEvidence $Path (Split-Path -Parent $Path)
    try { Remove-Item -LiteralPath $Path -Force;return [ordered]@{disposition='removed-unvalidated';reason=$Reason;preDelete=$before} }
    catch { return [ordered]@{disposition='retained-after-cleanup-failure';reason=$Reason;preDelete=$before;cleanupError=$_.Exception.Message} }
}
function Get-EnvironmentIdentity([string] $Stage,[string] $Runtime,[string] $Fixture,[string] $Artifact) {
    $os=Get-CimInstance Win32_OperatingSystem;$cpu=Get-CimInstance Win32_Processor|Select-Object -First 1;$gpu=@(Get-CimInstance Win32_VideoController|ForEach-Object{[ordered]@{name=$_.Name;driverVersion=$_.DriverVersion;adapterRamBytes=$_.AdapterRAM}});$disk=Get-Item -LiteralPath $Stage
    $memory=[int64]$os.TotalVisibleMemorySize*1024;$cpuMatch=$cpu.Name -match 'Ryzen 7 3700X';$memoryMatch=$memory -ge 30GB -and $memory -le 34GB;$gpuMatch=@($gpu|Where-Object{$_.name-match'RTX 3070 Ti'}).Count-gt0
    [ordered]@{referenceProfileComparison=[ordered]@{classification='initial-reference-profile-not-public-minimum';cpuExpectedContains='Ryzen 7 3700X';cpuMatched=$cpuMatch;memoryExpectedGiB=32;memoryToleranceGiB=2;memoryMatched=$memoryMatch;gpuExpectedContains='RTX 3070 Ti';gpuMatched=$gpuMatch;matched=($cpuMatch-and$memoryMatch-and$gpuMatch)};os=[ordered]@{caption=$os.Caption;version=$os.Version;build=$os.BuildNumber};cpu=[ordered]@{name=$cpu.Name;logicalProcessors=[Environment]::ProcessorCount};memoryBytes=$memory;gpu=$gpu;gpuDisposition='not-applicable-software-P2-route';storageTarget=[ordered]@{root=$disk.PSDrive.Root;volumeFormat=$disk.PSDrive.Provider.Name};hashes=[ordered]@{contract=(Get-FileEvidence $contractPath $repositoryRoot);harness=(Get-FileEvidence $PSCommandPath $repositoryRoot);retentionManifest=(Get-FileEvidence (Join-Path $PSScriptRoot 'artifact-retention-manifest.json') $repositoryRoot);fixtureInventory=(Get-FileEvidence (Join-Path $PSScriptRoot 'fixture-source-inventory.json') $repositoryRoot)};runtimeRoot=$Runtime;fixtureRoot=$Fixture;artifactRoot=$Artifact}
}

$state=[ordered]@{schemaVersion=1;contractId='Gate0.G05.Calibration.V1';profileId='P2.BtbnLgplShared.WindowsX64.20260820';proofRunId=$null;proofExecutionStatus='not-started';status='started';startedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');completedAtUtc=$null;contractOnly=[bool]$ContractOnly;appendRetentionRequested=[bool]$AppendRetention;preflight=[ordered]@{status='not-run';error=$null};matrix=@();attempts=@();cancellationProbes=@();snapshots=@();error=$null;limitations='Exploratory sequential calibration only; no Stage 2, UI, public-hardware, shipping-runtime, distribution, or legal conclusion.'}
if(-not(Test-Path -LiteralPath $contractPath -PathType Leaf)){throw 'G0.5 calibration contract is missing.'};$contract=Get-Content -LiteralPath $contractPath -Raw|ConvertFrom-Json -Depth 64;$state.matrix=New-Matrix $contract
if($ContractOnly){$state.status='contract-only';$state.completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');if($OutputDirectory){$out=Assert-DirectDirectory $OutputDirectory 'OutputDirectory' $false;if(Test-Path $out){throw 'Contract-only OutputDirectory must be new.'};[IO.Directory]::CreateDirectory($out)|Out-Null;Write-JsonAtomic (Join-Path $out 'g0.5-calibration-evidence.json') $state};$state|ConvertTo-Json -Depth 100;exit 0}

$runSuffix=New-RunSuffix;$trustedProjectParent=[IO.Path]::GetDirectoryName($repositoryRoot);$approvedArtifacts=Join-Path $trustedProjectParent 'ReelForge.Gate0Artifacts';$stagingBase=Join-Path $trustedProjectParent 'ReelForge.Gate0Staging';$stage=Join-Path $stagingBase "g05-calibration-$runSuffix"
try {
    # Every non-contract attempt receives persistent staging before any preflight
    # can fail, so a closed failure record cannot be lost with its diagnostics.
    if(-not $AppendRetention){throw 'Live calibration requires explicit -AppendRetention so every attempted closure is retained and revalidated before reporting.'}
    [IO.Directory]::CreateDirectory($stagingBase)|Out-Null
    if(Test-Path $stage){throw 'Collision-resistant staging directory already exists.'}
    [IO.Directory]::CreateDirectory($stage)|Out-Null
    [void](Assert-DirectDirectory $stagingBase 'Staging base' $true $trustedProjectParent);[void](Assert-DirectDirectory $stage 'Staging directory' $true $trustedProjectParent)
    $state.proofRunId="g05-calibration-$runSuffix";$snapshotDirectory=Join-Path $stage 'snapshots';[IO.Directory]::CreateDirectory($snapshotDirectory)|Out-Null;foreach($source in @($contractPath,$PSCommandPath)){Copy-Item -LiteralPath $source -Destination (Join-Path $snapshotDirectory (Split-Path -Leaf $source)) -ErrorAction Stop};$state.snapshots=@((Get-ChildItem -LiteralPath $snapshotDirectory -File|ForEach-Object{Get-FileEvidence $_.FullName $stage}))
    if([string]::IsNullOrWhiteSpace($RuntimeRoot)-or[string]::IsNullOrWhiteSpace($FixtureRoot)-or[string]::IsNullOrWhiteSpace($ArtifactRoot)){throw 'RuntimeRoot, FixtureRoot, and ArtifactRoot are required unless -ContractOnly is used.'}
    $artifact=Assert-DirectDirectory $ArtifactRoot 'ArtifactRoot' $true $trustedProjectParent;if(-not $artifact.Equals($approvedArtifacts,[StringComparison]::OrdinalIgnoreCase)){throw "ArtifactRoot must be the approved repository sibling: $approvedArtifacts"}
    $runtime=Assert-DirectDirectory $RuntimeRoot 'RuntimeRoot' $true $trustedProjectParent;$fixtures=Assert-DirectDirectory $FixtureRoot 'FixtureRoot' $true $trustedProjectParent;$approvedFixture=Join-Path $artifact 'fixtures';if(-not $fixtures.Equals($approvedFixture,[StringComparison]::OrdinalIgnoreCase)){throw "FixtureRoot must be the exact retained ArtifactRoot/fixtures directory: $approvedFixture"}
    & $retentionValidator -ArtifactRoot $artifact;if($LASTEXITCODE-ne 0){throw 'Retained corpus preflight failed.'};$state.fixtureClosure=Test-FixtureClosure $fixtures
    & $runtimeValidator -RuntimeRoot $runtime -EvidencePath (Join-Path $stage 'runtime-identity.json');if($LASTEXITCODE-ne 0){throw 'P2 runtime preflight failed.'}
    $state.preflight.status='passed';$ffmpeg=Assert-ContainedFile $runtime 'bin/ffmpeg.exe' 'P2 FFmpeg';$ffprobe=Assert-ContainedFile $runtime 'bin/ffprobe.exe' 'P2 FFprobe';$logs=Join-Path $stage 'logs';$media=Join-Path $stage 'media';$work=Join-Path $stage 'work';@($logs,$media,$work)|ForEach-Object{[IO.Directory]::CreateDirectory($_)|Out-Null};$state.environment=Get-EnvironmentIdentity $stage $runtime $fixtures $artifact;if(-not $state.environment.referenceProfileComparison.matched){$state.preflight.status='blocked-reference-profile-mismatch';throw 'Reference profile comparison did not match the approved calibration machine.'}
    foreach($row in $state.matrix){$route=@($contract.routes|Where-Object id -eq $row.routeId)[0];$extension=if($route.container-eq'mp4'){'mp4'}else{'webm'};$name="$($row.routeId.Split('.')[-1])-$($row.resolutionId)-$($row.threadPolicyId)-$($row.repetitionKind)-$($row.repetitionOrdinal).$extension";$output=Join-Path $media $name;$record=[ordered]@{row=$row;status='started';startedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');command=$null;measurement=$null;rawEvidence=$null;oracle=$null;error=$null};$state.attempts+=,$record;try{$args=New-FfmpegArguments $row $route $contract $fixtures $output ([int]$contract.calibration.durationSeconds);$record.command=[ordered]@{executable=$ffmpeg;arguments=$args;components=[ordered]@{inputDemuxers=@($route.inputDemuxers);inputDecoders=@($route.inputDecoders);filters=@($route.filters);streamMaps=@($route.streamMaps);videoEncoder=$route.videoEncoder;audioEncoder=$route.audioEncoder;muxer=$route.muxer;probeDemuxerToken=if($route.container-eq'mp4'){'mp4'}else{'webm'};decodeDemuxerToken=if($route.container-eq'mp4'){'mp4'}else{'webm'};outputDemuxerDescriptor=$route.outputDemuxer;outputDecoders=@($route.outputDecoders);codecThreads=$row.codecThreads;filterThreads=$row.filterThreads;complexFilterThreads='not-applicable-no-complex-filtergraph'}};$observed=Invoke-ObservedFfmpeg $ffmpeg $args (Join-Path $logs $name) $output $false $contract;$record.measurement=$observed.summary;$record.rawEvidence=[ordered]@{stdout=(Get-FileEvidence "$($logs)\\$name.stdout.txt" $stage);stderr=(Get-FileEvidence "$($logs)\\$name.stderr.txt" $stage);progress=(Get-FileEvidence "$($logs)\\$name.progress.ndjson" $stage);samples=(Get-FileEvidence "$($logs)\\$name.samples.ndjson" $stage)};if($observed.exitCode-ne 0){throw "Calibration command failed: $name"};$record.oracle=Invoke-IndependentOracle $ffmpeg $ffprobe $output $row $route $work $fixtures;$record.status='passed'}catch{$record.status='failed';$record.error=$_.Exception.ToString()};$record.completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O') }
    foreach($route in @($contract.routes)){$row=[ordered]@{routeId=$route.id;resolutionId='1080p';width=1920;height=1080;threadPolicyId='auto';codecThreads=0;filterThreads=$null;repetitionKind='cancellation';repetitionOrdinal=1};$output=Join-Path $media ("cancellation-$($route.container).partial");$logPrefix=Join-Path $logs "cancellation-$($route.container)";$record=[ordered]@{routeId=$route.id;status='started';command=$null;measurement=$null;rawEvidence=$null;cancellation=$null;partialOutputDisposition=$null;error=$null};$state.cancellationProbes+=,$record;try{$args=New-FfmpegArguments $row $route $contract $fixtures $output ([int]$contract.cancellation.targetOutputDurationSeconds);$record.command=[ordered]@{executable=$ffmpeg;arguments=$args;components=[ordered]@{inputDemuxers=@($route.inputDemuxers);inputDecoders=@($route.inputDecoders);filters=@($route.filters);streamMaps=@($route.streamMaps);videoEncoder=$route.videoEncoder;audioEncoder=$route.audioEncoder;muxer=$route.muxer;codecThreads=0;filterThreads=$null;complexFilterThreads='not-applicable-no-complex-filtergraph'}};$probe=Invoke-ObservedFfmpeg $ffmpeg $args $logPrefix $output $true $contract;$record.measurement=$probe.summary;$record.rawEvidence=[ordered]@{stdout=(Get-FileEvidence "$logPrefix.stdout.txt" $stage);stderr=(Get-FileEvidence "$logPrefix.stderr.txt" $stage);progress=(Get-FileEvidence "$logPrefix.progress.ndjson" $stage);samples=(Get-FileEvidence "$logPrefix.samples.ndjson" $stage)};$record.cancellation=$probe.cancellation;$record.partialOutputDisposition=Get-PartialOutputDisposition $output $probe.cancellation.terminationTrigger;if($record.partialOutputDisposition.disposition-eq'retained-after-cleanup-failure'){throw 'Partial-output cleanup failed.'};$record.status=if($probe.cancellation.forcedTermination){'forced-termination-evidence'}else{'not-exercised'}}catch{$record.status='failed';$record.error=$_.Exception.ToString()}}
    foreach($cancellationRecord in $state.cancellationProbes){$cancellationRecord.status=Get-FinalCancellationRecordStatus $cancellationRecord}
    $failed=@($state.attempts|Where-Object status -ne 'passed').Count+$(@($state.cancellationProbes|Where-Object status -ne 'forced-termination-evidence').Count);$state.status=if($failed-eq0){'completed'}else{'completed-with-failures'};$state.proofExecutionStatus=$state.status
} catch { $state.status='failed';$state.proofExecutionStatus='failed';$state.preflight.status=if($state.preflight.status-eq'not-run'){'failed'}else{$state.preflight.status};$state.preflight.error=$_.Exception.Message;$state.error=$_.Exception.ToString() }
finally {
    $state.completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
    if($stage -and(Test-Path $stage)){
        $preRetentionStatus=$state.status;$state.retention=[ordered]@{status=if($AppendRetention){'pending-append'}else{'not-requested'};error=$null}
        Write-JsonAtomic (Join-Path $stage 'g0.5-calibration-evidence.json') $state
        if($AppendRetention -and(Test-Path -LiteralPath $approvedArtifacts -PathType Container)){
            $groupStamp=([DateTimeOffset]::UtcNow).ToString('yyyyMMddTHHmmssfffZ');$groupSuffix=([Guid]::NewGuid().ToString('N').Substring(0,8)).ToUpperInvariant();$group="Gate0.G05.Calibration.$groupStamp.$groupSuffix";$destination="proofs/g05-calibration-$runSuffix"
            try {
                $state.retention=[ordered]@{status='pending-append';groupId=$group;destinationName=$destination;error=$null};Write-JsonAtomic (Join-Path $stage 'g0.5-calibration-evidence.json') $state
                & $retentionAppender -ArtifactRoot $approvedArtifacts -SourceRoot $stage -SourceTrustBoundary $trustedProjectParent -GroupId $group -DestinationName $destination -Provenance 'G0.5 bounded calibration evidence' -ProducerRuntimeIdentity @('repository:eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json') -LicenseRecords @('artifact:p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1/LICENSE.txt') -ProofRunIdentity @("artifact:$destination/g0.5-calibration-evidence.json");if($LASTEXITCODE-ne 0){throw 'Retention append failed.'}
                & $retentionValidator -ArtifactRoot $approvedArtifacts;if($LASTEXITCODE-ne 0){throw 'Post-append retained corpus validation failed.'}
                $state.retention=[ordered]@{status='retained-and-revalidated';groupId=$group;destinationName=$destination;error=$null};$state.status=$preRetentionStatus
            } catch { $state.retention=[ordered]@{status='failed';groupId=$group;destinationName=$destination;error=$_.Exception.ToString()};$state.status='retention-failed';$state.error=$_.Exception.ToString() }
            Write-JsonAtomic (Join-Path $stage 'g0.5-calibration-evidence.json') $state
        } elseif($AppendRetention) { $state.retention=[ordered]@{status='not-attempted-artifact-root-unusable';error='Approved retained artifact root was unavailable; staging remains the closed failure record.'};$state.status='retention-failed';Write-JsonAtomic (Join-Path $stage 'g0.5-calibration-evidence.json') $state
        }
    }
    $state|ConvertTo-Json -Depth 100;if($state.status-notin @('completed','contract-only')){exit 1}
}
