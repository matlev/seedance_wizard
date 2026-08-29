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
        var location = _workspace.Location ?? throw new InvalidOperationException("Open a project first.");
        var result = await UpdateIfCurrentAsync(
            project,
            location,
            transform,
            applyAdditionalMutation: null,
            rollbackAdditionalMutation: null,
            cancellationToken).ConfigureAwait(false);
        if (result.Failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(result.Failure).Throw();
        if (!result.Committed)
            throw new OperationCanceledException(
                "The composition edit did not commit because the active project changed or the operation was cancelled.",
                cancellationToken);
        return result.Revision!;
    }

    public async Task<CompositionRevisionUpdateResult> UpdateIfCurrentAsync(
        VideoProject project,
        ProjectLocation location,
        Func<WorkingCompositionState, WorkingCompositionState> transform,
        Action? applyAdditionalMutation,
        Action? rollbackAdditionalMutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(transform);

        ProjectAsset? asset = null;
        var revisionCount = 0;
        var oldProjectModifiedAt = default(DateTimeOffset);
        Guid? oldCurrentRevisionId = null;
        List<Guid>? oldSources = null;
        var provenanceExisted = false;
        RecipeDraft? existingDraft = null;
        Guid? oldDraftBasedOn = null;
        AssetRecipe? oldDraftRecipe = null;
        var oldDraftModifiedAt = default(DateTimeOffset?);
        var mutationStarted = false;
        RecipeRevision? revision = null;

        void Apply()
        {
            var current = CompositionCurrentAccessor.GetCurrent(project);
            asset = current.Asset;
            var state = transform(current.Recipe.Composition)
                ?? throw new InvalidOperationException("A Working Composition transform must return a composition state.");
            var recipe = new CompositionRecipe { Composition = state };

            revisionCount = project.RecipeRevisions.Count;
            oldCurrentRevisionId = asset.Virtual!.CurrentRecipeRevisionId;
            provenanceExisted = asset.Provenance is not null;
            oldSources = asset.Provenance?.SourceAssetIds.ToList() ?? [];
            existingDraft = project.RecipeDrafts.SingleOrDefault(draft => draft.VirtualAssetId == asset.Id);
            oldDraftBasedOn = existingDraft?.BasedOnRevisionId;
            oldDraftRecipe = existingDraft?.EditableRecipe;
            oldDraftModifiedAt = existingDraft?.ModifiedAt;
            mutationStarted = true;

            applyAdditionalMutation?.Invoke();
            revision = project.CommitRecipe(asset.Id, recipe);
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
        }

        Task RollbackAsync()
        {
            if (!mutationStarted)
                return Task.CompletedTask;

            project.RecipeRevisions.RemoveRange(
                revisionCount,
                project.RecipeRevisions.Count - revisionCount);
            project.ModifiedAt = oldProjectModifiedAt;
            asset!.Virtual!.CurrentRecipeRevisionId = oldCurrentRevisionId;
            if (!provenanceExisted)
                asset.Provenance = null;
            else if (asset.Provenance is not null)
                asset.Provenance.SourceAssetIds = oldSources!;

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

            rollbackAdditionalMutation?.Invoke();
            return Task.CompletedTask;
        }

        var saved = await _workspace.SaveMutationIfCurrentWithSnapshotAsync(
            project,
            location,
            () => oldProjectModifiedAt = project.ModifiedAt,
            Apply,
            RollbackAsync,
            cancellationToken).ConfigureAwait(false);
        return saved.Committed
            ? new CompositionRevisionUpdateResult(true, revision, null)
            : new CompositionRevisionUpdateResult(false, null, saved.Failure);
    }

    public static CompositionRecipe CloneRecipe(CompositionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new CompositionRecipe { Composition = recipe.Composition };
    }
}

internal sealed record CompositionRevisionUpdateResult(
    bool Committed,
    RecipeRevision? Revision,
    Exception? Failure);
