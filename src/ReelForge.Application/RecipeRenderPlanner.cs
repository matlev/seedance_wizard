using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ReelForge.Core;

namespace ReelForge.Application;

public sealed record RecipeRenderPlan(
    Guid TargetAssetId,
    Guid TargetRecipeRevisionId,
    MaterializationPurpose Purpose,
    string? Profile,
    MediaRenderPlanNode Root,
    string PlanHash);

public abstract record MediaRenderPlanNode(
    Guid AssetId,
    MediaType MediaType,
    string NodeHash);

public sealed record PhysicalSourceRenderPlanNode(
    Guid SourceAssetId,
    MediaType SourceMediaType,
    string? ExpectedContentHash,
    string Hash)
    : MediaRenderPlanNode(SourceAssetId, SourceMediaType, Hash);

public sealed record TrimRenderPlanNode(
    Guid VirtualAssetId,
    MediaType OutputMediaType,
    Guid RecipeRevisionId,
    MediaRenderPlanNode Source,
    RecipeBoundary Start,
    RecipeBoundary End,
    string? RenderProfile,
    string Hash)
    : MediaRenderPlanNode(VirtualAssetId, OutputMediaType, Hash);

public sealed record ExtractFrameRenderPlanNode(
    Guid VirtualAssetId,
    Guid RecipeRevisionId,
    MediaRenderPlanNode Source,
    AnchorRevisionReference Anchor,
    string? ImageProfile,
    string Hash)
    : MediaRenderPlanNode(VirtualAssetId, MediaType.Image, Hash);

public sealed record CompositionRenderPlanNode(
    Guid VirtualAssetId,
    Guid RecipeRevisionId,
    IReadOnlyList<CompositionSegmentRenderPlan> Segments,
    CompositionCompatibilityReport Compatibility,
    string Hash)
    : MediaRenderPlanNode(VirtualAssetId, MediaType.Video, Hash)
{
    public IReadOnlyList<CompositionAudioClipRenderPlan> AudioClips { get; init; } = [];
}

public sealed record CompositionSegmentRenderPlan(
    Guid SegmentId,
    MediaRenderPlanNode Source,
    RecipeBoundary Start,
    RecipeBoundary End,
    bool AudioEnabled,
    string SegmentHash);

public sealed record CompositionAudioClipRenderPlan(
    Guid ClipId,
    MediaRenderPlanNode Source,
    long TimelineStartTicks,
    bool IsMuted,
    double GainDecibels,
    string ClipHash);

public static class RecipeRenderPlanner
{
    public static RecipeRenderPlan Plan(
        VideoProject project,
        AssetMaterializationTarget target,
        MaterializationPurpose purpose,
        string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(target);
        var asset = FindAsset(project, target.AssetId);
        if (asset.StorageKind != AssetStorageKind.Virtual || asset.Virtual is null)
            throw new InvalidOperationException("Recipe planning requires a virtual target asset.");
        var revisionId = target.RecipeRevisionId ?? asset.Virtual.CurrentRecipeRevisionId
            ?? throw new InvalidOperationException($"Virtual asset '{asset.EffectiveDisplayName}' has no committed recipe.");
        var root = BuildNode(
            project,
            new AssetRevisionReference { AssetId = asset.Id, RecipeRevisionId = revisionId },
            []);
        return new RecipeRenderPlan(
            asset.Id,
            revisionId,
            purpose,
            profile,
            root,
            Hash($"plan|{purpose}|{profile ?? string.Empty}|{root.NodeHash}"));
    }

    private static MediaRenderPlanNode BuildNode(
        VideoProject project,
        AssetRevisionReference source,
        HashSet<Guid> activeRevisions)
    {
        var asset = FindAsset(project, source.AssetId);
        if (asset.StorageKind == AssetStorageKind.Physical)
        {
            if (source.RecipeRevisionId is not null)
                throw new InvalidDataException($"Physical asset '{asset.Id}' cannot pin a recipe revision.");
            var contentHash = asset.Physical?.ContentIdentity.Sha256?.ToLowerInvariant();
            return new PhysicalSourceRenderPlanNode(
                asset.Id,
                asset.MediaType,
                contentHash,
                Hash($"physical|{asset.Id:N}|{asset.MediaType}|{contentHash ?? "pending"}"));
        }

        if (asset.Virtual is null)
            throw new InvalidDataException($"Virtual asset '{asset.Id}' has no virtual state.");
        var revisionId = source.RecipeRevisionId
            ?? throw new InvalidDataException($"Virtual source '{asset.EffectiveDisplayName}' must pin an exact recipe revision.");
        var revision = project.RecipeRevisions.SingleOrDefault(candidate =>
                candidate.Id == revisionId && candidate.VirtualAssetId == asset.Id)
            ?? throw new InvalidOperationException($"Recipe revision '{revisionId}' no longer exists for asset '{asset.Id}'.");
        if (!activeRevisions.Add(revision.Id))
            throw new InvalidDataException($"Recipe dependency cycle includes revision '{revision.Id}'.");

        try
        {
            return revision.Recipe switch
            {
                TrimRecipe trim => BuildTrimNode(project, asset, revision, trim, activeRevisions),
                ExtractFrameRecipe frame => BuildExtractFrameNode(project, asset, revision, frame, activeRevisions),
                CompositionRecipe composition => BuildCompositionNode(project, asset, revision, composition, activeRevisions),
                _ => throw new NotSupportedException($"Recipe '{revision.Recipe.GetType().Name}' cannot be planned.")
            };
        }
        finally
        {
            activeRevisions.Remove(revision.Id);
        }
    }

