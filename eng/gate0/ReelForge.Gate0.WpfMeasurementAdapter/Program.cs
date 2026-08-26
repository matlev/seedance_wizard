using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ReelForge.Gate0.WpfMeasurementAdapter;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = AdapterOptions.Parse(args);
            if (options.Mode != AdapterMode.NoMediaControl)
                throw new ArgumentException("Only the bounded no-media-control mode is implemented. Media execution remains manual-opt-in and unavailable in this deliverable.");

            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new MeasurementWindow(options);
            application.Run(window);
            return window.ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 2;
        }
    }
}

internal enum AdapterMode { NoMediaControl }

internal sealed record AdapterOptions(AdapterMode Mode, string StagingRoot, string RunId, int DurationMilliseconds)
{
    public static AdapterOptions Parse(IReadOnlyList<string> args)
    {
        string? stagingRoot = null;
        string? runId = null;
        var mode = AdapterMode.NoMediaControl;
        var duration = 30_000;
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--mode":
                    mode = args[++index] == "no-media-control" ? AdapterMode.NoMediaControl : throw new ArgumentException("Unsupported mode.");
                    break;
                case "--staging-root": stagingRoot = args[++index]; break;
                case "--run-id": runId = args[++index]; break;
                case "--duration-ms":
                    duration = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default: throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }
        if (string.IsNullOrWhiteSpace(stagingRoot) || !Path.IsPathFullyQualified(stagingRoot) || !Directory.Exists(stagingRoot))
            throw new ArgumentException("--staging-root must name an existing absolute, caller-supplied approved Gate 0 staging root.");
        if (string.IsNullOrWhiteSpace(runId) || runId.Any(Path.GetInvalidFileNameChars().Contains))
            throw new ArgumentException("--run-id is required and must be a simple filename-safe identifier.");
        if (duration != 30_000)
            throw new ArgumentException("The no-media-control duration is frozen at 30000 milliseconds.");
        return new AdapterOptions(mode, stagingRoot!, runId!, duration);
    }
}

internal sealed class MeasurementWindow : Window
{
    private readonly AdapterOptions _options;
    private readonly TextBlock _status = new() { Margin = new Thickness(18), FontSize = 18 };
    private int _exitCode = 1;
    private AdapterEvidence? _run;
    public int ExitCode => _exitCode;

    public MeasurementWindow(AdapterOptions options)
    {
        _options = options;
        Title = "ReelForge Gate 0 WPF Measurement Adapter";
        Width = 640;
        Height = 360;
        MinWidth = 640;
        MinHeight = 360;
        WindowState = WindowState.Normal;
        Content = _status;
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += (_, _) => _run?.ReportWindowLifecycle("state-changed");
        IsVisibleChanged += (_, _) => _run?.ReportWindowLifecycle("visibility-changed");
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            _status.Text = "No-media control running. Keep this window visible and unminimized.";
            _run = AdapterEvidence.Create(_options, this);
            await _run.ExecuteNoMediaControlAsync();
            _exitCode = _run.Passed ? 0 : 1;
            _status.Text = _run.Passed ? "No-media control passed. Evidence written; closing." : "No-media control failed. Evidence written; closing.";
        }
        catch (Exception exception)
        {
            _run?.FailInfrastructure("unhandled-no-media-control-exception", exception);
            _status.Text = "No-media control infrastructure failure. Closed evidence written.";
            Console.Error.WriteLine(exception);
            _exitCode = 2;
        }
        finally
        {
            await Task.Delay(700);
            Close();
        }
    }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        if (_run?.IsRunning == true)
        {
            eventArgs.Cancel = true;
            _run.FailImmediately("window-close-attempted-during-measurement");
        }
    }
}

internal sealed class AdapterEvidence
{
    private const int CadenceMilliseconds = 16;
    private readonly AdapterOptions _options;
    private readonly Window _window;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _writeGate = new();
    private readonly List<double> _latencies = [];
    private readonly List<string> _failures = [];
    private readonly string _scenarioDirectory;
    private readonly string _probePath;
    private readonly string _processPath;
    private int _outstanding;
    private long _expected;
    private long _executed;
    private long _missed;
    private long _overdue;
    private bool _visibilityValid = true;
    private bool _running;
    private bool _finalized;
    private DateTimeOffset _startedUtc;
    private HostCapture? _host;

    private AdapterEvidence(AdapterOptions options, Window window, string scenarioDirectory)
    {
        _options = options;
        _window = window;
        _scenarioDirectory = scenarioDirectory;
        _probePath = Path.Combine(scenarioDirectory, "dispatcher-probes.ndjson");
        _processPath = Path.Combine(scenarioDirectory, "process-samples.ndjson");
    }

