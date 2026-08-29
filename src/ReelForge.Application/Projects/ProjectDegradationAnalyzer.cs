using ReelForge.Core;
using System.Collections.ObjectModel;

namespace ReelForge.Application;

public enum ProjectDegradedMediaKind { SavedFrame, SavedClip, Composition }

/// <summary>
/// An active Project Media item whose exact, persisted dependency graph cannot currently be
/// materialized. The logical record remains retained for history and possible future relinking.
/// </summary>
public sealed record ProjectDegradedMediaItem(
    Guid LogicalId,
    ProjectDegradedMediaKind Kind,
    string DisplayName);

public sealed class ProjectDegradationReport
{
    private readonly ReadOnlyCollection<ProjectDegradedMediaItem> _items;
    private readonly HashSet<Guid> _anchorIds;
    private readonly HashSet<Guid> _assetIds;

    public ProjectDegradationReport(IEnumerable<ProjectDegradedMediaItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = Array.AsReadOnly(items.ToArray());
        _anchorIds = _items.Where(item => item.Kind == ProjectDegradedMediaKind.SavedFrame)
            .Select(item => item.LogicalId).ToHashSet();
        _assetIds = _items.Where(item => item.Kind != ProjectDegradedMediaKind.SavedFrame)
            .Select(item => item.LogicalId).ToHashSet();
    }

    public IReadOnlyList<ProjectDegradedMediaItem> Items => _items;
    public int SavedFrameCount => _items.Count(item => item.Kind == ProjectDegradedMediaKind.SavedFrame);
    public int SavedClipCount => _items.Count(item => item.Kind == ProjectDegradedMediaKind.SavedClip);
    public int CompositionCount => _items.Count(item => item.Kind == ProjectDegradedMediaKind.Composition);
    public int CleanupCandidateCount => _items.Count;

    public bool IsDegradedAnchor(Guid anchorId) => _anchorIds.Contains(anchorId);
    public bool IsDegradedAsset(Guid assetId) => _assetIds.Contains(assetId);
}

/// <summary>
/// Evaluates the exact pinned dependency graph of visible derived media. Physical roots are
/// degraded only when deleted, missing, inaccessible, or mismatched; Unknown is intentionally
/// not treated as a failure.
/// </summary>
public sealed class ProjectDegradationAnalyzer
{
#pragma warning disable CA1822 // Kept as an injected application service at the coordination boundary.
    public ProjectDegradationReport Analyze(VideoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var graph = new DegradationGraph(project);
        var items = new List<ProjectDegradedMediaItem>();

        foreach (var anchor in project.Anchors.Where(anchor => !anchor.IsArchived))
        {
            if (graph.IsAnchorDegraded(anchor.Id))
                items.Add(new ProjectDegradedMediaItem(anchor.Id, ProjectDegradedMediaKind.SavedFrame,
                    anchor.DisplayLabel ?? "Saved Frame"));
        }

        foreach (var asset in project.Assets.Where(asset =>
                     !asset.IsDeleted &&
                     asset.StorageKind == AssetStorageKind.Virtual &&
                     asset.Virtual?.Kind is VirtualAssetKind.SavedClip or VirtualAssetKind.Composition))
        {
            if (!graph.IsVirtualAssetDegraded(asset.Id))
                continue;

            items.Add(new ProjectDegradedMediaItem(asset.Id,
                asset.Virtual!.Kind == VirtualAssetKind.SavedClip
                    ? ProjectDegradedMediaKind.SavedClip
                    : ProjectDegradedMediaKind.Composition,
                asset.EffectiveDisplayName));
        }

        return new ProjectDegradationReport(items);
    }
#pragma warning restore CA1822

    private sealed class DegradationGraph
    {
        private readonly Dictionary<Guid, ProjectAsset> _assets;
        private readonly Dictionary<Guid, RecipeRevision> _recipes;
        private readonly Dictionary<Guid, FrameAnchor> _anchors;
        private readonly Dictionary<Guid, FrameAnchorRevision> _anchorRevisions;
        private readonly Dictionary<Guid, bool> _recipeResults = [];
        private readonly Dictionary<Guid, bool> _anchorResults = [];
        private readonly HashSet<Guid> _visitingRecipes = [];

        public DegradationGraph(VideoProject project)
        {
            _assets = project.Assets.GroupBy(asset => asset.Id).ToDictionary(group => group.Key, group => group.First());
            _recipes = project.RecipeRevisions.GroupBy(revision => revision.Id).ToDictionary(group => group.Key, group => group.First());
            _anchors = project.Anchors.GroupBy(anchor => anchor.Id).ToDictionary(group => group.Key, group => group.First());
            _anchorRevisions = project.AnchorRevisions.GroupBy(revision => revision.Id).ToDictionary(group => group.Key, group => group.First());
        }

