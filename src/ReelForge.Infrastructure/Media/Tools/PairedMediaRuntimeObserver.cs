using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class PairedMediaRuntimeObserver : IMediaRuntimeObserver
{
    private static readonly IReadOnlyDictionary<MediaRuntimeComponentKind, string[]> ComponentCommands =
        new Dictionary<MediaRuntimeComponentKind, string[]>
        {
            [MediaRuntimeComponentKind.Encoder] = ["-hide_banner", "-encoders"],
            [MediaRuntimeComponentKind.Decoder] = ["-hide_banner", "-decoders"],
            [MediaRuntimeComponentKind.Muxer] = ["-hide_banner", "-muxers"],
            [MediaRuntimeComponentKind.Demuxer] = ["-hide_banner", "-demuxers"],
            [MediaRuntimeComponentKind.Filter] = ["-hide_banner", "-filters"],
            [MediaRuntimeComponentKind.Protocol] = ["-hide_banner", "-protocols"]
        };

    private const string InspectionProgramVersionContractId = "ProgramVersion.Json.1";

    private readonly IExternalProcessRunner _runner;

    public PairedMediaRuntimeObserver(IExternalProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<ObservedMediaRuntime> ObserveAsync(
        MediaRuntimePair runtime,
        CancellationToken cancellationToken = default)
    {
        ValidateRuntimePaths(runtime);

        var primaryVersion = await RunRequiredAsync(runtime.PrimaryToolPath, ["-version"], cancellationToken);
        var inspectionVersion = await RunRequiredAsync(runtime.InspectionToolPath, ["-version"], cancellationToken);
        var inspectionProgramVersion = await RunRequiredAsync(
            runtime.InspectionToolPath,
            ["-v", "error", "-print_format", "json", "-show_program_version"],
            cancellationToken);

        var components = new Dictionary<MediaRuntimeComponentKind, IReadOnlyList<string>>();
        foreach (var (kind, arguments) in ComponentCommands)
        {
            var output = await RunRequiredAsync(runtime.PrimaryToolPath, arguments, cancellationToken);
            components[kind] = ParseComponents(kind, output);
        }

        var root = Path.GetFullPath(runtime.RuntimeRootPath);
        return new ObservedMediaRuntime(
            root,
            await ObserveToolAsync(runtime.PrimaryToolPath, primaryVersion, components, cancellationToken),
            await ObserveToolAsync(runtime.InspectionToolPath, inspectionVersion, EmptyComponents(), cancellationToken),
            ParseInspectionProgramVersion(inspectionProgramVersion),
            await HashRuntimeClosureAsync(root, cancellationToken));
    }

    private static async Task<MediaRuntimeToolObservation> ObserveToolAsync(
        string executablePath,
        string versionOutput,
        IReadOnlyDictionary<MediaRuntimeComponentKind, IReadOnlyList<string>> components,
        CancellationToken cancellationToken)
    {
        var report = ParseVersionReport(versionOutput);
        return new MediaRuntimeToolObservation(
            Path.GetFullPath(executablePath),
            await ComputeSha256Async(executablePath, cancellationToken),
            report.Version,
            report.Compiler,
            report.Configuration,
            report.LibraryVersions,
            components);
    }

    private async Task<string> RunRequiredAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            new ExternalProcessRequest(executablePath, arguments),
            cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            throw new ExternalProcessException(executablePath, result);
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError
            : result.StandardOutput;
    }

    private static void ValidateRuntimePaths(MediaRuntimePair runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtime.RuntimeRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtime.PrimaryToolPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtime.InspectionToolPath);

        var root = Path.GetFullPath(runtime.RuntimeRootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Media runtime root was not found: {root}");
        }

        foreach (var executable in new[] { runtime.PrimaryToolPath, runtime.InspectionToolPath })
        {
            var fullPath = Path.GetFullPath(executable);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Media runtime executable was not found.", fullPath);
            }

            var relativePath = Path.GetRelativePath(root, fullPath);
            if (Path.IsPathRooted(relativePath) || relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new ArgumentException("Media runtime executables must be contained by the runtime root.", nameof(runtime));
            }
        }
    }

    private static async Task<IReadOnlyList<MediaRuntimeFileObservation>> HashRuntimeClosureAsync(
        string runtimeRootPath,
        CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(runtimeRootPath, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        var observations = new List<MediaRuntimeFileObservation>();
        foreach (var path in files)
        {
            observations.Add(new MediaRuntimeFileObservation(
                NormalizeRelativePath(Path.GetRelativePath(runtimeRootPath, path)),
                await ComputeSha256Async(path, cancellationToken)));
        }

        return observations;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static RuntimeVersionReport ParseVersionReport(string output)
    {
        var normalizedLines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeWhitespace)
            .ToArray();

        var version = normalizedLines.FirstOrDefault(line => line.StartsWith("ffmpeg version ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("ffprobe version ", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        var compilerLine = normalizedLines.FirstOrDefault(line => line.StartsWith("built with ", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        var compiler = compilerLine.Length == 0 ? string.Empty : compilerLine["built with ".Length..];
        var configurationLine = normalizedLines.FirstOrDefault(line => line.StartsWith("configuration: ", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        var configuration = configurationLine.Length == 0 ? string.Empty : configurationLine["configuration: ".Length..];
        var libraries = normalizedLines
            .Where(line => line.StartsWith("lib", StringComparison.OrdinalIgnoreCase) && line.Contains(' '))
            .Select(line => line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].ToLowerInvariant(), parts => parts[1], StringComparer.Ordinal);

        return new RuntimeVersionReport(version, compiler, configuration, libraries);
    }

    private static MediaRuntimeParserContractObservation ParseInspectionProgramVersion(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.TryGetProperty("program_version", out var programVersion)
                && programVersion.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(version.GetString()))
            {
                return new MediaRuntimeParserContractObservation(
                    InspectionProgramVersionContractId,
                    true,
                    version.GetString()!);
            }
        }
        catch (JsonException)
        {
            // A successfully launched inspection tool can still violate its JSON parser contract.
        }

        return new MediaRuntimeParserContractObservation(
            InspectionProgramVersionContractId,
            false,
            string.Empty);
    }

    private static string[] ParseComponents(MediaRuntimeComponentKind kind, string output)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var inProtocolSection = kind != MediaRuntimeComponentKind.Protocol;

        foreach (var rawLine in output.Split(['\r', '\n']))
        {
            var line = rawLine.Trim();
            if (kind == MediaRuntimeComponentKind.Protocol)
            {
                if (line.EndsWith(':'))
                {
                    inProtocolSection = line.Equals("Input:", StringComparison.OrdinalIgnoreCase)
                        || line.Equals("Output:", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inProtocolSection || line.Length == 0 || line.Contains(' '))
                {
                    continue;
                }

                names.Add(line.ToLowerInvariant());
                continue;
            }

            var match = Regex.Match(line, "^[.A-Z|]{1,8}\\s+([^\\s]+)");
            if (match.Success)
            {
                names.Add(match.Groups[1].Value.ToLowerInvariant());
            }
        }

        return names.ToArray();
    }

    private static Dictionary<MediaRuntimeComponentKind, IReadOnlyList<string>> EmptyComponents() =>
        new Dictionary<MediaRuntimeComponentKind, IReadOnlyList<string>>();

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static string NormalizeWhitespace(string value) => Regex.Replace(value.Trim(), "\\s+", " ");

    private sealed record RuntimeVersionReport(
        string Version,
        string Compiler,
        string Configuration,
        IReadOnlyDictionary<string, string> LibraryVersions);
}