    public bool Passed { get; private set; }
    public bool IsRunning => _running;

    public static AdapterEvidence Create(AdapterOptions options, Window window)
    {
        // New direct child only: the caller retains ownership of the staging root.
        var childName = $"wpf-no-media-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var scenarioDirectory = Path.Combine(options.StagingRoot, childName);
        Directory.CreateDirectory(scenarioDirectory);
        return new AdapterEvidence(options, window, scenarioDirectory);
    }

    public async Task ExecuteNoMediaControlAsync()
    {
        if (_window.WindowState != WindowState.Normal || !_window.IsVisible || !NativeWindow.IsWindowVisible(new WindowInteropHelper(_window).Handle))
            throw new InvalidOperationException("The no-media control requires a real visible, unminimized Window before measurement starts.");

        _startedUtc = DateTimeOffset.UtcNow;
        _running = true;
        _host = HostEvidence.Capture(_window, new WindowInteropHelper(_window).Handle);
        if (!_host.RequiredFieldsAvailable) _failures.Add("required-host-evidence-unavailable");
        var cancellation = new CancellationTokenSource(_options.DurationMilliseconds);
        RecordVisibility("initial");
        using var periodic = new PeriodicTimer(TimeSpan.FromMilliseconds(CadenceMilliseconds));
        var heartbeat = Task.Run(async () =>
        {
            while (await periodic.WaitForNextTickAsync(cancellation.Token).ConfigureAwait(false)) ScheduleHeartbeat();
        });
        var visibility = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => RecordVisibility("periodic"), _window.Dispatcher);
        visibility.Start();
        try { await Task.Delay(_options.DurationMilliseconds, CancellationToken.None); }
        finally
        {
            cancellation.Cancel();
            visibility.Stop();
            try { await heartbeat.ConfigureAwait(true); } catch (OperationCanceledException) { }
            // Close the interval only after every already-enqueued Normal-priority probe.
            await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            RecordVisibility("final");
            _running = false;
            WriteClosedSummary(_startedUtc, DateTimeOffset.UtcNow, "failed");
        }
    }

    public void ReportWindowLifecycle(string reason)
    {
        if (!_running) return;
        RecordVisibility(reason);
        if (_window.WindowState != WindowState.Normal || !_window.IsVisible) FailImmediately("window-lifecycle-invalid:" + reason);
    }
    public void FailImmediately(string failure)
    {
        _failures.Add(failure);
        _visibilityValid = false;
    }
    public void FailInfrastructure(string failure, Exception exception)
    {
        _failures.Add(failure + ":" + exception.GetType().Name);
        _running = false;
        _host ??= HostEvidence.Fallback(failure);
        WriteClosedSummary(_startedUtc == default ? DateTimeOffset.UtcNow : _startedUtc, DateTimeOffset.UtcNow, "infrastructure-failed");
    }

    private void ScheduleHeartbeat()
    {
        var expected = Interlocked.Increment(ref _expected);
        var enqueued = _clock.ElapsedTicks;
        if (Interlocked.CompareExchange(ref _outstanding, 1, 0) != 0)
        {
            Interlocked.Increment(ref _missed);
            WriteNdjson(_probePath, new { kind = "dispatcher", classification = "missed", expected, enqueueMonotonicTicks = enqueued, reason = "preceding-callback-outstanding" });
            return;
        }
        _ = _window.Dispatcher.InvokeAsync(() =>
        {
            var executed = _clock.ElapsedTicks;
            var latency = (executed - enqueued) * 1000.0 / Stopwatch.Frequency;
            lock (_latencies) _latencies.Add(latency);
            Interlocked.Increment(ref _executed);
            if (latency > CadenceMilliseconds) Interlocked.Increment(ref _overdue);
            WriteNdjson(_probePath, new { kind = "dispatcher", classification = "executed", expected, enqueueMonotonicTicks = enqueued, executionMonotonicTicks = executed, latencyMilliseconds = latency, overdue = latency > CadenceMilliseconds });
            Volatile.Write(ref _outstanding, 0);
        }, DispatcherPriority.Normal);
    }

    private void RecordVisibility(string phase)
    {
        var handle = new WindowInteropHelper(_window).Handle;
        var nativeVisible = handle != IntPtr.Zero && NativeWindow.IsWindowVisible(handle);
        var valid = _window.IsVisible && _window.WindowState == WindowState.Normal && nativeVisible && NativeWindow.IsInteractiveDesktop();
        _visibilityValid &= valid;
        if (!valid) _failures.Add($"window-visibility-invalid:{phase}");
        WriteNdjson(_processPath, new
        {
            kind = "window-visibility", phase, monotonicTicks = _clock.ElapsedTicks,
            wpfIsVisible = _window.IsVisible, windowState = _window.WindowState.ToString(), nativeVisible,
            interactiveDesktop = NativeWindow.IsInteractiveDesktop(), hwnd = handle.ToInt64(), mediaChildCount = 0
        });
    }

    private void WriteClosedSummary(DateTimeOffset started, DateTimeOffset ended, string requestedStatus)
    {
        if (_finalized) return;
        _finalized = true;
        double[] latencies;
        lock (_latencies) latencies = _latencies.Order().ToArray();
        var p95 = Percentile(latencies, 0.95);
        var p99 = Percentile(latencies, 0.99);
        var maximum = latencies.Length == 0 ? double.PositiveInfinity : latencies[^1];
        if (_executed < 1800) _failures.Add("minimum-executed-callbacks-not-met");
        if (!_visibilityValid) _failures.Add("visible-unminimized-window-gate-failed");
        if (p95 > 50 || p99 > 100 || maximum > 250) _failures.Add("dispatcher-latency-threshold-failed");
        if (_expected != _executed + _missed) _failures.Add("silent-cadence-accounting-loss");
        Passed = _failures.Count == 0 && requestedStatus != "infrastructure-failed";
        var handle = new WindowInteropHelper(_window).Handle;
        var summary = new
        {
            schemaVersion = 1,
            schemaId = "Gate0.G05.WpfMeasurementAdapterEvidence.V1",
            boundaryId = "p2-windows-wpf-measurement-adapter",
            mode = "no-media-control",
            status = requestedStatus == "infrastructure-failed" ? requestedStatus : Passed ? "passed" : "failed",
            run = new { runId = _options.RunId, startedUtc = started, completedUtc = ended, adapterSha256 = HashOfCurrentAssembly(), contractSha256 = ContractHash(), processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), interactiveUser = NativeWindow.IsInteractiveDesktop() },
            staging = new { directChildName = Path.GetFileName(_scenarioDirectory), absolutePathExcludedFromTrackedSummary = true },
            media = new { childCount = 0, processStarted = false },
            host = _host ?? HostEvidence.Fallback("host-evidence-was-not-captured"),
            dispatcher = new { cadenceMilliseconds = CadenceMilliseconds, priority = "Normal", maximumOutstandingCallbacks = 1, expected = _expected, executed = _executed, missed = _missed, overdue = _overdue, p95Milliseconds = p95, p99Milliseconds = p99, maximumMilliseconds = maximum, probeEvidence = Path.GetFileName(_probePath), everyExpectedCadenceClassified = _expected == _executed + _missed },
            processEvidence = new { samples = Path.GetFileName(_processPath), exactP2ProcessStarted = false, activeProcessCountAtClose = 0 },
            wpr = new { automaticLaunchPermitted = false, installationPermitted = false, captured = false, disposition = "manual-privacy-sensitive-only" },
            failures = _failures.ToArray(),
            nonClaims = new[] { "No current ReelForge product rendering, preview, cache, cancellation, project, or UI behavior claim.", "No media child was started by no-media-control." }
        };
        try { File.WriteAllText(Path.Combine(_scenarioDirectory, "evidence.json"), JsonSerializer.Serialize(summary, JsonOptions)); }
        catch (Exception writeError) { Console.Error.WriteLine("Closed adapter evidence write failed: " + writeError); }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private void WriteNdjson(string path, object record)
    {
        lock (_writeGate) File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine);
    }
    private static double Percentile(double[] sorted, double proportion) => sorted.Length == 0 ? double.PositiveInfinity : sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * proportion) - 1, 0, sorted.Length - 1)];
    private static string HashOfCurrentAssembly() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location)));
    private static string ContractHash()
    {
        const string expected = "C13AA5236AD025415807F843D5861E707CB9DD82BC386CC79FF2AB580B954836";
        var path = Path.Combine(AppContext.BaseDirectory, "g0.5-wpf-measurement-adapter-contract.json");
        if (!File.Exists(path)) throw new InvalidOperationException("Frozen adapter contract copy is missing from the adapter runtime.");
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (!actual.Equals(expected, StringComparison.Ordinal)) throw new InvalidOperationException("Frozen adapter contract SHA-256 mismatch.");
        return actual;
    }
}
