using System.Text.Json;
using System.Xml.Linq;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05WpfMeasurementAdapterTests
{
    [Fact]
    public void AdapterRemainsAnIsolatedWindowsWpfWinExe()
    {
        var project = XDocument.Load(PathInRepo("eng", "gate0", "ReelForge.Gate0.WpfMeasurementAdapter", "ReelForge.Gate0.WpfMeasurementAdapter.csproj"));
        var properties = project.Root!.Element("PropertyGroup")!;
        Assert.Equal("net8.0-windows", properties.Element("TargetFramework")!.Value);
        Assert.Equal("true", properties.Element("UseWPF")!.Value);
        Assert.Equal("WinExe", properties.Element("OutputType")!.Value);
        Assert.Empty(project.Descendants("ProjectReference"));

        var solution = File.ReadAllText(PathInRepo("ReelForge.sln"));
        Assert.Contains("ReelForge.Gate0.WpfMeasurementAdapter", solution);
        Assert.Contains("eng\\gate0\\ReelForge.Gate0.WpfMeasurementAdapter", solution);
    }

    [Fact]
    public void NoMediaControlHasFrozenVisibleWindowAndHeartbeatBoundaries()
    {
        var source = Read("Program.cs");
        foreach (var required in new[]
        {
            "[STAThread]", "no-media-control", "duration != 30_000", "Title = \"ReelForge Gate 0 WPF Measurement Adapter\"",
            "WindowState = WindowState.Normal", "MinWidth = 640", "MinHeight = 360", "mediaChildCount = 0",
            "PeriodicTimer", "CadenceMilliseconds = 16", "DispatcherPriority.Normal", "CompareExchange(ref _outstanding, 1, 0)",
            "classification = \"missed\"", "classification = \"executed\"", "_expected != _executed + _missed",
            "minimum-executed-callbacks-not-met", "p95 > 50 || p99 > 100 || maximum > 250", "dispatcher-probes.ndjson",
            "process-samples.ndjson", "evidence.json", "automaticLaunchPermitted = false", "manual-privacy-sensitive-only"
            , "typeof(Program).Assembly.Location", "C13AA5236AD025415807F843D5861E707CB9DD82BC386CC79FF2AB580B954836",
            "Closing += OnClosing", "StateChanged", "IsVisibleChanged", "FailInfrastructure", "required-host-evidence-unavailable",
            "DispatcherPriority.ApplicationIdle"
        }) Assert.Contains(required, source);
        Assert.DoesNotContain("Process.Start(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactP2RunnerProhibitsPathDiscoveryAndRecordsCancellationClosure()
    {
        var source = Read("ExactP2ProcessRunner.cs");
        foreach (var required in new[]
        {
            "ApprovedP2Root", "SHA256.HashData", "PATH discovery is prohibited", "-nostdin is prohibited",
            "RedirectStandardInput = true", "RedirectStandardOutput = true", "RedirectStandardError = true",
            "Assign(_process.Handle)", "JobObjectLimitKillOnJobClose", "q followed by newline", "FlushAsync",
            "Task.Delay(750", "Kill(entireProcessTree: true)", "ActiveProcessCountAtClose", "ObservedProcessTreeAtRequest",
            "ObservedProcessTreeAtClose", "OrphanPids", "CleanupPartialOutputs", "CleanupClosedTicks", "MarkUiAcknowledged",
            "ReadToEndAsync", "RetainStreams", "ProcessStreamEvidence", "Task.Delay(750, CancellationToken.None)",
            "entryCancellationToken.ThrowIfCancellationRequested", "FinalState = fields.ActiveProcessCountAtClose == 0", "fields.Accepted =",
            "ScenarioRoot", "ContainedPath.Require", "ProcessTreeSamples", "FindStillRunning", "catch", "_process.Kill(entireProcessTree: true)",
            "_job.Dispose()", "finally", "_job.Terminate(1)", "WaitForJobZeroAsync", "RequireDirectory", "RequireFile", "reparse", "existed, removed",
            "fields.ProcessTreeExitedTicks = Stopwatch.GetTimestamp();"
        }) Assert.Contains(required, source);
        Assert.DoesNotContain("Environment.GetEnvironmentVariable(\"PATH\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfAdapterDoesNotReferenceProductAssembliesOrImplementMediaExecution()
    {
        var root = PathInRepo("eng", "gate0", "ReelForge.Gate0.WpfMeasurementAdapter");
        var text = string.Join(Environment.NewLine, Directory.EnumerateFiles(root, "*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("ReelForge.Core", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ReelForge.Application", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ReelForge.Infrastructure", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaFoundation", text, StringComparison.Ordinal);
        Assert.Contains("not invoked by no-media-control", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HostAndRuntimeContractEvidenceHaveClosedRequiredFields()
    {
        var host = Read("HostEvidence.cs");
        foreach (var required in new[]
        {
            "RequiredFieldsAvailable", "DeviceRegistryIdentity", "DriverVersion", "DriverDate", "DriverProvider",
            "MonitorBounds", "MonitorWorkArea", "Primary", "powercfg.exe", "PowerEvidence", "EnumDisplayMonitors", "MonitorInfoEx", "DeviceRegistryIdentity"
        }) Assert.Contains(required, host);
        Assert.DoesNotContain("MonitorFromPoint", host, StringComparison.Ordinal);
        var project = File.ReadAllText(PathInRepo("eng", "gate0", "ReelForge.Gate0.WpfMeasurementAdapter", "ReelForge.Gate0.WpfMeasurementAdapter.csproj"));
        Assert.Contains("g0.5-wpf-measurement-adapter-contract.json", project);
        Assert.Contains("CopyToOutputDirectory", project);
    }

    [Fact]
    public void AuthoritativeNoMediaResultIsClosedAndSourceBound()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-wpf-no-media-result-summary.json")));
        var root = document.RootElement;
        Assert.Equal("passed", root.GetProperty("status").GetString());
        Assert.Equal("f4d39ac", root.GetProperty("execution").GetProperty("repositoryCommit").GetString());
        Assert.Equal("237C38B7EA8A99DC3BE865F4EB25BE0D6BE1C24CCEB1CA45D6236C7DD549000D",
            root.GetProperty("retention").GetProperty("artifacts")[1].GetProperty("sha256").GetString());

        var window = root.GetProperty("windowAndMedia");
        Assert.Equal(0, window.GetProperty("mediaChildCount").GetInt32());
        Assert.False(window.GetProperty("mediaProcessStarted").GetBoolean());

        var dispatcher = root.GetProperty("dispatcher");
        Assert.Equal(dispatcher.GetProperty("expected").GetInt32(),
            dispatcher.GetProperty("executed").GetInt32() + dispatcher.GetProperty("missed").GetInt32());
        Assert.True(dispatcher.GetProperty("allExpectedCadencesClassified").GetBoolean());
        Assert.True(root.GetProperty("host").GetProperty("requiredFieldsAvailable").GetBoolean());

        var disposition = root.GetProperty("disposition");
        Assert.True(disposition.GetProperty("noMediaControlComplete").GetBoolean());
        Assert.False(disposition.GetProperty("wpfMediaScenarioComplete").GetBoolean());
        Assert.True(disposition.GetProperty("preMatrixSmokeAuthorized").GetBoolean());
        Assert.Empty(root.GetProperty("remainingBeforePreMatrixSmoke").EnumerateArray());
    }

    private static string Read(string filename) => File.ReadAllText(PathInRepo("eng", "gate0", "ReelForge.Gate0.WpfMeasurementAdapter", filename));
    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
