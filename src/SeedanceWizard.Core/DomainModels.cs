namespace SeedanceWizard.Core;

public enum MediaType
{
    Image,
    Video,
    Audio
}

public enum AssetOrigin
{
    Imported,
    Generated,
    EditorDerived,
    ExtractedFrame,
    Exported
}

public enum GenerationMode
{
    TextToVideo,
    ImageToVideo,
    ReferenceToVideo
}

public enum GenerationStatus
{
    Draft,
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed class VideoProject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled project";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? MainVideoAssetId { get; set; }
    public List<ProjectAsset> Assets { get; set; } = [];
    public List<GenerationRecord> Generations { get; set; } = [];
    public Timeline Timeline { get; set; } = new();

    public void Touch() => ModifiedAt = DateTimeOffset.UtcNow;

    public void AddAsset(ProjectAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (Assets.Any(existing => existing.Id == asset.Id))
        {
            throw new InvalidOperationException($"Asset '{asset.Id}' already belongs to the project.");
        }

        Assets.Add(asset);
        if (MainVideoAssetId is null && asset.MediaType == MediaType.Video && asset.Origin == AssetOrigin.Generated)
        {
            MainVideoAssetId = asset.Id;
        }

        Touch();
    }
}

public sealed class ProjectAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public AssetOrigin Origin { get; set; } = AssetOrigin.Imported;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public MediaEncodingMetadata? Encoding { get; set; }
    public AssetProvenance? Provenance { get; set; }
    public Dictionary<string, string> ProviderReferences { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AssetProvenance
{
    public string Operation { get; set; } = string.Empty;
    public List<Guid> SourceAssetIds { get; set; } = [];
    public Guid? GenerationId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MediaEncodingMetadata
{
    public string? ContainerFormat { get; set; }
    public double? DurationSeconds { get; set; }
    public long? SizeBytes { get; set; }
    public long? BitRate { get; set; }
    public VideoStreamMetadata? Video { get; set; }
    public AudioStreamMetadata? Audio { get; set; }
}

public sealed class VideoStreamMetadata
{
    public string? Codec { get; set; }
    public string? CodecProfile { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? PixelFormat { get; set; }
    public string? FrameRate { get; set; }
    public string? TimeBase { get; set; }
    public int? CodecLevel { get; set; }
}

public sealed class AudioStreamMetadata
{
    public string? Codec { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
}

public sealed class GenerationRequest
{
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; } = GenerationMode.ReferenceToVideo;
    public int DurationSeconds { get; set; } = 15;
    public string AspectRatio { get; set; } = "16:9";
    public string Resolution { get; set; } = "720p";
    public List<Guid> ReferenceAssetIds { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
}

public sealed class GenerationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProviderId { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public GenerationRequest Request { get; set; } = new();
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ProviderJobId { get; set; }
    public GenerationStatus Status { get; set; } = GenerationStatus.Draft;
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? OutputAssetId { get; set; }
    public Dictionary<string, string> ResponseMetadata { get; set; } = new(StringComparer.Ordinal);
    public GenerationError? Error { get; set; }
    public Guid? ParentGenerationId { get; set; }
}

public sealed class GenerationError
{
    public int? HttpStatus { get; set; }
    public string? ProviderCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TechnicalDetails { get; set; }
}

public sealed record GenerationProviderCapabilities(
    string ProviderId,
    string DisplayName,
    string ModelVersion,
    IReadOnlyList<GenerationMode> Modes,
    int MinimumDurationSeconds,
    int MaximumDurationSeconds,
    IReadOnlyList<string> AspectRatios,
    IReadOnlyList<string> Resolutions,
    int MaximumImageReferences,
    int MaximumVideoReferences,
    int MaximumAudioReferences,
    IReadOnlySet<MediaType> SupportedReferenceTypes,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ProviderParameters)
{
    public IReadOnlyList<string> Validate(GenerationRequest request, IReadOnlyCollection<ProjectAsset> assets)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            errors.Add("A prompt is required.");
        }

        if (!Modes.Contains(request.Mode))
        {
            errors.Add($"Mode '{request.Mode}' is not supported by {DisplayName}.");
        }

        if (request.DurationSeconds < MinimumDurationSeconds || request.DurationSeconds > MaximumDurationSeconds)
        {
            errors.Add($"Duration must be between {MinimumDurationSeconds} and {MaximumDurationSeconds} seconds.");
        }

        if (!AspectRatios.Contains(request.AspectRatio, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Aspect ratio '{request.AspectRatio}' is not supported.");
        }

        if (!Resolutions.Contains(request.Resolution, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Resolution '{request.Resolution}' is not supported.");
        }

        var selectedAssets = request.ReferenceAssetIds
            .Select(id => assets.FirstOrDefault(asset => asset.Id == id))
            .ToList();

        if (selectedAssets.Any(asset => asset is null))
        {
            errors.Add("One or more reference assets no longer exist in the project.");
        }

        var references = selectedAssets.OfType<ProjectAsset>().ToList();
        ValidateReferenceCount(references, MediaType.Image, MaximumImageReferences, errors);
        ValidateReferenceCount(references, MediaType.Video, MaximumVideoReferences, errors);
        ValidateReferenceCount(references, MediaType.Audio, MaximumAudioReferences, errors);

        foreach (var unsupported in references.Where(asset => !SupportedReferenceTypes.Contains(asset.MediaType)))
        {
            errors.Add($"{unsupported.FileName} cannot be used as a {unsupported.MediaType} reference.");
        }

        if (request.Mode == GenerationMode.TextToVideo && references.Count > 0)
        {
            errors.Add("Text-to-video requests cannot include reference assets.");
        }

        if (request.Mode != GenerationMode.TextToVideo && references.Count == 0)
        {
            errors.Add("This generation mode requires at least one reference asset.");
        }

        return errors;
    }

    private static void ValidateReferenceCount(
        IReadOnlyCollection<ProjectAsset> assets,
        MediaType mediaType,
        int maximum,
        List<string> errors)
    {
        var count = assets.Count(asset => asset.MediaType == mediaType);
        if (count > maximum)
        {
            errors.Add($"At most {maximum} {mediaType.ToString().ToLowerInvariant()} reference(s) are supported.");
        }
    }
}

public sealed class GenerationSubmission
{
    public string ProviderJobId { get; init; } = string.Empty;
    public GenerationStatus Status { get; init; } = GenerationStatus.Queued;
    public Dictionary<string, string> ResponseMetadata { get; init; } = new(StringComparer.Ordinal);
}

public sealed class Timeline
{
    public List<TimelineClip> Clips { get; set; } = [];
}

public sealed class TimelineClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceAssetId { get; set; }
    public double InPointSeconds { get; set; }
    public double OutPointSeconds { get; set; }
    public double TimelinePositionSeconds { get; set; }
    public bool AudioEnabled { get; set; } = true;
}

public sealed class FrameAnchor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceVideoAssetId { get; set; }
    public long FrameNumber { get; set; }
    public double TimestampSeconds { get; set; }
    public string? Label { get; set; }
}
