using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

internal sealed class CompositionCurrentAccessor
{
    private readonly ProjectWorkspace _workspace;

    public CompositionCurrentAccessor(ProjectWorkspace workspace)
    {
        _workspace = workspace;
    }

    public VideoProject Project =>
        _workspace.Project ?? throw new InvalidOperationException("Open a project first.");

    public (ProjectAsset Asset, RecipeRevision Revision, CompositionRecipe Recipe) GetCurrent()
    {
        var project = Project;
        var compositionId = project.WorkingCompositionAssetId
            ?? throw new InvalidOperationException("Start a Working Composition first.");
        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == compositionId)
            ?? throw new InvalidDataException("The Working Composition asset is missing.");
        var revisionId = asset.Virtual?.CurrentRecipeRevisionId
            ?? throw new InvalidDataException("The Working Composition has no committed recipe revision.");
        var revision = project.RecipeRevisions.SingleOrDefault(candidate => candidate.Id == revisionId)
            ?? throw new InvalidDataException("The current Working Composition recipe revision is missing.");

        return revision.Recipe is CompositionRecipe recipe
            ? (asset, revision, recipe)
            : throw new InvalidDataException("The Working Composition revision does not contain a composition recipe.");
    }

    public ProjectAsset RequireVideoSource(Guid sourceAssetId)
    {
        var project = Project;
        var source = project.Assets.SingleOrDefault(asset => asset.Id == sourceAssetId)
            ?? throw new InvalidOperationException("The selected composition source no longer exists.");

        if (source.Id == project.WorkingCompositionAssetId)
            throw new InvalidOperationException("A Working Composition cannot contain itself.");
        if (source.MediaType != MediaType.Video ||
            (source.StorageKind == AssetStorageKind.Virtual && source.Virtual?.Kind != VirtualAssetKind.SavedClip))
            throw new InvalidOperationException("Add a physical video or Saved Clip to the Working Composition.");

        return source;
    }

    public ProjectAsset RequireAudioSource(Guid sourceAssetId)
    {
        var source = Project.Assets.SingleOrDefault(asset => asset.Id == sourceAssetId)
            ?? throw new InvalidOperationException("The selected audio source no longer exists.");

        if (source.StorageKind != AssetStorageKind.Physical || source.MediaType != MediaType.Audio)
            throw new InvalidOperationException("Add a physical audio file to the Working Composition.");

        return source;
    }

    public static CompositionSegment CreateSegment(ProjectAsset source) => new()
    {
        Source = new AssetRevisionReference
        {
            AssetId = source.Id,
            RecipeRevisionId = source.StorageKind == AssetStorageKind.Virtual
                ? source.Virtual?.CurrentRecipeRevisionId
                    ?? throw new InvalidOperationException("The selected Saved Clip has no committed recipe revision.")
                : null
        },
        Start = RecipeBoundary.SourceStart,
        End = RecipeBoundary.SourceEnd
    };
}
