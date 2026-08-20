using System.Collections.ObjectModel;

namespace ReelForge.Core;

public enum MediaType { Image, Video, Audio }
public enum AssetOrigin { Imported, Generated, EditorDerived, ExtractedFrame, Exported, ExtractedAudio }
public enum AssetStorageKind { Physical, Virtual }
public enum VirtualAssetKind { Other, SavedClip, Composition, ExtractedFrame }
public enum PhysicalAssetDurability { Source, Generated, Exported, Promoted }
public enum ContentHashStatus { Pending, Verified, Mismatch, Failed }
public enum PhysicalAssetAvailability { Unknown, Available, Missing }
public enum GenerationMode { TextToVideo, ImageToVideo, ReferenceToVideo }
public enum GenerationStatus { Draft, Queued, Running, Succeeded, Failed, Cancelled }
public enum OutputIngestionStatus { NotRequired, Pending, Running, Succeeded, Failed }
public enum GenerationReferenceObjectKind { Asset, FrameAnchor }
public enum GenerationReferenceRole { GeneralReference, StartFrame, EndFrame, Character, Style, Environment, Motion, Audio }
public enum GenerationRelationshipType { RetryOf, VariantOf, ContinueAfter, ContinueBefore, BasedOn }
public enum RecipeBoundaryKind { SourceStart, SourceEnd, Anchor, Timestamp }
public enum AnchorBoundaryEdge { BeforeFrame, AfterFrame }
public enum AnchorRemovalDisposition { Removed, Archived }

