using System.Diagnostics;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ReelForge.Gate0.WpfMeasurementAdapter;

// Reusable primitive only. It is deliberately not invoked by no-media-control.
internal sealed class ExactP2ProcessRunner : IDisposable
{
    private readonly JobObject _job = new();
    private Process? _process;
    private ExactP2ProcessSpec? _spec;
    private Task<string>? _stdoutDrain;
    private Task<string>? _stderrDrain;
    private CancellationTokenSource? _treeSamplingCancellation;
    private Task? _treeSampling;
    private readonly List<ProcessTreeSample> _treeSamples = [];
    private readonly object _treeSampleGate = new();
    private long? _uiStateDisplayedTicks;
    public ExactP2ProcessRunnerResult? Result { get; private set; }

    public Process Start(ExactP2ProcessSpec spec)
    {
        ValidateExactP2(spec);
        if (spec.Arguments.Contains("-nostdin", StringComparer.Ordinal)) throw new InvalidOperationException("-nostdin is prohibited for adapter jobs.");
        var startInfo = new ProcessStartInfo(spec.ExecutablePath) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = spec.WorkingDirectory, CreateNoWindow = true };
        foreach (var argument in spec.Arguments) startInfo.ArgumentList.Add(argument);
        _spec = spec;
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Exact P2 process did not start.");
        try
        {
            _job.Assign(_process.Handle); // Immediately after start, before a caller may accept media progress.
            _jobMembershipAssigned = true;
            _stdoutDrain = _process.StandardOutput.ReadToEndAsync();
            _stderrDrain = _process.StandardError.ReadToEndAsync();
            _treeSamplingCancellation = new CancellationTokenSource();
            _treeSampling = Task.Run(async () =>
            {
                while (!_treeSamplingCancellation.IsCancellationRequested)
                {
                    lock (_treeSampleGate) _treeSamples.AddRange(ProcessTree.Observe(_process.Id));
                    await Task.Delay(100, _treeSamplingCancellation.Token).ConfigureAwait(false);
                }
            });
        }
        catch
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.WaitForExit(2000);
            _job.Dispose();
            _process.Dispose();
            throw;
        }
        return _process;
    }

    public async Task<ExactP2ProcessRunnerResult> CancelAndCleanAsync(IEnumerable<string> partialOutputs, CancellationToken entryCancellationToken = default)
    {
        if (_process is null) throw new InvalidOperationException("No process is running.");
        entryCancellationToken.ThrowIfCancellationRequested(); // After this point cleanup cannot be abandoned by the caller.
        var handlerEntry = Stopwatch.GetTimestamp();
        var fields = new CancellationEvidence { CommandHandlerEntryTicks = handlerEntry, UiStateDisplayedTicks = _uiStateDisplayedTicks, RootPid = _process.Id, JobMembershipAssigned = _jobMembershipAssigned, ObservedProcessTreeAtRequest = ProcessTree.Observe(_process.Id) };
        ProcessStreamEvidence? streams = null;
        try
        {
            await _process.StandardInput.WriteLineAsync("q").ConfigureAwait(false); // q followed by newline
            await _process.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            fields.StdinWriteFlushedTicks = Stopwatch.GetTimestamp();
            var exitTask = _process.WaitForExitAsync(CancellationToken.None);
            var graceful = await Task.WhenAny(exitTask, Task.Delay(750, CancellationToken.None)).ConfigureAwait(false);
            if (graceful != exitTask)
            {
                fields.GraceExpiredTicks = Stopwatch.GetTimestamp();
                if (!_process.HasExited) { _process.Kill(entireProcessTree: true); fields.ForcedFallback = true; }
            }
            await exitTask.ConfigureAwait(false);
        }
        catch (Exception error) { fields.Failures.Add("graceful-cancellation-error:" + error.GetType().Name); }
        finally
        {
            try
            {
                if (!_process.HasExited) { _process.Kill(entireProcessTree: true); fields.ForcedFallback = true; }
                _job.Terminate(1);
                await WaitForJobZeroAsync(_job).ConfigureAwait(false);
                if (!_process.HasExited) _process.WaitForExit(2000);
                fields.ProcessTreeExitedTicks = Stopwatch.GetTimestamp();
            }
            catch (Exception error) { fields.Failures.Add("forced-closure-error:" + error.GetType().Name); }
            try { _treeSamplingCancellation!.Cancel(); if (_treeSampling is not null) await _treeSampling.ConfigureAwait(false); } catch (OperationCanceledException) { }
            catch (Exception error) { fields.Failures.Add("tree-sampling-error:" + error.GetType().Name); }
            try { streams = RetainStreams(await _stdoutDrain!.ConfigureAwait(false), await _stderrDrain!.ConfigureAwait(false)); } catch (Exception error) { fields.Failures.Add("stream-retention-error:" + error.GetType().Name); }
            lock (_treeSampleGate) fields.ProcessTreeSamples = _treeSamples.ToArray();
            fields.ObservedProcessTreeAtClose = ProcessTree.Observe(_process.Id);
            fields.ActiveProcessCountAtClose = _job.ActiveProcessCount;
            fields.OrphanPids = ProcessTree.FindStillRunning(fields.ProcessTreeSamples.Select(item => item.ProcessId).Append(_process.Id), _process.Id);
        }
        var cleanup = CleanupPartialOutputs(partialOutputs);
        fields.CleanupClosedTicks = Stopwatch.GetTimestamp();
        fields.FinalState = fields.ActiveProcessCountAtClose == 0 && fields.OrphanPids.Count == 0 && cleanup.Succeeded && fields.Failures.Count == 0 ? "Cancelled" : "CancellationCleanupFailed";
        fields.UiAcknowledgementMilliseconds = fields.UiStateDisplayedTicks is null ? null : Milliseconds(fields.UiStateDisplayedTicks.Value - handlerEntry);
        fields.TotalProcessTreeExitMilliseconds = Milliseconds(fields.ProcessTreeExitedTicks - handlerEntry);
        fields.Accepted = fields.UiAcknowledgementMilliseconds <= 100 && fields.TotalProcessTreeExitMilliseconds <= 2000 && fields.ActiveProcessCountAtClose == 0 && fields.OrphanPids.Count == 0 && cleanup.Succeeded && fields.FinalState == "Cancelled";
        Result = new ExactP2ProcessRunnerResult(_process.ExitCode, fields, cleanup, streams);
        return Result;
    }

    // The WPF command handler records this only after its next dispatcher turn observes CancellationRequested.
    public void MarkUiAcknowledged() => _uiStateDisplayedTicks = Stopwatch.GetTimestamp();
    private bool _jobMembershipAssigned;

    private ProcessStreamEvidence RetainStreams(string stdout, string stderr)
    {
        var spec = _spec ?? throw new InvalidOperationException("Scenario specification was not retained.");
        var stdoutPath = ContainedPath.RequireFile(spec.ScenarioRoot, spec.StdoutPath);
        var stderrPath = ContainedPath.RequireFile(spec.ScenarioRoot, spec.StderrPath);
        File.WriteAllText(stdoutPath, stdout);
        File.WriteAllText(stderrPath, stderr);
        return new ProcessStreamEvidence(FileEvidence(stdoutPath), FileEvidence(stderrPath));
    }
    private static FileEvidence FileEvidence(string path) => new(Path.GetFileName(path), new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    private static double Milliseconds(long deltaTicks) => deltaTicks * 1000.0 / Stopwatch.Frequency;
    private static async Task WaitForJobZeroAsync(JobObject job)
    {
        for (var attempt = 0; attempt < 80 && job.ActiveProcessCount != 0; attempt++) await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
    }

    private PartialCleanupEvidence CleanupPartialOutputs(IEnumerable<string> paths)
    {
        var records = new List<object>(); var failed = false;
        var spec = _spec ?? throw new InvalidOperationException("Scenario specification was not retained.");
        foreach (var path in paths)
        {
            try
            {
                var contained = ContainedPath.RequireFile(spec.ScenarioRoot, path);
                var existed = File.Exists(contained);
                if (existed) File.Delete(contained);
                records.Add(new { path = Path.GetFileName(contained), existed, removed = !File.Exists(contained) });
            }
            catch (Exception error) { failed = true; records.Add(new { path = Path.GetFileName(path), error = error.Message }); }
        }
        return new PartialCleanupEvidence(!failed, records);
    }
    private static void ValidateExactP2(ExactP2ProcessSpec spec)
    {
        var root = Path.GetFullPath(spec.ApprovedP2Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var executable = Path.GetFullPath(spec.ExecutablePath);
        if (!executable.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("P2 executable must be beneath the explicit approved root; PATH discovery is prohibited.");
        if (!File.Exists(executable)) throw new FileNotFoundException("Exact P2 executable was not found.", executable);
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable)));
        if (!actual.Equals(spec.ExecutableSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Exact P2 executable SHA-256 mismatch.");
        ContainedPath.RequireDirectory(spec.ScenarioRoot, spec.WorkingDirectory);
        ContainedPath.RequireFile(spec.ScenarioRoot, spec.StdoutPath);
        ContainedPath.RequireFile(spec.ScenarioRoot, spec.StderrPath);
    }
    public void Dispose() { _process?.Dispose(); _job.Dispose(); }
}

internal sealed record ExactP2ProcessSpec(string ApprovedP2Root, string ExecutablePath, string ExecutableSha256, string ScenarioRoot, string WorkingDirectory, string StdoutPath, string StderrPath, IReadOnlyList<string> Arguments);
internal sealed record ExactP2ProcessRunnerResult(int ExitCode, CancellationEvidence Cancellation, PartialCleanupEvidence PartialCleanup, ProcessStreamEvidence? Streams);
internal sealed record FileEvidence(string RelativeName, long Size, string Sha256);
internal sealed record ProcessStreamEvidence(FileEvidence Stdout, FileEvidence Stderr);
internal sealed class CancellationEvidence
{
    public long CommandHandlerEntryTicks { get; init; }
    public long? UiStateDisplayedTicks { get; set; }
    public long StdinWriteFlushedTicks { get; set; }
    public long? GraceExpiredTicks { get; set; }
    public long ProcessTreeExitedTicks { get; set; }
    public long CleanupClosedTicks { get; set; }
    public int RootPid { get; init; }
    public bool ForcedFallback { get; set; }
    public bool JobMembershipAssigned { get; init; }
    public int ActiveProcessCountAtClose { get; set; }
    public IReadOnlyList<ProcessTreeSample> ObservedProcessTreeAtRequest { get; init; } = [];
    public IReadOnlyList<ProcessTreeSample> ObservedProcessTreeAtClose { get; set; } = [];
    public IReadOnlyList<int> OrphanPids { get; set; } = [];
    public IReadOnlyList<ProcessTreeSample> ProcessTreeSamples { get; set; } = [];
    public double? UiAcknowledgementMilliseconds { get; set; }
    public double TotalProcessTreeExitMilliseconds { get; set; }
    public string FinalState { get; set; } = "CancellationRequested";
    public bool Accepted { get; set; }
    public List<string> Failures { get; } = [];
}
internal sealed record PartialCleanupEvidence(bool Succeeded, IReadOnlyList<object> Records);

internal static class ContainedPath
{
    internal static string RequireDirectory(string scenarioRoot, string candidate) => Require(scenarioRoot, candidate, true);
    internal static string RequireFile(string scenarioRoot, string candidate) => Require(scenarioRoot, candidate, false);
    private static string Require(string scenarioRoot, string candidate, bool allowRoot)
    {
        var normalizedRoot = Path.GetFullPath(scenarioRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(normalizedRoot) || new DirectoryInfo(normalizedRoot).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Scenario root must exist and may not be a reparse point.");
        var root = normalizedRoot + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate);
        if (allowRoot && full.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)) return full;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Working, log, and partial paths must remain beneath the contained scenario root.");
        var current = new FileInfo(full).Directory;
        while (current is not null && current.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Scenario root reparse escape is prohibited.");
            current = current.Parent;
        }
        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Scenario file reparse escape is prohibited.");
        return full;
    }
}

