namespace ReelForge.Core;

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
        if (string.IsNullOrWhiteSpace(request.Prompt)) errors.Add("A prompt is required.");
        if (!Modes.Contains(request.Mode)) errors.Add($"Mode '{request.Mode}' is not supported by {DisplayName}.");
        if (request.DurationSeconds < MinimumDurationSeconds || request.DurationSeconds > MaximumDurationSeconds)
            errors.Add($"Duration must be between {MinimumDurationSeconds} and {MaximumDurationSeconds} seconds.");
        if (!AspectRatios.Contains(request.AspectRatio, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Aspect ratio '{request.AspectRatio}' is not supported.");
        if (!Resolutions.Contains(request.Resolution, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Resolution '{request.Resolution}' is not supported.");

        var references = GenerationRequestReferenceResolver.Resolve(request, assets);
        if (references.Any(reference =>
                reference.LogicalObjectKind == GenerationReferenceObjectKind.Asset && reference.Asset is null))
            errors.Add("One or more reference assets no longer exist in the project.");
        ValidateReferenceCount(references, MediaType.Image, MaximumImageReferences, errors);
        ValidateReferenceCount(references, MediaType.Video, MaximumVideoReferences, errors);
        ValidateReferenceCount(references, MediaType.Audio, MaximumAudioReferences, errors);
        foreach (var unsupported in references.Where(reference => !SupportedReferenceTypes.Contains(reference.MediaType)))
            errors.Add($"{unsupported.DisplayName} cannot be used as a {unsupported.MediaType} reference.");
        if (request.Mode == GenerationMode.TextToVideo && references.Count > 0)
            errors.Add("Text-to-video requests cannot include reference assets.");
        if (request.Mode != GenerationMode.TextToVideo && references.Count == 0)
            errors.Add("This generation mode requires at least one reference asset.");
        return errors;
    }

    private static void ValidateReferenceCount(
        IReadOnlyCollection<GenerationRequestReference> references,
        MediaType type,
        int maximum,
        List<string> errors)
    {
        var count = references.Count(reference => reference.MediaType == type);
        if (count > maximum)
            errors.Add($"At most {maximum} {type.ToString().ToLowerInvariant()} reference(s) are supported.");
    }
}

public sealed class GenerationSubmission
{
    public string ProviderJobId { get; init; } = string.Empty;
    public GenerationStatus Status { get; init; } = GenerationStatus.Queued;
    public Dictionary<string, string> ResponseMetadata { get; init; } = new(StringComparer.Ordinal);
}
