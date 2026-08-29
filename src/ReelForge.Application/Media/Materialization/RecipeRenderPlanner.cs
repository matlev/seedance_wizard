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
    double Pan,
    long FadeInMilliseconds,
    long FadeOutMilliseconds,
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
                CompositionRecipe => RejectCompositionMaterialization(asset, revision),
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

    private static MediaRenderPlanNode RejectCompositionMaterialization(
        ProjectAsset asset,
        RecipeRevision revision) =>
        throw new InvalidDataException(
            $"Composition '{asset.EffectiveDisplayName}' (revision '{revision.Id}') cannot be materialized yet. " +
            "The candidate multitrack composition requires the track-aware renderer planned for Milestone 6; " +
            "use composition audition for its supported single visible-track shape or wait for track-aware preview/export.");

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
