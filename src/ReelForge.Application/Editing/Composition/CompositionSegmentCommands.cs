using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

internal sealed class CompositionSegmentCommands
{
    private readonly CompositionCurrentAccessor _current;
    private readonly TransactionalCompositionRevisionEditor _editor;

    public CompositionSegmentCommands(
        CompositionCurrentAccessor current,
        TransactionalCompositionRevisionEditor editor)
    {
        _current = current;
        _editor = editor;
    }

    public Task<RecipeRevision> AddAsync(Guid sourceAssetId, int? insertionIndex, CancellationToken cancellationToken) =>
        _editor.UpdateAsync(recipe =>
        {
            var segment = CompositionCurrentAccessor.CreateSegment(_current.RequireVideoSource(sourceAssetId));
            var index = Math.Clamp(insertionIndex ?? recipe.Segments.Count, 0, recipe.Segments.Count);
            recipe.Segments.Insert(index, segment);
        }, cancellationToken);

    public Task<RecipeRevision> MoveAsync(Guid segmentId, int offset, CancellationToken cancellationToken)
    {
        if (offset is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(offset), "A composition segment can move one position at a time.");

        return _editor.UpdateAsync(recipe =>
        {
            var index = recipe.Segments.FindIndex(segment => segment.Id == segmentId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition segment no longer exists.");
            var target = index + offset;
            if (target < 0 || target >= recipe.Segments.Count)
                return;
            (recipe.Segments[index], recipe.Segments[target]) = (recipe.Segments[target], recipe.Segments[index]);
        }, cancellationToken);
    }

    public Task<RecipeRevision> MoveToIndexAsync(Guid segmentId, int targetIndex, CancellationToken cancellationToken)
    {
        var (_, revision, recipe) = _current.GetCurrent();
        var currentIndex = recipe.Segments.FindIndex(segment => segment.Id == segmentId);
        if (currentIndex < 0)
            throw new InvalidOperationException("The selected composition segment no longer exists.");
        var boundedTarget = Math.Clamp(targetIndex, 0, recipe.Segments.Count - 1);
        if (currentIndex == boundedTarget)
            return Task.FromResult(revision);

        return _editor.UpdateAsync(candidate =>
        {
            var index = candidate.Segments.FindIndex(segment => segment.Id == segmentId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition segment no longer exists.");
            var segment = candidate.Segments[index];
            candidate.Segments.RemoveAt(index);
            candidate.Segments.Insert(boundedTarget, segment);
        }, cancellationToken);
    }

    public Task<RecipeRevision> SetAudioEnabledAsync(Guid segmentId, bool audioEnabled, CancellationToken cancellationToken)
    {
        var (_, revision, recipe) = _current.GetCurrent();
        var currentSegment = recipe.Segments.SingleOrDefault(segment => segment.Id == segmentId)
            ?? throw new InvalidOperationException("The selected composition segment no longer exists.");
        if (currentSegment.AudioEnabled == audioEnabled)
            return Task.FromResult(revision);

        return _editor.UpdateAsync(candidate =>
        {
            var index = candidate.Segments.FindIndex(segment => segment.Id == segmentId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition segment no longer exists.");
            candidate.Segments[index] = candidate.Segments[index] with { AudioEnabled = audioEnabled };
        }, cancellationToken);
    }

    public Task<RecipeRevision> RemoveAsync(Guid segmentId, CancellationToken cancellationToken) =>
        _editor.UpdateAsync(recipe =>
        {
            if (recipe.Segments.Count == 1)
                throw new InvalidOperationException("A Working Composition must contain at least one segment.");
            if (recipe.Segments.RemoveAll(segment => segment.Id == segmentId) == 0)
                throw new InvalidOperationException("The selected composition segment no longer exists.");
        }, cancellationToken);

    public Task<RecipeRevision> RemoveItemAsync(Guid itemId, CancellationToken cancellationToken) =>
        _editor.UpdateAsync(recipe =>
        {
            if (recipe.AudioClips.RemoveAll(clip => clip.Id == itemId) > 0)
                return;
            if (recipe.Segments.Count == 1)
                throw new InvalidOperationException("A Working Composition must contain at least one video segment.");
            if (recipe.Segments.RemoveAll(segment => segment.Id == itemId) == 0)
                throw new InvalidOperationException("The selected composition item no longer exists.");
        }, cancellationToken);
}
