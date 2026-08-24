using System.Text.Json;
using ReelForge.Application;
using ReelForge.Infrastructure;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0RuntimeFactAttribute : FactAttribute
{
    public Gate0RuntimeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_RUNTIME_ROOT")))
        {
            Skip = "Gate 0 P2 runtime validation is opt-in and requires an explicitly verified runtime root.";
        }
    }
}

public sealed class Gate0P2RuntimeIntegrationTests
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new() { WriteIndented = true };

    [Gate0RuntimeFact]
    [Trait("Category", "Gate0ExecutableProof")]
    public async Task ApprovedP2RuntimeMatchesReviewedManifest()
    {
        var runtimeRoot = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_RUNTIME_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(runtimeRoot));

        var manifestPath = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_MANIFEST")
            ?? RepositoryPath("eng", "gate0", "manifests", "p2-btbn-lgplv3-shared-windows-x64-20260820.json");
        var evidencePath = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_EVIDENCE_PATH");

        var profile = LoadProfile(manifestPath);
        var runtime = new MediaRuntimePair(
            runtimeRoot!,
            Path.Combine(runtimeRoot!, profile.PrimaryTool.RelativePath),
            Path.Combine(runtimeRoot!, profile.InspectionTool.RelativePath));
        var observation = await new PairedMediaRuntimeObserver(new ExternalProcessRunner()).ObserveAsync(runtime);
        var assessment = new MediaRuntimeProfileValidator().Validate(profile, observation);

        if (!string.IsNullOrWhiteSpace(evidencePath))
        {
            var evidenceDirectory = Path.GetDirectoryName(Path.GetFullPath(evidencePath));
            if (!string.IsNullOrWhiteSpace(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);
            var evidence = new
            {
                schemaVersion = 1,
                profileId = profile.ProfileId,
                observedAtUtc = DateTimeOffset.UtcNow,
                statement = assessment.MatchesProfile
                    ? "Runtime matches the reviewed Gate 0 profile. Semantic capabilities are not yet proven."
                    : "Runtime does not match the reviewed Gate 0 profile.",
                observation,
                assessment,
                executedSemanticProofs = Array.Empty<object>()
            };
            await File.WriteAllTextAsync(
                evidencePath,
                JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
        }

        Assert.True(
            assessment.MatchesProfile,
            string.Join(Environment.NewLine, assessment.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static MediaRuntimeProfile LoadProfile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var configuration = root.GetProperty("configuration").GetString()!;
        var libraries = root.GetProperty("libraryVersions").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);

        var primary = Tool(root.GetProperty("primaryTool"), configuration, libraries);
        var inspection = Tool(root.GetProperty("inspectionTool"), configuration, libraries);
        var runtimeFiles = root.GetProperty("runtimeFiles").EnumerateArray()
            .Select(file => new MediaRuntimeFileManifest(file.GetProperty("path").GetString()!, file.GetProperty("sha256").GetString()!))
            .ToArray();
        var requirements = ReadRequirements(root.GetProperty("approvedProofComponents"));
        var parser = root.GetProperty("inspectionParserContract");

        return new MediaRuntimeProfile(
            root.GetProperty("profileId").GetString()!,
            primary,
            inspection,
            runtimeFiles,
            requirements,
            root.GetProperty("forbiddenConfigurationFlags").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            new MediaRuntimeParserContractManifest(
                parser.GetProperty("contractId").GetString()!,
                parser.GetProperty("programVersion").GetString()!));
    }

    private static MediaRuntimeToolManifest Tool(
        JsonElement element,
        string configuration,
        IReadOnlyDictionary<string, string> libraries) => new(
        element.GetProperty("relativePath").GetString()!,
        element.GetProperty("sha256").GetString()!,
        element.GetProperty("versionLine").GetString()!,
        element.GetProperty("compiler").GetString()!,
        configuration,
        libraries);

    private static List<MediaRuntimeComponentRequirement> ReadRequirements(JsonElement components)
    {
        var requirements = new List<MediaRuntimeComponentRequirement>();
        foreach (var property in components.EnumerateObject())
        {
            var kind = property.Name switch
            {
                "encoders" => MediaRuntimeComponentKind.Encoder,
                "decoders" => MediaRuntimeComponentKind.Decoder,
                "muxers" => MediaRuntimeComponentKind.Muxer,
                "demuxers" => MediaRuntimeComponentKind.Demuxer,
                "filters" => MediaRuntimeComponentKind.Filter,
                "protocols" => MediaRuntimeComponentKind.Protocol,
                _ => throw new InvalidDataException($"Unknown media runtime component group '{property.Name}'.")
            };
            requirements.Add(new MediaRuntimeComponentRequirement(
                kind,
                property.Value.EnumerateArray().Select(value => value.GetString()!).ToArray()));
        }
        return requirements;
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore")))
        {
            directory = directory.Parent;
        }
        if (directory is null) throw new DirectoryNotFoundException("Could not locate the ReelForge repository root.");
        return Path.Combine([directory.FullName, .. segments]);
    }
}
