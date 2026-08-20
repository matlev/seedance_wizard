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

    public Task<RecipeRevision> AddSegmentAsync(
        Guid sourceAssetId,
        CancellationToken cancellationToken = default) =>
        AddSegmentAsync(sourceAssetId, insertionIndex: null, cancellationToken);

    public async Task<RecipeRevision> AddSegmentAsync(
        Guid sourceAssetId,
        int? insertionIndex,
        CancellationToken cancellationToken = default) =>
        await UpdateAsync(recipe =>
        {
            var segment = CreateSegment(RequireVideoSource(sourceAssetId));
            var index = Math.Clamp(insertionIndex ?? recipe.Segments.Count, 0, recipe.Segments.Count);
            recipe.Segments.Insert(index, segment);
        }, cancellationToken).ConfigureAwait(false);

    public async Task<RecipeRevision> AddAudioClipAsync(
        Guid sourceAssetId,
        TimeSpan timelineStart,
        CancellationToken cancellationToken = default)
    {
        var normalizedStart = NormalizeAudioTimelineStart(timelineStart);
        return await UpdateAsync(recipe =>
        {
            var source = RequireAudioSource(sourceAssetId);
            recipe.AudioClips.Add(new CompositionAudioClip
            {
                Source = new AssetRevisionReference { AssetId = source.Id },
                TimelineStartTicks = normalizedStart.Ticks
            });
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecipeRevision> MoveSegmentAsync(
        Guid segmentId,
        int offset,
        CancellationToken cancellationToken = default)
    {
        if (offset is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(offset), "A composition segment can move one position at a time.");
        return await UpdateAsync(recipe =>
        {
            var segments = recipe.Segments;
            var index = segments.FindIndex(segment => segment.Id == segmentId);
            if (index < 0) throw new InvalidOperationException("The selected composition segment no longer exists.");
            var target = index + offset;
            if (target < 0 || target >= segments.Count) return;
            (segments[index], segments[target]) = (segments[target], segments[index]);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecipeRevision> MoveSegmentToIndexAsync(
        Guid segmentId,
        int targetIndex,
        CancellationToken cancellationToken = default)
    {
        var (_, currentRevision, currentRecipe) = GetCurrent();
        var currentIndex = currentRecipe.Segments.FindIndex(segment => segment.Id == segmentId);
        if (currentIndex < 0)
            throw new InvalidOperationException("The selected composition segment no longer exists.");

        var boundedTarget = Math.Clamp(targetIndex, 0, currentRecipe.Segments.Count - 1);
        if (currentIndex == boundedTarget) return currentRevision;

        return await UpdateAsync(recipe =>
        {
            var index = recipe.Segments.FindIndex(segment => segment.Id == segmentId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition segment no longer exists.");
            var segment = recipe.Segments[index];
            recipe.Segments.RemoveAt(index);
            recipe.Segments.Insert(boundedTarget, segment);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecipeRevision> SetSegmentAudioEnabledAsync(
        Guid segmentId,
        bool audioEnabled,
        CancellationToken cancellationToken = default)
    {
        var (_, currentRevision, currentRecipe) = GetCurrent();
        var currentSegment = currentRecipe.Segments.SingleOrDefault(segment => segment.Id == segmentId)
            ?? throw new InvalidOperationException("The selected composition segment no longer exists.");
        if (currentSegment.AudioEnabled == audioEnabled) return currentRevision;

        return await UpdateAsync(recipe =>
        {
            var index = recipe.Segments.FindIndex(segment => segment.Id == segmentId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition segment no longer exists.");
            recipe.Segments[index] = recipe.Segments[index] with { AudioEnabled = audioEnabled };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecipeRevision> SetAudioClipTimelineStartAsync(
        Guid audioClipId,
        TimeSpan timelineStart,
        CancellationToken cancellationToken = default)
    {
        var normalizedStart = NormalizeAudioTimelineStart(timelineStart);
        var (_, currentRevision, currentRecipe) = GetCurrent();
        var currentClip = currentRecipe.AudioClips.SingleOrDefault(clip => clip.Id == audioClipId)
            ?? throw new InvalidOperationException("The selected composition audio clip no longer exists.");
        if (currentClip.TimelineStartTicks == normalizedStart.Ticks) return currentRevision;

        return await UpdateAsync(recipe =>
        {
            var index = recipe.AudioClips.FindIndex(clip => clip.Id == audioClipId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition audio clip no longer exists.");
            recipe.AudioClips[index] = recipe.AudioClips[index] with
            {
                TimelineStartTicks = normalizedStart.Ticks
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    private static TimeSpan NormalizeAudioTimelineStart(TimeSpan timelineStart)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timelineStart, TimeSpan.Zero);
        var milliseconds = Math.Round(timelineStart.TotalMilliseconds, MidpointRounding.AwayFromZero);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public async Task<RecipeRevision> SetAudioClipMixAsync(
        Guid audioClipId,
        bool isMuted,
        double gainDecibels,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(gainDecibels) || gainDecibels is < -60 or > 12)
            throw new ArgumentOutOfRangeException(
                nameof(gainDecibels),
                "Audio gain must be between -60 dB and +12 dB.");
        var (_, currentRevision, currentRecipe) = GetCurrent();
        var currentClip = currentRecipe.AudioClips.SingleOrDefault(clip => clip.Id == audioClipId)
            ?? throw new InvalidOperationException("The selected composition audio clip no longer exists.");
        if (currentClip.IsMuted == isMuted && currentClip.GainDecibels.Equals(gainDecibels))
            return currentRevision;

        return await UpdateAsync(recipe =>
        {
            var index = recipe.AudioClips.FindIndex(clip => clip.Id == audioClipId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition audio clip no longer exists.");
            recipe.AudioClips[index] = recipe.AudioClips[index] with
            {
                IsMuted = isMuted,
                GainDecibels = gainDecibels
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecipeRevision> RemoveSegmentAsync(
        Guid segmentId,
        CancellationToken cancellationToken = default) =>
        await UpdateAsync(recipe =>
        {
            var segments = recipe.Segments;
            if (segments.Count == 1)
                throw new InvalidOperationException("A Working Composition must contain at least one segment.");
            if (segments.RemoveAll(segment => segment.Id == segmentId) == 0)
                throw new InvalidOperationException("The selected composition segment no longer exists.");
        }, cancellationToken).ConfigureAwait(false);

    public async Task<RecipeRevision> RemoveItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        await UpdateAsync(recipe =>
        {
            if (recipe.AudioClips.RemoveAll(clip => clip.Id == itemId) > 0) return;
            if (recipe.Segments.Count == 1)
                throw new InvalidOperationException("A Working Composition must contain at least one video segment.");
            if (recipe.Segments.RemoveAll(segment => segment.Id == itemId) == 0)
                throw new InvalidOperationException("The selected composition item no longer exists.");
        }, cancellationToken).ConfigureAwait(false);

    public (ProjectAsset Asset, RecipeRevision Revision, CompositionRecipe Recipe) GetCurrent()
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
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

    private async Task<RecipeRevision> UpdateAsync(
        Action<CompositionRecipe> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var (asset, _, currentRecipe) = GetCurrent();
        var recipe = CloneRecipe(currentRecipe);
        update(recipe);

        var revisionCount = project.RecipeRevisions.Count;
        var oldCurrentRevisionId = asset.Virtual!.CurrentRecipeRevisionId;
        var oldSources = asset.Provenance?.SourceAssetIds.ToList() ?? [];
        var existingDraft = project.RecipeDrafts.SingleOrDefault(draft => draft.VirtualAssetId == asset.Id);
        var oldDraftBasedOn = existingDraft?.BasedOnRevisionId;
        var oldDraftRecipe = existingDraft?.EditableRecipe;
        try
        {
            var revision = project.CommitRecipe(asset.Id, recipe);
            asset.Provenance ??= new AssetProvenance { Operation = "working-composition" };
            asset.Provenance.SourceAssetIds = recipe.Segments.Select(segment => segment.Source.AssetId)
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
            project.RecipeRevisions.RemoveRange(revisionCount, project.RecipeRevisions.Count - revisionCount);
            asset.Virtual!.CurrentRecipeRevisionId = oldCurrentRevisionId;
            if (asset.Provenance is not null) asset.Provenance.SourceAssetIds = oldSources;
            if (oldDraftRecipe is null)
            {
                project.RecipeDrafts.RemoveAll(draft => draft.VirtualAssetId == asset.Id);
            }
            else if (existingDraft is not null)
            {
                existingDraft.BasedOnRevisionId = oldDraftBasedOn;
                existingDraft.EditableRecipe = oldDraftRecipe;
            }
            throw;
        }
    }

    private ProjectAsset RequireVideoSource(Guid sourceAssetId)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var source = project.Assets.SingleOrDefault(asset => asset.Id == sourceAssetId)
            ?? throw new InvalidOperationException("The selected composition source no longer exists.");
        if (source.Id == project.WorkingCompositionAssetId)
            throw new InvalidOperationException("A Working Composition cannot contain itself.");
        if (source.MediaType != MediaType.Video ||
            (source.StorageKind == AssetStorageKind.Virtual && source.Virtual?.Kind != VirtualAssetKind.SavedClip))
            throw new InvalidOperationException("Add a physical video or Saved Clip to the Working Composition.");
        return source;
    }

    private ProjectAsset RequireAudioSource(Guid sourceAssetId)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var source = project.Assets.SingleOrDefault(asset => asset.Id == sourceAssetId)
            ?? throw new InvalidOperationException("The selected audio source no longer exists.");
        if (source.StorageKind != AssetStorageKind.Physical || source.MediaType != MediaType.Audio)
            throw new InvalidOperationException("Add a physical audio file to the Working Composition.");
        return source;
    }

    private static CompositionSegment CreateSegment(ProjectAsset source) => new()
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

    private static CompositionSegment CloneSegment(CompositionSegment segment) => new()
    {
        Id = segment.Id,
        Source = segment.Source with { },
        Start = segment.Start with { },
        End = segment.End with { },
        AudioEnabled = segment.AudioEnabled
    };

    private static CompositionRecipe CloneRecipe(CompositionRecipe recipe) => new()
    {
        Segments = recipe.Segments.Select(CloneSegment).ToList(),
        AudioClips = recipe.AudioClips.Select(clip => new CompositionAudioClip
        {
            Id = clip.Id,
            Source = clip.Source with { },
            TimelineStartTicks = clip.TimelineStartTicks,
            IsMuted = clip.IsMuted,
            GainDecibels = clip.GainDecibels
        }).ToList()
    };
}
