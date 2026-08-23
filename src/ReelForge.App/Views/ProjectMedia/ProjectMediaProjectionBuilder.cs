using System.IO;
using System.Windows.Media.Imaging;
using ReelForge.App.Views.Generation;
using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

/// <summary>
/// Builds the Project Media and generation-reference projections for the current project.
/// It deliberately has no selection or control-lifecycle responsibilities.
/// </summary>
public static class ProjectMediaProjectionBuilder
{
    public static ProjectMediaProjection Build(
        VideoProject project,
        Func<ProjectAsset, string> resolvePhysicalAssetPath,
        Func<string, BitmapSource> loadBitmap,
        IEnumerable<GenerationReferenceChoice> existingChoices)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(resolvePhysicalAssetPath);
        ArgumentNullException.ThrowIfNull(loadBitmap);
        ArgumentNullException.ThrowIfNull(existingChoices);

        var choices = existingChoices.ToArray();
        var projectedChoices = new List<GenerationReferenceChoice>();
        var mediaItems = new List<ProjectMediaListItem>();

        foreach (var asset in project.Assets)
        {
            var mediaItem = new ProjectMediaListItem(asset);
            if (asset is { StorageKind: AssetStorageKind.Physical, MediaType: MediaType.Image })
            {
                var path = resolvePhysicalAssetPath(asset);
                if (File.Exists(path))
                {
                    try
                    {
                        mediaItem.Thumbnail = loadBitmap(path);
                    }
                    catch (Exception exception) when (exception is IOException or NotSupportedException)
                    {
                        // The viewer reports unreadable image details when the item is explicitly selected.
                    }
                }
            }

            mediaItems.Add(mediaItem);
            var matching = choices.Where(choice =>
                choice.ObjectKind == GenerationReferenceObjectKind.Asset && choice.LogicalObjectId == asset.Id).ToArray();
            if (matching.Length > 0)
            {
                foreach (var existing in matching)
                {
                    existing.UpdateAsset(asset, mediaItem.Thumbnail);
                    projectedChoices.Add(existing);
                }
            }
            else
            {
                projectedChoices.Add(new GenerationReferenceChoice(asset, projectedChoices.Count, mediaItem.Thumbnail));
            }
        }

        foreach (var anchor in project.Anchors.Where(anchor => !anchor.IsArchived))
        {
            if (anchor.CurrentRevisionId is not { } revisionId) continue;
            var revision = project.AnchorRevisions.SingleOrDefault(candidate => candidate.Id == revisionId);
            if (revision is null) continue;

            var source = project.Assets.SingleOrDefault(asset => asset.Id == revision.SourceAssetId);
            mediaItems.Add(new ProjectMediaListItem(anchor, revision));
            var matching = choices.Where(choice =>
                choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor && choice.LogicalObjectId == anchor.Id).ToArray();
            if (matching.Length > 0)
            {
                foreach (var existing in matching)
                {
                    existing.UpdateAnchor(anchor, revision, source?.EffectiveDisplayName);
                    projectedChoices.Add(existing);
                }
            }
            else
            {
                projectedChoices.Add(new GenerationReferenceChoice(
                    anchor,
                    revision,
                    source?.EffectiveDisplayName,
                    projectedChoices.Count));
            }
        }

        return new ProjectMediaProjection(
            mediaItems
                .OrderBy(item => item.GroupOrder)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            projectedChoices,
            project.Generations.OrderByDescending(item => item.RequestedAt).ToArray());
    }
}

public sealed record ProjectMediaProjection(
    IReadOnlyList<ProjectMediaListItem> MediaItems,
    IReadOnlyList<GenerationReferenceChoice> ReferenceChoices,
    IReadOnlyList<GenerationRecord> GenerationHistory);