internal sealed class JobObject : IDisposable
{
    private readonly SafeFileHandle _handle;
    public JobObject()
    {
        _handle = NativeJobs.CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var info = new NativeJobs.JobObjectExtendedLimitInformation { BasicLimitInformation = new NativeJobs.JobObjectBasicLimitInformation { LimitFlags = NativeJobs.JobObjectLimitKillOnJobClose } };
        if (!NativeJobs.SetInformationJobObject(_handle, 9, ref info, (uint)Marshal.SizeOf<NativeJobs.JobObjectExtendedLimitInformation>())) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Assign(IntPtr processHandle) { if (!NativeJobs.AssignProcessToJobObject(_handle, processHandle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    public void Terminate(uint exitCode) { if (!NativeJobs.TerminateJobObject(_handle, exitCode)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    public int ActiveProcessCount { get { NativeJobs.JobObjectBasicAndIoAccountingInformation info = default; return NativeJobs.QueryInformationJobObject(_handle, 8, ref info, (uint)Marshal.SizeOf<NativeJobs.JobObjectBasicAndIoAccountingInformation>(), out _) ? (int)info.BasicInfo.ActiveProcesses : -1; } }
    public void Dispose() => _handle.Dispose();
}

internal static class NativeJobs
{
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobObjectExtendedLimitInformation info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryInformationJobObject(SafeFileHandle job, int infoClass, ref JobObjectBasicAndIoAccountingInformation info, uint length, out uint returned);
    [StructLayout(LayoutKind.Sequential)] internal struct JobObjectExtendedLimitInformation { public JobObjectBasicLimitInformation BasicLimitInformation; public IoCounters IoInfo; public nuint ProcessMemoryLimit; public nuint JobMemoryLimit; public nuint PeakProcessMemoryUsed; public nuint PeakJobMemoryUsed; }
    [StructLayout(LayoutKind.Sequential)] internal struct JobObjectBasicLimitInformation { public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags; public nuint MinimumWorkingSetSize; public nuint MaximumWorkingSetSize; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass; public uint SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount; public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] internal struct JobObjectBasicAndIoAccountingInformation { public JobObjectBasicAccountingInformation BasicInfo; public IoCounters IoInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct JobObjectBasicAccountingInformation { public long TotalUserTime; public long TotalKernelTime; public long ThisPeriodTotalUserTime; public long ThisPeriodTotalKernelTime; public uint TotalPageFaultCount; public uint TotalProcesses; public uint ActiveProcesses; public uint TotalTerminatedProcesses; }
}

internal readonly record struct ProcessTreeSample(int ProcessId, int ParentProcessId, long TimestampTicks);
internal static class ProcessTree
{
    public static IReadOnlyList<int> FindStillRunning(IEnumerable<int> processIds, int rootPid)
    {
        var alive = new List<int>();
        foreach (var processId in processIds.Where(id => id != rootPid).Distinct())
        {
            try { using var process = Process.GetProcessById(processId); if (!process.HasExited) alive.Add(processId); }
            catch (ArgumentException) { }
        }
        return alive;
    }
    public static IReadOnlyList<ProcessTreeSample> Observe(int rootPid)
    {
        // Toolhelp captures observed descendant/parent identities without spawning a PATH-discovered helper.
        var snapshot = NativeProcessTree.CreateToolhelp32Snapshot(NativeProcessTree.Th32csSnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return [];
        try
        {
            var entries = new Dictionary<int, ProcessTreeSample>();
            var entry = NativeProcessTree.ProcessEntry32.Create();
            if (NativeProcessTree.Process32First(snapshot, ref entry))
            {
                do
                {
                    entries[(int)entry.ProcessId] = new ProcessTreeSample((int)entry.ProcessId, (int)entry.ParentProcessId, Stopwatch.GetTimestamp());
                    entry = NativeProcessTree.ProcessEntry32.Create();
                } while (NativeProcessTree.Process32Next(snapshot, ref entry));
            }
            if (!entries.ContainsKey(rootPid)) return [];
            var observed = new List<ProcessTreeSample>();
            var pending = new Queue<int>(); pending.Enqueue(rootPid);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (!entries.TryGetValue(current, out var process) || observed.Any(item => item.ProcessId == current)) continue;
                observed.Add(process);
                foreach (var child in entries.Values.Where(item => item.ParentProcessId == current)) pending.Enqueue(child.ProcessId);
            }
            return observed;
        }
        finally { NativeProcessTree.CloseHandle(snapshot); }
    }
}

internal static class NativeProcessTree
{
    internal const uint Th32csSnapProcess = 0x00000002;
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ProcessEntry32
    {
        internal uint Size; internal uint Usage; internal uint ProcessId; internal IntPtr DefaultHeapId; internal uint ModuleId; internal uint Threads; internal uint ParentProcessId; internal int BasePriority; internal uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ExecutableFile;
        internal static ProcessEntry32 Create() => new() { Size = (uint)Marshal.SizeOf<ProcessEntry32>(), ExecutableFile = string.Empty };
    }
}