public sealed class VideoProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled project";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectAsset> Assets { get; set; } = [];
    public List<RecipeRevision> RecipeRevisions { get; set; } = [];
    public List<RecipeDraft> RecipeDrafts { get; set; } = [];
    public List<FrameAnchor> Anchors { get; set; } = [];
    public List<FrameAnchorRevision> AnchorRevisions { get; set; } = [];
    public Guid? WorkingCompositionAssetId { get; set; }
    public GenerationDraft? CurrentGenerationDraft { get; set; }
    public List<GenerationRecord> Generations { get; set; } = [];

    public void Touch() => ModifiedAt = DateTimeOffset.UtcNow;

    public void AddAsset(ProjectAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (Assets.Any(existing => existing.Id == asset.Id))
        {
            throw new InvalidOperationException($"Asset '{asset.Id}' already belongs to the project.");
        }

        Assets.Add(asset);
        Touch();
    }

    public RecipeRevision CommitRecipe(Guid virtualAssetId, AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var asset = Assets.SingleOrDefault(candidate => candidate.Id == virtualAssetId)
            ?? throw new InvalidOperationException($"Virtual asset '{virtualAssetId}' does not exist.");
        if (asset.StorageKind != AssetStorageKind.Virtual || asset.Virtual is null)
        {
            throw new InvalidOperationException($"Asset '{virtualAssetId}' is not virtual.");
        }

        var previousId = asset.Virtual.CurrentRecipeRevisionId;
        var previous = previousId is null
            ? null
            : RecipeRevisions.SingleOrDefault(candidate => candidate.Id == previousId.Value)
                ?? throw new InvalidOperationException($"Current recipe revision '{previousId}' does not exist.");
        var revision = new RecipeRevision
        {
            VirtualAssetId = virtualAssetId,
            RevisionNumber = (previous?.RevisionNumber ?? 0) + 1,
            PreviousRevisionId = previous?.Id,
            Recipe = recipe,
            CreatedAt = DateTimeOffset.UtcNow
        };

        RecipeRevisions.Add(revision);
        asset.Virtual.CurrentRecipeRevisionId = revision.Id;
        Touch();
        return revision;
    }

    public FrameAnchorRevision CommitAnchorRevision(Guid anchorId, ExactFramePosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        var anchor = Anchors.SingleOrDefault(candidate => candidate.Id == anchorId)
            ?? throw new InvalidOperationException($"Frame anchor '{anchorId}' does not exist.");
        var source = Assets.SingleOrDefault(candidate => candidate.Id == position.SourceAssetId)
            ?? throw new InvalidOperationException($"Anchor source asset '{position.SourceAssetId}' does not exist.");
        if (source.StorageKind != AssetStorageKind.Physical || source.MediaType != MediaType.Video)
            throw new InvalidOperationException("Frame anchors currently require a durable physical video source.");
        if (position.VideoStreamIndex < 0 ||
            position.TimeBaseNumerator <= 0 || position.TimeBaseDenominator <= 0)
            throw new InvalidOperationException("An exact frame position requires a valid stream, PTS, and rational time base.");
        if (!IsSha256(position.SourceContentHash))
            throw new InvalidOperationException("An exact frame position requires a verified source SHA-256 hash.");
        if (source.Physical?.ContentIdentity is not { Status: ContentHashStatus.Verified, Sha256: { } sourceHash } ||
            !sourceHash.Equals(position.SourceContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The exact frame position must match the source video's verified content identity.");

        var previous = anchor.CurrentRevisionId is { } currentId
            ? AnchorRevisions.SingleOrDefault(candidate => candidate.Id == currentId)
                ?? throw new InvalidOperationException($"Current anchor revision '{currentId}' does not exist.")
            : null;
        var revision = new FrameAnchorRevision
        {
            AnchorId = anchorId,
            RevisionNumber = (previous?.RevisionNumber ?? 0) + 1,
            PreviousRevisionId = previous?.Id,
            SourceAssetId = position.SourceAssetId,
            SourceContentHash = position.SourceContentHash,
            VideoStreamIndex = position.VideoStreamIndex,
            PresentationTimestamp = position.PresentationTimestamp,
            TimeBaseNumerator = position.TimeBaseNumerator,
            TimeBaseDenominator = position.TimeBaseDenominator,
            FrameNumber = position.FrameNumber
        };
        AnchorRevisions.Add(revision);
        anchor.CurrentRevisionId = revision.Id;
        Touch();
        return revision;
    }

    public AnchorRemovalDisposition RemoveOrArchiveAnchor(Guid anchorId)
    {
        var anchor = Anchors.SingleOrDefault(candidate => candidate.Id == anchorId)
            ?? throw new InvalidOperationException($"Frame anchor '{anchorId}' does not exist.");
        var isReferenced = RecipeRevisions.Any(revision => RecipeReferencesAnchor(revision.Recipe, anchorId)) ||
            RecipeDrafts.Any(draft => RecipeReferencesAnchor(draft.EditableRecipe, anchorId)) ||
            CurrentGenerationDraft?.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                reference.LogicalObjectId == anchorId) == true ||
            Generations.Any(generation => generation.RequestSnapshot.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                reference.LogicalObjectId == anchorId));
        if (isReferenced)
        {
            anchor.IsArchived = true;
            Touch();
            return AnchorRemovalDisposition.Archived;
        }

        AnchorRevisions.RemoveAll(revision => revision.AnchorId == anchorId);
        Anchors.Remove(anchor);
        Touch();
        return AnchorRemovalDisposition.Removed;
    }

    private static bool RecipeReferencesAnchor(AssetRecipe recipe, Guid anchorId) => recipe switch
    {
        TrimRecipe trim => trim.Start.Anchor?.AnchorId == anchorId || trim.End.Anchor?.AnchorId == anchorId,
        ExtractFrameRecipe frame => frame.Anchor.AnchorId == anchorId,
        CompositionRecipe composition => composition.Segments.Any(segment =>
            segment.Start.Anchor?.AnchorId == anchorId || segment.End.Anchor?.AnchorId == anchorId),
        _ => false
    };

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => Uri.IsHexDigit(character));
}

public sealed class ProjectAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public AssetStorageKind StorageKind { get; set; } = AssetStorageKind.Physical;
    public AssetOrigin Origin { get; set; } = AssetOrigin.Imported;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public MediaEncodingMetadata? Encoding { get; set; }
    public AssetProvenance? Provenance { get; set; }
    public PhysicalAssetStorage? Physical { get; set; } = new();
    public VirtualAssetState? Virtual { get; set; }
    public Dictionary<string, ProviderAssetReference> ProviderReferences { get; set; } = new(StringComparer.Ordinal);

    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName;
}

public sealed class PhysicalAssetStorage
{
    public string RelativePath { get; set; } = string.Empty;
    public PhysicalAssetDurability Durability { get; set; } = PhysicalAssetDurability.Source;
    public ContentIdentity ContentIdentity { get; set; } = new();
    public PhysicalAssetAvailability Availability { get; set; } = PhysicalAssetAvailability.Unknown;
}

