using ReelForge.Core;

namespace ReelForge.Application;

public sealed class WorkingCompositionService
{
    private readonly ProjectWorkspace _workspace;

    public WorkingCompositionService(ProjectWorkspace workspace) => _workspace = workspace;

    public async Task<ProjectAsset> CreateInitialAsync(
        Guid sourceAssetId,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        if (project.WorkingCompositionAssetId is { } existingId)
            return project.Assets.Single(asset => asset.Id == existingId);
        var source = project.Assets.SingleOrDefault(asset => asset.Id == sourceAssetId)
            ?? throw new InvalidOperationException("The composition source no longer exists.");
        if (source.MediaType != MediaType.Video ||
            (source.StorageKind == AssetStorageKind.Virtual && source.Virtual?.Kind != VirtualAssetKind.SavedClip))
            throw new InvalidOperationException("Start the Working Composition from a physical video or Saved Clip.");
        Guid? sourceRevisionId = source.StorageKind == AssetStorageKind.Virtual
            ? source.Virtual?.CurrentRecipeRevisionId
                ?? throw new InvalidOperationException("The selected Saved Clip has no committed recipe revision.")
            : null;

        var assetsCount = project.Assets.Count;
        var revisionsCount = project.RecipeRevisions.Count;
        var draftsCount = project.RecipeDrafts.Count;
        var previousWorkingCompositionId = project.WorkingCompositionAssetId;
        try
        {
            var composition = new ProjectAsset
            {
                DisplayName = "Working Composition",
                MediaType = MediaType.Video,
                StorageKind = AssetStorageKind.Virtual,
                Origin = AssetOrigin.EditorDerived,
                Physical = null,
                Virtual = new VirtualAssetState
                {
                    Kind = VirtualAssetKind.Composition,
                    ExpectedMediaProperties = source.Encoding ?? source.Virtual?.ExpectedMediaProperties
                },
                Provenance = new AssetProvenance
                {
                    Operation = "working-composition",
                    SourceAssetIds = [source.Id]
                }
            };
            project.AddAsset(composition);
            var recipe = new CompositionRecipe
            {
                Segments =
                [
                    new CompositionSegment
                    {
                        Source = new AssetRevisionReference
                        {
                            AssetId = source.Id,
                            RecipeRevisionId = sourceRevisionId
                        },
                        Start = RecipeBoundary.SourceStart,
                        End = RecipeBoundary.SourceEnd
                    }
                ]
            };
            var revision = project.CommitRecipe(composition.Id, recipe);
            project.RecipeDrafts.Add(new RecipeDraft
            {
                VirtualAssetId = composition.Id,
                BasedOnRevisionId = revision.Id,
                EditableRecipe = new CompositionRecipe
                {
                    Segments = recipe.Segments.Select(segment => new CompositionSegment
                    {
                        Id = segment.Id,
                        Source = segment.Source with { },
                        Start = segment.Start with { },
                        End = segment.End with { },
                        AudioEnabled = segment.AudioEnabled
                    }).ToList()
                }
            });
            project.WorkingCompositionAssetId = composition.Id;
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
            return composition;
        }
        catch
        {
            project.Assets.RemoveRange(assetsCount, project.Assets.Count - assetsCount);
            project.RecipeRevisions.RemoveRange(revisionsCount, project.RecipeRevisions.Count - revisionsCount);
            project.RecipeDrafts.RemoveRange(draftsCount, project.RecipeDrafts.Count - draftsCount);
            project.WorkingCompositionAssetId = previousWorkingCompositionId;
            throw;
        }
    }
}