    private static TrimRenderPlanNode BuildTrimNode(
        VideoProject project,
        ProjectAsset asset,
        RecipeRevision revision,
        TrimRecipe trim,
        HashSet<Guid> activeRevisions)
    {
        var source = BuildNode(project, trim.Source, activeRevisions);
        if (source.MediaType != MediaType.Video || asset.MediaType != MediaType.Video)
            throw new InvalidDataException("Trim recipes require video input and output.");
        var hash = Hash(string.Join('|',
            "trim",
            asset.Id.ToString("N"),
            revision.Id.ToString("N"),
            source.NodeHash,
            BoundaryKey(trim.Start),
            BoundaryKey(trim.End),
            trim.RenderProfile ?? string.Empty));
        return new TrimRenderPlanNode(
            asset.Id, asset.MediaType, revision.Id, source, trim.Start, trim.End, trim.RenderProfile, hash);
    }

    private static ExtractFrameRenderPlanNode BuildExtractFrameNode(
        VideoProject project,
        ProjectAsset asset,
        RecipeRevision revision,
        ExtractFrameRecipe frame,
        HashSet<Guid> activeRevisions)
    {
        var source = BuildNode(project, frame.Source, activeRevisions);
        if (source.MediaType != MediaType.Video || asset.MediaType != MediaType.Image)
            throw new InvalidDataException("Extract-frame recipes require video input and image output.");
        var hash = Hash(string.Join('|',
            "frame",
            asset.Id.ToString("N"),
            revision.Id.ToString("N"),
            source.NodeHash,
            frame.Anchor.AnchorId.ToString("N"),
            frame.Anchor.AnchorRevisionId.ToString("N"),
            frame.ImageProfile ?? string.Empty));
        return new ExtractFrameRenderPlanNode(
            asset.Id, revision.Id, source, frame.Anchor, frame.ImageProfile, hash);
    }

    private static CompositionRenderPlanNode BuildCompositionNode(
        VideoProject project,
        ProjectAsset asset,
        RecipeRevision revision,
        CompositionRecipe composition,
        HashSet<Guid> activeRevisions)
    {
        if (asset.MediaType != MediaType.Video || composition.Segments.Count == 0)
            throw new InvalidDataException("Composition recipes require video output and at least one segment.");
        var segments = composition.Segments.Select(segment =>
        {
            var source = BuildNode(project, segment.Source, activeRevisions);
            if (source.MediaType != MediaType.Video)
                throw new InvalidDataException($"Composition segment '{segment.Id}' requires video input.");
            var segmentHash = Hash(string.Join('|',
                "segment",
                segment.Id.ToString("N"),
                source.NodeHash,
                BoundaryKey(segment.Start),
                BoundaryKey(segment.End),
                segment.AudioEnabled));
            return new CompositionSegmentRenderPlan(
                segment.Id, source, segment.Start, segment.End, segment.AudioEnabled, segmentHash);
        }).ToArray();
        var audioClips = composition.AudioClips.Select(clip =>
        {
            var source = BuildNode(project, clip.Source, activeRevisions);
            if (source.MediaType != MediaType.Audio)
                throw new InvalidDataException($"Composition audio clip '{clip.Id}' requires audio input.");
            if (clip.TimelineStartTicks < 0)
                throw new InvalidDataException($"Composition audio clip '{clip.Id}' has a negative timeline start.");
            if (!double.IsFinite(clip.GainDecibels) || clip.GainDecibels is < -60 or > 12)
                throw new InvalidDataException($"Composition audio clip '{clip.Id}' has invalid gain.");
            var clipHash = Hash(string.Join('|',
                "audio-clip",
                clip.Id.ToString("N"),
                source.NodeHash,
                clip.TimelineStartTicks,
                clip.IsMuted,
                clip.GainDecibels.ToString("R", CultureInfo.InvariantCulture)));
            return new CompositionAudioClipRenderPlan(
                clip.Id,
                source,
                clip.TimelineStartTicks,
                clip.IsMuted,
                clip.GainDecibels,
                clipHash);
        }).ToArray();
        var segmentKey = string.Join(';', segments.Select(segment => string.Join(',',
            segment.SegmentId.ToString("N"),
            segment.Source.NodeHash,
            BoundaryKey(segment.Start),
            BoundaryKey(segment.End),
            segment.AudioEnabled)));
        var compatibility = MediaCompatibilityAnalyzer.Analyze(
            composition.Segments.Select(segment =>
            {
                var sourceAsset = FindAsset(project, segment.Source.AssetId);
                return sourceAsset.Encoding ?? sourceAsset.Virtual?.ExpectedMediaProperties;
            }).ToArray());
        var audioKey = string.Join(';', audioClips.Select(clip => string.Join(',',
            clip.ClipId.ToString("N"),
            clip.Source.NodeHash,
            clip.TimelineStartTicks,
            clip.IsMuted,
            clip.GainDecibels.ToString("R", CultureInfo.InvariantCulture))));
        return new CompositionRenderPlanNode(
            asset.Id,
            revision.Id,
            segments,
            compatibility,
            Hash($"composition|{asset.Id:N}|{revision.Id:N}|{segmentKey}|{audioKey}"))
        {
            AudioClips = audioClips
        };
    }

    private static ProjectAsset FindAsset(VideoProject project, Guid assetId) =>
        project.Assets.SingleOrDefault(candidate => candidate.Id == assetId)
        ?? throw new InvalidOperationException($"Asset '{assetId}' no longer exists.");

    private static string BoundaryKey(RecipeBoundary boundary) => string.Join(':',
        boundary.Kind,
        boundary.Anchor?.AnchorId.ToString("N") ?? string.Empty,
        boundary.Anchor?.AnchorRevisionId.ToString("N") ?? string.Empty,
        boundary.Edge?.ToString() ?? string.Empty,
        boundary.TimestampSeconds?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
