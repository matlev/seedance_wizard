namespace ReelForge.Application;

/// <summary>
/// A paired external media runtime observed from one isolated runtime root.
/// This identifies operational tooling only; it does not define project media semantics.
/// </summary>
public sealed record MediaRuntimePair(
    string RuntimeRootPath,
    string PrimaryToolPath,
    string InspectionToolPath);

public enum MediaRuntimeComponentKind
{
    Encoder,
    Decoder,
    Muxer,
    Demuxer,
    Filter,
    Protocol
}

public sealed record MediaRuntimeFileObservation(string RelativePath, string Sha256);

public sealed record MediaRuntimeToolObservation(
    string ExecutablePath,
    string Sha256,
    string Version,
    string Compiler,
    string Configuration,
    IReadOnlyDictionary<string, string> LibraryVersions,
    IReadOnlyDictionary<MediaRuntimeComponentKind, IReadOnlyList<string>> Components);

public sealed record MediaRuntimeParserContractObservation(
    string ContractId,
    bool Succeeded,
    string ProgramVersion);

/// <summary>
/// Generated operational evidence. Component listing records availability only and never
/// establishes that a ReelForge semantic capability has been successfully executed.
/// </summary>
public sealed record ObservedMediaRuntime(
    string RuntimeRootPath,
    MediaRuntimeToolObservation PrimaryTool,
    MediaRuntimeToolObservation InspectionTool,
    MediaRuntimeParserContractObservation InspectionParserContract,
    IReadOnlyList<MediaRuntimeFileObservation> RuntimeFiles);

public sealed record MediaRuntimeFileManifest(string RelativePath, string Sha256);

public sealed record MediaRuntimeToolManifest(
    string RelativePath,
    string Sha256,
    string Version,
    string Compiler,
    string Configuration,
    IReadOnlyDictionary<string, string> LibraryVersions);

public sealed record MediaRuntimeComponentRequirement(
    MediaRuntimeComponentKind Kind,
    IReadOnlyList<string> Names);

public sealed record MediaRuntimeParserContractManifest(
    string ContractId,
    string ProgramVersion);

/// <summary>
/// A reviewed concrete runtime-profile mapping. It intentionally contains engine details;
/// product requirements remain outside this operational manifest.
/// </summary>
public sealed record MediaRuntimeProfile(
    string ProfileId,
    MediaRuntimeToolManifest PrimaryTool,
    MediaRuntimeToolManifest InspectionTool,
    IReadOnlyList<MediaRuntimeFileManifest> RuntimeFiles,
    IReadOnlyList<MediaRuntimeComponentRequirement> RequiredComponents,
    IReadOnlyList<string> ForbiddenConfigurationFlags,
    MediaRuntimeParserContractManifest InspectionParserContract);

public sealed record MediaRuntimeValidationIssue(string Code, string Message);

/// <summary>
/// A profile match is operational identity/component evidence only. Executed semantic media
/// proofs are recorded by the separate Gate 0 proof matrix, not by this assessment.
/// </summary>
public sealed record MediaRuntimeProfileAssessment(
    string ProfileId,
    IReadOnlyList<MediaRuntimeValidationIssue> Issues)
{
    public bool MatchesProfile => Issues.Count == 0;
}

public interface IMediaRuntimeObserver
{
    Task<ObservedMediaRuntime> ObserveAsync(
        MediaRuntimePair runtime,
        CancellationToken cancellationToken = default);
}

public interface IMediaRuntimeProfileValidator
{
    MediaRuntimeProfileAssessment Validate(
        MediaRuntimeProfile profile,
        ObservedMediaRuntime observedRuntime);
}
