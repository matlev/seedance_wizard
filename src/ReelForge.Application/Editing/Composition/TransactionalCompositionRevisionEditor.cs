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
        Action<CompositionRecipe> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var project = _current.Project;
        var (asset, _, currentRecipe) = _current.GetCurrent();
        var recipe = CloneRecipe(currentRecipe);
        update(recipe);

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
            asset.Provenance.SourceAssetIds = recipe.Segments
                .Select(segment => segment.Source.AssetId)
                .Concat(recipe.AudioClips.Select(clip => clip.Source.AssetId))
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

    public static CompositionRecipe CloneRecipe(CompositionRecipe recipe) => new()
    {
        Segments = recipe.Segments.Select(segment => new CompositionSegment
        {
            Id = segment.Id,
            Source = segment.Source with { },
            Start = segment.Start with { },
            End = segment.End with { },
            AudioEnabled = segment.AudioEnabled
        }).ToList(),
        AudioClips = recipe.AudioClips.Select(clip => new CompositionAudioClip
        {
            Id = clip.Id,
            Source = clip.Source with { },
            TimelineStartTicks = clip.TimelineStartTicks,
            IsMuted = clip.IsMuted,
            GainDecibels = clip.GainDecibels,
            Pan = clip.Pan,
            FadeInMilliseconds = clip.FadeInMilliseconds,
            FadeOutMilliseconds = clip.FadeOutMilliseconds
        }).ToList()
    };
}