        public bool IsVirtualAssetDegraded(Guid assetId)
        {
            if (!_assets.TryGetValue(assetId, out var asset) || asset.StorageKind != AssetStorageKind.Virtual ||
                asset.Virtual?.CurrentRecipeRevisionId is not { } recipeId)
                return true;
            return IsRecipeDegraded(recipeId, assetId);
        }

        public bool IsAnchorDegraded(Guid anchorId)
        {
            if (_anchorResults.TryGetValue(anchorId, out var cached)) return cached;
            if (!_anchors.TryGetValue(anchorId, out var anchor) || anchor.CurrentRevisionId is not { } revisionId ||
                !_anchorRevisions.TryGetValue(revisionId, out var revision) || revision.AnchorId != anchorId)
                return _anchorResults[anchorId] = true;
            return _anchorResults[anchorId] = IsAnchorRevisionDegraded(revision);
        }

        private bool IsRecipeDegraded(Guid recipeId, Guid expectedAssetId)
        {
            if (_recipeResults.TryGetValue(recipeId, out var cached)) return cached;
            if (!_recipes.TryGetValue(recipeId, out var recipe) || recipe.VirtualAssetId != expectedAssetId)
                return _recipeResults[recipeId] = true;
            if (!_visitingRecipes.Add(recipeId))
                return _recipeResults[recipeId] = true;

            try
            {
                var degraded = recipe.Recipe switch
                {
                    TrimRecipe trim => IsAssetReferenceDegraded(trim.Source) ||
                                       IsBoundaryDegraded(trim.Start, trim.Source) ||
                                       IsBoundaryDegraded(trim.End, trim.Source),
                    ExtractFrameRecipe frame => IsAssetReferenceDegraded(frame.Source) ||
                                                IsAnchorReferenceDegraded(frame.Anchor),
                    CompositionRecipe composition => composition.Composition.VideoTracks
                                                            .SelectMany(track => track.Items)
                                                            .Any(item => IsAssetReferenceDegraded(item.Source)) ||
                                                        composition.Composition.AudioTracks
                                                            .SelectMany(track => track.Items)
                                                            .Any(item => IsAssetReferenceDegraded(item.Source)),
                    _ => true
                };
                return _recipeResults[recipeId] = degraded;
            }
            finally
            {
                _visitingRecipes.Remove(recipeId);
            }
        }

        private bool IsAssetReferenceDegraded(AssetRevisionReference reference)
        {
            if (!_assets.TryGetValue(reference.AssetId, out var asset)) return true;
            if (asset.StorageKind == AssetStorageKind.Physical)
                return reference.RecipeRevisionId is not null || asset.IsDeleted || asset.Physical is null ||
                       asset.Physical.Availability is PhysicalAssetAvailability.Missing or
                           PhysicalAssetAvailability.Inaccessible or PhysicalAssetAvailability.Mismatched;

            return asset.IsDeleted || reference.RecipeRevisionId is not { } recipeId || IsRecipeDegraded(recipeId, asset.Id);
        }

        private bool IsAnchorReferenceDegraded(AnchorRevisionReference reference)
        {
            if (!_anchorRevisions.TryGetValue(reference.AnchorRevisionId, out var revision) ||
                revision.AnchorId != reference.AnchorId)
                return true;
            return IsAnchorRevisionDegraded(revision);
        }

        private bool IsAnchorRevisionDegraded(FrameAnchorRevision revision)
        {
            if (!_assets.TryGetValue(revision.SourceAssetId, out var source)) return true;
            if (source.StorageKind == AssetStorageKind.Physical)
                return revision.SourceRecipeRevisionId is not null || IsAssetReferenceDegraded(
                    new AssetRevisionReference { AssetId = source.Id });
            return revision.SourceRecipeRevisionId is not { } recipeId || IsRecipeDegraded(recipeId, source.Id);
        }

        private bool IsBoundaryDegraded(RecipeBoundary boundary, AssetRevisionReference source) =>
            boundary.Kind == RecipeBoundaryKind.Anchor &&
            (boundary.Anchor is null ||
             IsAnchorReferenceDegraded(boundary.Anchor) ||
             !_anchorRevisions.TryGetValue(boundary.Anchor.AnchorRevisionId, out var revision) ||
             revision.SourceAssetId != source.AssetId || revision.SourceRecipeRevisionId != source.RecipeRevisionId);
    }
}