public sealed class VirtualAssetState
{
    public VirtualAssetKind Kind { get; set; }
    public Guid? CurrentRecipeRevisionId { get; set; }
    public MediaEncodingMetadata? ExpectedMediaProperties { get; set; }
}

public sealed class ContentIdentity
{
    public const string Sha256Algorithm = "SHA-256";

    public string Algorithm { get; set; } = Sha256Algorithm;
    public string? Sha256 { get; set; }
    public ContentHashStatus Status { get; set; } = ContentHashStatus.Pending;
    public long? LengthBytes { get; set; }
    public DateTimeOffset? ObservedLastWriteTimeUtc { get; set; }
}

public sealed class ProviderAssetReference
{
    public string Value { get; set; } = string.Empty;
    public string? SourceContentHash { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public string? Scope { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class AssetProvenance
{
    public string Operation { get; set; } = string.Empty;
    public List<Guid> SourceAssetIds { get; set; } = [];
    public Guid? GenerationId { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

public sealed class RecipeRevision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid VirtualAssetId { get; init; }
    public int RevisionNumber { get; init; }
    public Guid? PreviousRevisionId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public AssetRecipe Recipe { get; init; } = new ExtractFrameRecipe();
}

public sealed class RecipeDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? VirtualAssetId { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public AssetRecipe EditableRecipe { get; set; } = new ExtractFrameRecipe();
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

public abstract record AssetRecipe;

public sealed record TrimRecipe : AssetRecipe
{
    public AssetRevisionReference Source { get; init; } = new();
    public RecipeBoundary Start { get; init; } = RecipeBoundary.SourceStart;
    public RecipeBoundary End { get; init; } = RecipeBoundary.SourceEnd;
    public string? RenderProfile { get; init; }
}

public sealed record ExtractFrameRecipe : AssetRecipe
{
    public AssetRevisionReference Source { get; init; } = new();
    public AnchorRevisionReference Anchor { get; init; } = new();
    public string? ImageProfile { get; init; }
}

public sealed record CompositionRecipe : AssetRecipe
{
    public List<CompositionSegment> Segments { get; init; } = [];
    public List<CompositionAudioClip> AudioClips { get; init; } = [];
}

public sealed record CompositionSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AssetRevisionReference Source { get; init; } = new();
    public RecipeBoundary Start { get; init; } = RecipeBoundary.SourceStart;
    public RecipeBoundary End { get; init; } = RecipeBoundary.SourceEnd;
    public bool AudioEnabled { get; init; } = true;
}

public sealed record CompositionAudioClip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AssetRevisionReference Source { get; init; } = new();
    public long TimelineStartTicks { get; init; }
    public bool IsMuted { get; init; }
    public double GainDecibels { get; init; }
    public double Pan { get; init; }
    public long FadeInMilliseconds { get; init; }
    public long FadeOutMilliseconds { get; init; }

    public TimeSpan TimelineStart => TimeSpan.FromTicks(TimelineStartTicks);
    public TimeSpan FadeIn => TimeSpan.FromMilliseconds(FadeInMilliseconds);
    public TimeSpan FadeOut => TimeSpan.FromMilliseconds(FadeOutMilliseconds);
}

public sealed record AssetRevisionReference
{
    public Guid AssetId { get; init; }
    public Guid? RecipeRevisionId { get; init; }
}

public sealed record AnchorRevisionReference
{
    public Guid AnchorId { get; init; }
    public Guid AnchorRevisionId { get; init; }
}

public sealed record RecipeBoundary
{
    public static RecipeBoundary SourceStart { get; } = new() { Kind = RecipeBoundaryKind.SourceStart };
    public static RecipeBoundary SourceEnd { get; } = new() { Kind = RecipeBoundaryKind.SourceEnd };

    public RecipeBoundaryKind Kind { get; init; }
    public AnchorRevisionReference? Anchor { get; init; }
    public AnchorBoundaryEdge? Edge { get; init; }
    public double? TimestampSeconds { get; init; }
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

// Mutable provider-input object. It is never persisted as generation history directly.
public sealed class GenerationRequest
{
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; } = GenerationMode.ReferenceToVideo;
    public int DurationSeconds { get; set; } = 15;
    public string AspectRatio { get; set; } = "16:9";
    public string Resolution { get; set; } = "720p";
    public List<Guid> ReferenceAssetIds { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
    // Transient provider-ready representations. These values are never persisted as logical history.
    public List<PreparedGenerationReference> PreparedReferences { get; set; } = [];
}

public sealed record PreparedGenerationReference(
    Guid ReferenceId,
    GenerationReferenceObjectKind LogicalObjectKind,
    Guid LogicalObjectId,
    MediaType MediaType,
    GenerationReferenceRole? Role,
    int Order,
    string ProviderRepresentation);

public sealed record GenerationRequestReference(
    Guid ReferenceId,
    GenerationReferenceObjectKind LogicalObjectKind,
    Guid LogicalObjectId,
    MediaType MediaType,
    GenerationReferenceRole? Role,
    int Order,
    string DisplayName,
    string? PreparedRepresentation,
    ProjectAsset? Asset);

public static class GenerationRequestReferenceResolver
{
    public static IReadOnlyList<GenerationRequestReference> Resolve(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assets);
        if (request.PreparedReferences.Count > 0)
        {
            return request.PreparedReferences.Select(reference =>
            {
                var asset = reference.LogicalObjectKind == GenerationReferenceObjectKind.Asset
                    ? assets.FirstOrDefault(candidate => candidate.Id == reference.LogicalObjectId)
                    : null;
                return new GenerationRequestReference(
                    reference.ReferenceId,
                    reference.LogicalObjectKind,
                    reference.LogicalObjectId,
                    reference.MediaType,
                    reference.Role,
                    reference.Order,
                    asset?.EffectiveDisplayName ?? "Saved Frame",
                    string.IsNullOrWhiteSpace(reference.ProviderRepresentation)
                        ? null
                        : reference.ProviderRepresentation,
                    asset);
            }).ToArray();
        }

        return request.ReferenceAssetIds.Select((id, index) =>
        {
            var asset = assets.FirstOrDefault(candidate => candidate.Id == id);
            return new GenerationRequestReference(
                Guid.Empty,
                GenerationReferenceObjectKind.Asset,
                id,
                asset?.MediaType ?? MediaType.Image,
                null,
                index,
                asset?.EffectiveDisplayName ?? $"Missing asset {index + 1}",
                null,
                asset);
        }).ToArray();
    }
}

public sealed class GenerationDraft
{
    public string? ProviderId { get; set; }
    public string? ModelVersion { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; } = GenerationMode.ReferenceToVideo;
    public int DurationSeconds { get; set; } = 15;
    public string AspectRatio { get; set; } = "16:9";
    public string Resolution { get; set; } = "720p";
    public List<GenerationReferenceDraft> References { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
    public Guid? ParentGenerationId { get; set; }
    public GenerationRelationshipType? RelationshipType { get; set; }
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GenerationReferenceDraft
{
    public Guid ReferenceId { get; set; } = Guid.NewGuid();
    public GenerationReferenceObjectKind ObjectKind { get; set; } = GenerationReferenceObjectKind.Asset;
    public Guid LogicalObjectId { get; set; }
    public Guid? AnchorRevisionId { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int? Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
}

public sealed class GenerationRequestSnapshot
{
    public string ProviderId { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public GenerationMode Mode { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public int DurationSeconds { get; init; }
    public string AspectRatio { get; init; } = string.Empty;
    public string Resolution { get; init; } = string.Empty;
    public IReadOnlyList<GenerationReferenceSnapshot> References { get; init; } = Array.Empty<GenerationReferenceSnapshot>();
    public IReadOnlyDictionary<string, string> ProviderParameters { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed class GenerationReferenceSnapshot
{
    public Guid ReferenceId { get; init; } = Guid.NewGuid();
    public GenerationReferenceObjectKind ObjectKind { get; init; }
    public Guid LogicalObjectId { get; init; }
    public Guid? RecipeRevisionId { get; init; }
    public FrameAnchorReferenceSnapshot? Anchor { get; init; }
    public string? ContentHash { get; init; }
    public GenerationReferenceRole? Role { get; init; }
    public int? Order { get; init; }
    public string? Label { get; init; }
    public string? Notes { get; init; }
    public MaterializationReceipt? Materialization { get; init; }
}

public sealed record FrameAnchorReferenceSnapshot
{
    public Guid AnchorRevisionId { get; init; }
    public Guid SourceAssetId { get; init; }
    public string SourceContentHash { get; init; } = string.Empty;
    public int VideoStreamIndex { get; init; }
    public long PresentationTimestamp { get; init; }
    public int TimeBaseNumerator { get; init; }
    public int TimeBaseDenominator { get; init; }
    public long? FrameNumber { get; init; }
}

public sealed class MaterializationReceipt
{
    public string? PlanHash { get; init; }
    public string? SourceContentHash { get; init; }
    public string? ProducedContentHash { get; init; }
    public MediaEncodingMetadata? Encoding { get; init; }
    public string? ProviderReferenceId { get; init; }
    public string? ProviderScope { get; init; }
    public DateTimeOffset? ProviderReferenceExpiresAt { get; init; }
}

public sealed class GenerationRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GenerationRequestSnapshot RequestSnapshot { get; init; } = new();
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? ProviderJobId { get; set; }
    public GenerationStatus Status { get; set; } = GenerationStatus.Draft;
    public OutputIngestionStatus IngestionStatus { get; set; } = OutputIngestionStatus.NotRequired;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<Guid> OutputAssetIds { get; set; } = [];
    public Dictionary<string, string> ResponseMetadata { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, MaterializationReceipt> ReferenceMaterializations { get; set; } = [];
    public GenerationError? Error { get; set; }
    public Guid? ParentGenerationId { get; init; }
    public GenerationRelationshipType? RelationshipType { get; init; }
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
        if (string.IsNullOrWhiteSpace(request.Prompt)) errors.Add("A prompt is required.");
        if (!Modes.Contains(request.Mode)) errors.Add($"Mode '{request.Mode}' is not supported by {DisplayName}.");
        if (request.DurationSeconds < MinimumDurationSeconds || request.DurationSeconds > MaximumDurationSeconds)
            errors.Add($"Duration must be between {MinimumDurationSeconds} and {MaximumDurationSeconds} seconds.");
        if (!AspectRatios.Contains(request.AspectRatio, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Aspect ratio '{request.AspectRatio}' is not supported.");
        if (!Resolutions.Contains(request.Resolution, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Resolution '{request.Resolution}' is not supported.");

        var references = GenerationRequestReferenceResolver.Resolve(request, assets);
        if (references.Any(reference => reference.LogicalObjectKind == GenerationReferenceObjectKind.Asset && reference.Asset is null))
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

    private static void ValidateReferenceCount(IReadOnlyCollection<GenerationRequestReference> references, MediaType type, int maximum, List<string> errors)
    {
        var count = references.Count(reference => reference.MediaType == type);
        if (count > maximum) errors.Add($"At most {maximum} {type.ToString().ToLowerInvariant()} reference(s) are supported.");
    }
}

public sealed class GenerationSubmission
{
    public string ProviderJobId { get; init; } = string.Empty;
    public GenerationStatus Status { get; init; } = GenerationStatus.Queued;
    public Dictionary<string, string> ResponseMetadata { get; init; } = new(StringComparer.Ordinal);
}

public sealed class FrameAnchor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CurrentRevisionId { get; set; }
    public string? DisplayLabel { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FrameAnchorRevision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AnchorId { get; init; }
    public int RevisionNumber { get; init; }
    public Guid? PreviousRevisionId { get; init; }
    public Guid SourceAssetId { get; init; }
    public string SourceContentHash { get; init; } = string.Empty;
    public int VideoStreamIndex { get; init; }
    public long PresentationTimestamp { get; init; }
    public int TimeBaseNumerator { get; init; }
    public int TimeBaseDenominator { get; init; }
    public long? FrameNumber { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public double TimestampSeconds =>
        PresentationTimestamp * (double)TimeBaseNumerator / TimeBaseDenominator;
}

public sealed record ExactFramePosition(
    Guid SourceAssetId,
    string SourceContentHash,
    int VideoStreamIndex,
    long PresentationTimestamp,
    int TimeBaseNumerator,
    int TimeBaseDenominator,
    long? FrameNumber = null);
