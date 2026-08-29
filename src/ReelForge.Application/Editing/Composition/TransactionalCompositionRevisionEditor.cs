using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

internal sealed class TransactionalCompositionRevisionEditor
{
    private readonly ProjectWorkspace _workspace;
    private readonly CompositionCurrentAccessor _current;

    public TransactionalCompositionRevisionEditor(
        ProjectWorkspace workspace,
        CompositionCurrentAccessor current)
    {
        _workspace = workspace;
        _current = current;
    }

    public async Task<RecipeRevision> UpdateAsync(
        Func<WorkingCompositionState, WorkingCompositionState> transform,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transform);

        var project = _current.Project;
        var (asset, _, currentRecipe) = _current.GetCurrent();
        var state = transform(currentRecipe.Composition)
            ?? throw new InvalidOperationException("A Working Composition transform must return a composition state.");
        var recipe = new CompositionRecipe { Composition = state };

        var revisionCount = project.RecipeRevisions.Count;
        var oldProjectModifiedAt = project.ModifiedAt;
        var oldCurrentRevisionId = asset.Virtual!.CurrentRecipeRevisionId;
        var oldSources = asset.Provenance?.SourceAssetIds.ToList() ?? [];
        var existingDraft = project.RecipeDrafts.SingleOrDefault(draft => draft.VirtualAssetId == asset.Id);
        var oldDraftBasedOn = existingDraft?.BasedOnRevisionId;
        var oldDraftRecipe = existingDraft?.EditableRecipe;
        var oldDraftModifiedAt = existingDraft?.ModifiedAt;

        try
        {
            var revision = project.CommitRecipe(asset.Id, recipe);
            asset.Provenance ??= new AssetProvenance { Operation = "working-composition" };
            asset.Provenance.SourceAssetIds = recipe.Composition.VideoTracks
                .SelectMany(track => track.Items.Select(item => item.Source.AssetId))
                .Concat(recipe.Composition.AudioTracks.SelectMany(track => track.Items.Select(item => item.Source.AssetId)))
                .Distinct()
                .ToList();

            if (existingDraft is null)
            {
                existingDraft = new RecipeDraft { VirtualAssetId = asset.Id };
                project.RecipeDrafts.Add(existingDraft);
            }

            existingDraft.BasedOnRevisionId = revision.Id;
            existingDraft.EditableRecipe = CloneRecipe(recipe);
            existingDraft.ModifiedAt = DateTimeOffset.UtcNow;
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
            return revision;
        }
        catch
        {
            project.RecipeRevisions.RemoveRange(
                revisionCount,
                project.RecipeRevisions.Count - revisionCount);
            project.ModifiedAt = oldProjectModifiedAt;
            asset.Virtual!.CurrentRecipeRevisionId = oldCurrentRevisionId;
            if (asset.Provenance is not null)
                asset.Provenance.SourceAssetIds = oldSources;

            if (oldDraftRecipe is null)
            {
                project.RecipeDrafts.RemoveAll(draft => draft.VirtualAssetId == asset.Id);
            }
            else if (existingDraft is not null)
            {
                existingDraft.BasedOnRevisionId = oldDraftBasedOn;
                existingDraft.EditableRecipe = oldDraftRecipe;
                existingDraft.ModifiedAt = oldDraftModifiedAt!.Value;
            }

            throw;
        }
    }

    public static CompositionRecipe CloneRecipe(CompositionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new CompositionRecipe { Composition = recipe.Composition };
    }
}
