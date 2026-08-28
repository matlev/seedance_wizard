using System.Text.RegularExpressions;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class MediaRuntimeProfileValidator : IMediaRuntimeProfileValidator
{
    public MediaRuntimeProfileAssessment Validate(
        MediaRuntimeProfile profile,
        ObservedMediaRuntime observedRuntime)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(observedRuntime);

        var issues = new List<MediaRuntimeValidationIssue>();
        ValidateManifest(profile, issues);
        if (issues.Count > 0)
        {
            return new MediaRuntimeProfileAssessment(profile.ProfileId, issues);
        }

        ValidateTool("primary tool", profile.PrimaryTool, observedRuntime.PrimaryTool, observedRuntime.RuntimeRootPath, issues);
        ValidateTool("inspection tool", profile.InspectionTool, observedRuntime.InspectionTool, observedRuntime.RuntimeRootPath, issues);
        ValidateRuntimeClosure(profile.RuntimeFiles, observedRuntime.RuntimeFiles, issues);
        ValidateForbiddenFlags(profile.ForbiddenConfigurationFlags, observedRuntime, issues);
        ValidateRequiredComponents(profile.RequiredComponents, observedRuntime.PrimaryTool.Components, issues);
        ValidateParserContract(profile.InspectionParserContract, observedRuntime.InspectionParserContract, issues);

        return new MediaRuntimeProfileAssessment(profile.ProfileId, issues);
    }

    private static void ValidateManifest(MediaRuntimeProfile profile, List<MediaRuntimeValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            issues.Add(new MediaRuntimeValidationIssue("Manifest.ProfileId", "Profile ID is required."));
        }

        ValidateToolManifest("PrimaryTool", profile.PrimaryTool, issues);
        ValidateToolManifest("InspectionTool", profile.InspectionTool, issues);
        ValidateFileManifests(profile.RuntimeFiles, issues);
        ValidateToolClosureEntry("PrimaryTool", profile.PrimaryTool, profile.RuntimeFiles, issues);
        ValidateToolClosureEntry("InspectionTool", profile.InspectionTool, profile.RuntimeFiles, issues);

        if (profile.InspectionParserContract is null
            || string.IsNullOrWhiteSpace(profile.InspectionParserContract.ContractId)
            || string.IsNullOrWhiteSpace(profile.InspectionParserContract.ProgramVersion))
        {
            issues.Add(new MediaRuntimeValidationIssue("Manifest.ParserContract", "Inspection parser contract ID and program version are required."));
        }

        foreach (var requirement in profile.RequiredComponents)
        {
            if (requirement.Names.Count == 0 || requirement.Names.Any(string.IsNullOrWhiteSpace))
            {
                issues.Add(new MediaRuntimeValidationIssue("Manifest.Components", "Each component requirement must name at least one component."));
            }
        }

        if (profile.ForbiddenConfigurationFlags.Any(string.IsNullOrWhiteSpace))
        {
            issues.Add(new MediaRuntimeValidationIssue("Manifest.ForbiddenFlags", "Forbidden configuration flags cannot be blank."));
        }
    }

    private static void ValidateToolManifest(
        string name,
        MediaRuntimeToolManifest manifest,
        List<MediaRuntimeValidationIssue> issues)
    {
        if (manifest is null)
        {
            issues.Add(new MediaRuntimeValidationIssue($"Manifest.{name}", $"{name} manifest is required."));
            return;
        }

        if (!IsSafeRelativePath(manifest.RelativePath))
        {
            issues.Add(new MediaRuntimeValidationIssue($"Manifest.{name}.Path", $"{name} path must be a non-traversing relative path."));
        }

        if (!IsSha256(manifest.Sha256))
        {
            issues.Add(new MediaRuntimeValidationIssue($"Manifest.{name}.Hash", $"{name} hash must be a SHA-256 hexadecimal value."));
        }

        if (string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.Compiler)
            || string.IsNullOrWhiteSpace(manifest.Configuration)
            || manifest.LibraryVersions.Count == 0)
        {
            issues.Add(new MediaRuntimeValidationIssue($"Manifest.{name}.Identity", $"{name} version, compiler, configuration, and library versions are required."));
        }
    }

    private static void ValidateToolClosureEntry(
        string name,
        MediaRuntimeToolManifest tool,
        IReadOnlyList<MediaRuntimeFileManifest> files,
        List<MediaRuntimeValidationIssue> issues)
    {
        var closureEntry = files.FirstOrDefault(file => string.Equals(
            NormalizePath(file.RelativePath),
            NormalizePath(tool.RelativePath),
            StringComparison.OrdinalIgnoreCase));
        if (closureEntry is null || !string.Equals(closureEntry.Sha256, tool.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new MediaRuntimeValidationIssue("Manifest.RuntimeFiles", $"{name} executable must be present in the runtime closure with its approved hash."));
        }
    }

    private static void ValidateFileManifests(
        IReadOnlyList<MediaRuntimeFileManifest> files,
        List<MediaRuntimeValidationIssue> issues)
    {
        if (files.Count == 0)
        {
            issues.Add(new MediaRuntimeValidationIssue("Manifest.RuntimeFiles", "Runtime closure manifest cannot be empty."));
            return;
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!IsSafeRelativePath(file.RelativePath) || !IsSha256(file.Sha256))
            {
                issues.Add(new MediaRuntimeValidationIssue("Manifest.RuntimeFiles", "Each runtime file requires a safe relative path and SHA-256 hash."));
            }

            if (!paths.Add(NormalizePath(file.RelativePath)))
            {
                issues.Add(new MediaRuntimeValidationIssue("Manifest.RuntimeFiles", $"Runtime closure contains duplicate path '{file.RelativePath}'."));
            }
        }
    }

    private static void ValidateTool(
        string toolName,
        MediaRuntimeToolManifest expected,
        MediaRuntimeToolObservation observed,
        string runtimeRootPath,
        List<MediaRuntimeValidationIssue> issues)
    {
        if (!string.Equals(NormalizePath(expected.RelativePath), RelativeToRoot(runtimeRootPath, observed.ExecutablePath), StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new MediaRuntimeValidationIssue($"{toolName}.Path", $"{toolName} executable does not resolve to its approved runtime-relative path."));
        }

        Compare(toolName, "Hash", expected.Sha256, observed.Sha256, issues);
        Compare(toolName, "Version", expected.Version, observed.Version, issues);
        Compare(toolName, "Compiler", expected.Compiler, observed.Compiler, issues);
        Compare(toolName, "Configuration", expected.Configuration, observed.Configuration, issues);
        CompareDictionary(toolName, "LibraryVersions", expected.LibraryVersions, observed.LibraryVersions, issues);
    }

    private static void ValidateRuntimeClosure(
        IReadOnlyList<MediaRuntimeFileManifest> expected,
        IReadOnlyList<MediaRuntimeFileObservation> observed,
        List<MediaRuntimeValidationIssue> issues)
    {
        var expectedByPath = expected.ToDictionary(file => NormalizePath(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var observedByPath = observed.ToDictionary(file => NormalizePath(file.RelativePath), StringComparer.OrdinalIgnoreCase);

        foreach (var (path, file) in expectedByPath)
        {
            if (!observedByPath.TryGetValue(path, out var actual))
            {
                issues.Add(new MediaRuntimeValidationIssue("RuntimeFiles.Missing", $"Required runtime file '{file.RelativePath}' was not observed."));
                continue;
            }

            if (!string.Equals(file.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new MediaRuntimeValidationIssue("RuntimeFiles.Hash", $"Runtime file '{file.RelativePath}' hash did not match the approved manifest."));
            }
        }

        foreach (var (path, file) in observedByPath)
        {
            if (!expectedByPath.ContainsKey(path))
            {
                issues.Add(new MediaRuntimeValidationIssue("RuntimeFiles.Unexpected", $"Unexpected executable or library '{file.RelativePath}' was observed."));
            }
        }
    }

    private static void ValidateForbiddenFlags(
        IReadOnlyList<string> forbiddenFlags,
        ObservedMediaRuntime observedRuntime,
        List<MediaRuntimeValidationIssue> issues)
    {
        var configurations = new[] { observedRuntime.PrimaryTool.Configuration, observedRuntime.InspectionTool.Configuration };
        foreach (var flag in forbiddenFlags)
        {
            if (configurations.Any(configuration => ConfigurationContainsFlag(configuration, flag)))
            {
                issues.Add(new MediaRuntimeValidationIssue("Configuration.Forbidden", $"Forbidden configuration flag '{flag}' was observed."));
            }
        }
    }

    private static void ValidateRequiredComponents(
        IReadOnlyList<MediaRuntimeComponentRequirement> requirements,
        IReadOnlyDictionary<MediaRuntimeComponentKind, IReadOnlyList<string>> components,
        List<MediaRuntimeValidationIssue> issues)
    {
        foreach (var requirement in requirements)
        {
            var observed = components.TryGetValue(requirement.Kind, out var names)
                ? names.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in requirement.Names)
            {
                if (!observed.Contains(name))
                {
                    issues.Add(new MediaRuntimeValidationIssue("Components.Missing", $"Required {requirement.Kind.ToString().ToLowerInvariant()} '{name}' was not observed."));
                }
            }
        }
    }

    private static void ValidateParserContract(
        MediaRuntimeParserContractManifest expected,
        MediaRuntimeParserContractObservation observed,
        List<MediaRuntimeValidationIssue> issues)
    {
        if (!observed.Succeeded
            || !string.Equals(expected.ContractId, observed.ContractId, StringComparison.Ordinal)
            || !string.Equals(expected.ProgramVersion, observed.ProgramVersion, StringComparison.Ordinal))
        {
            issues.Add(new MediaRuntimeValidationIssue(
                "ParserContract.Mismatch",
                "Inspection tool did not satisfy the reviewed JSON program-version parser contract."));
        }
    }

    private static void Compare(
        string toolName,
        string field,
        string expected,
        string observed,
        List<MediaRuntimeValidationIssue> issues)
    {
        if (!string.Equals(NormalizeWhitespace(expected), NormalizeWhitespace(observed), StringComparison.Ordinal))
        {
            issues.Add(new MediaRuntimeValidationIssue($"{toolName}.{field}", $"{toolName} {field.ToLowerInvariant()} did not match the approved manifest."));
        }
    }

    private static void CompareDictionary(
        string toolName,
        string field,
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> observed,
        List<MediaRuntimeValidationIssue> issues)
    {
        if (expected.Count != observed.Count
            || expected.Any(entry => !observed.TryGetValue(entry.Key, out var value)
                || !string.Equals(NormalizeWhitespace(entry.Value), NormalizeWhitespace(value), StringComparison.Ordinal)))
        {
            issues.Add(new MediaRuntimeValidationIssue($"{toolName}.{field}", $"{toolName} library versions did not match the approved manifest."));
        }
    }

    private static bool IsSafeRelativePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..");

    private static bool IsSha256(string? value) =>
        value is not null && Regex.IsMatch(value, "^[0-9A-Fa-f]{64}$");

    private static string RelativeToRoot(string root, string path) => NormalizePath(Path.GetRelativePath(root, path));

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string NormalizeWhitespace(string value) => Regex.Replace(value.Trim(), "\\s+", " ");

    private static bool ConfigurationContainsFlag(string configuration, string flag) =>
        configuration.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => string.Equals(token, flag, StringComparison.Ordinal));
}
