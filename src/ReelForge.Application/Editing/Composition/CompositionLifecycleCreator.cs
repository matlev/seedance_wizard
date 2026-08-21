using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

internal sealed class CompositionLifecycleCreator
{
    private readonly ProjectWorkspace _workspace;
    private readonly CompositionCurrentAccessor _current;

    public CompositionLifecycleCreator(ProjectWorkspace workspace, CompositionCurrentAccessor current)
    {
        _workspace = workspace;
        _current = current;
    }

    public async Task<ProjectAsset> CreateInitialAsync(
        Guid sourceAssetId,
        CancellationToken cancellationToken)
    {
        var project = _current.Project;
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
        var previousModifiedAt = project.ModifiedAt;

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
                EditableRecipe = TransactionalCompositionRevisionEditor.CloneRecipe(recipe)
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
            project.ModifiedAt = previousModifiedAt;
            throw;
        }
    }
}
