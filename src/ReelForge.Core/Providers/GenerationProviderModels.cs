namespace ReelForge.Core;

public sealed record GenerationModeRequirements(
    int? FixedDurationSeconds = null,
    string? FixedAspectRatio = null,
    int? RequiredImageReferences = null,
    int? RequiredVideoReferences = null,
    int? RequiredAudioReferences = null,
    double? MinimumVideoReferenceDurationSeconds = null,
    double? MaximumVideoReferenceDurationSeconds = null);

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
    public IReadOnlyDictionary<GenerationMode, GenerationModeRequirements> ModeRequirements { get; init; } =
        new Dictionary<GenerationMode, GenerationModeRequirements>();

    public GenerationModeRequirements? GetModeRequirements(GenerationMode mode) =>
        ModeRequirements.TryGetValue(mode, out var requirements) ? requirements : null;

    public IReadOnlyList<string> Validate(GenerationRequest request, IReadOnlyCollection<ProjectAsset> assets)
    {
        var errors = new List<string>();
        var requirements = GetModeRequirements(request.Mode);
        if (string.IsNullOrWhiteSpace(request.Prompt)) errors.Add("A prompt is required.");
        if (!Modes.Contains(request.Mode)) errors.Add($"Mode '{request.Mode}' is not supported by {DisplayName}.");
        if (requirements?.FixedDurationSeconds is { } fixedDuration && request.DurationSeconds != fixedDuration)
            errors.Add($"Duration must be {fixedDuration} seconds for this mode.");
        else if (requirements?.FixedDurationSeconds is null &&
                 (request.DurationSeconds < MinimumDurationSeconds || request.DurationSeconds > MaximumDurationSeconds))
            errors.Add($"Duration must be between {MinimumDurationSeconds} and {MaximumDurationSeconds} seconds.");
        if (requirements?.FixedAspectRatio is { } fixedAspectRatio &&
            !request.AspectRatio.Equals(fixedAspectRatio, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Aspect ratio must be '{fixedAspectRatio}' for this mode.");
        else if (requirements?.FixedAspectRatio is null &&
                 !AspectRatios.Contains(request.AspectRatio, StringComparer.OrdinalIgnoreCase))
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
        ValidateRequiredReferenceCount(references, MediaType.Image, requirements?.RequiredImageReferences, errors);
        ValidateRequiredReferenceCount(references, MediaType.Video, requirements?.RequiredVideoReferences, errors);
        ValidateRequiredReferenceCount(references, MediaType.Audio, requirements?.RequiredAudioReferences, errors);
        ValidateVideoReferenceDurations(references, requirements, errors);
        foreach (var unsupported in references.Where(reference => !SupportedReferenceTypes.Contains(reference.MediaType)))
            errors.Add($"{unsupported.DisplayName} cannot be used as a {unsupported.MediaType} reference.");
        if (request.Mode == GenerationMode.TextToVideo && references.Count > 0)
            errors.Add("Text-to-video requests cannot include reference assets.");
        var hasPositiveExactReferenceRequirement = requirements is not null &&
            (requirements.RequiredImageReferences > 0 ||
             requirements.RequiredVideoReferences > 0 ||
             requirements.RequiredAudioReferences > 0);
        if (request.Mode != GenerationMode.TextToVideo && references.Count == 0 &&
            !hasPositiveExactReferenceRequirement)
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

    private static void ValidateRequiredReferenceCount(
        IReadOnlyCollection<GenerationRequestReference> references,
        MediaType type,
        int? required,
        List<string> errors)
    {
        if (required is null)
        {
            return;
        }

        var count = references.Count(reference => reference.MediaType == type);
        if (count != required)
            errors.Add($"This mode requires exactly {required} {type.ToString().ToLowerInvariant()} reference(s).");
    }

    private static void ValidateVideoReferenceDurations(
        IReadOnlyCollection<GenerationRequestReference> references,
        GenerationModeRequirements? requirements,
        List<string> errors)
    {
        if (requirements is null ||
            requirements.MinimumVideoReferenceDurationSeconds is null &&
            requirements.MaximumVideoReferenceDurationSeconds is null)
        {
            return;
        }

        foreach (var reference in references.Where(candidate => candidate.MediaType == MediaType.Video))
        {
            var duration = reference.Asset?.DurationSeconds ?? reference.Asset?.Encoding?.DurationSeconds;
            if (duration is not { } seconds || !double.IsFinite(seconds) || seconds <= 0)
            {
                errors.Add($"{reference.DisplayName} requires a known positive video duration for this mode.");
                continue;
            }

            if ((requirements.MinimumVideoReferenceDurationSeconds is { } minimumDuration && seconds < minimumDuration) ||
                (requirements.MaximumVideoReferenceDurationSeconds is { } maximumDuration && seconds > maximumDuration))
            {
                var range = requirements.MinimumVideoReferenceDurationSeconds is { } lower &&
                            requirements.MaximumVideoReferenceDurationSeconds is { } upper
                    ? $"between {lower:0.###} and {upper:0.###} seconds"
                    : requirements.MinimumVideoReferenceDurationSeconds is { } floor
                        ? $"at least {floor:0.###} seconds"
                        : $"at most {requirements.MaximumVideoReferenceDurationSeconds:0.###} seconds";
                errors.Add($"{reference.DisplayName} must be {range} for this mode.");
            }
        }
    }
}

public sealed class GenerationSubmission
{
    public string ProviderJobId { get; init; } = string.Empty;
    public GenerationStatus Status { get; init; } = GenerationStatus.Queued;
    public Dictionary<string, string> ResponseMetadata { get; init; } = new(StringComparer.Ordinal);
}
