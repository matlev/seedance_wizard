using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

/// <summary>
/// Compatibility commands for the former sequential-segment surface. Placement and
/// reordering are deliberately held until they can supply immutable occurrence evidence.
/// </summary>
internal sealed class CompositionSegmentCommands
{
    private readonly CompositionCurrentAccessor _current;
    private readonly TransactionalCompositionRevisionEditor _editor;

    public CompositionSegmentCommands(CompositionCurrentAccessor current, TransactionalCompositionRevisionEditor editor)
    {
        _current = current;
        _editor = editor;
    }

    public Task<RecipeRevision> AddAsync(Guid sourceAssetId, int? insertionIndex, CancellationToken cancellationToken)
    {
        _ = _current;
        return Task.FromException<RecipeRevision>(CompositionCurrentAccessor.TimingAwarePlacementRequired());
    }

    public Task<RecipeRevision> MoveAsync(Guid segmentId, int offset, CancellationToken cancellationToken)
    {
        _ = _current;
        return Task.FromException<RecipeRevision>(CompositionCurrentAccessor.OccurrenceAdapterRequired("Timeline-item movement"));
    }

    public Task<RecipeRevision> MoveToIndexAsync(Guid segmentId, int targetIndex, CancellationToken cancellationToken)
    {
        _ = _current;
        return Task.FromException<RecipeRevision>(CompositionCurrentAccessor.OccurrenceAdapterRequired("Timeline-item movement"));
    }

    public Task<RecipeRevision> SetAudioEnabledAsync(Guid segmentId, bool audioEnabled, CancellationToken cancellationToken)
    {
        _ = _current;
        return Task.FromException<RecipeRevision>(CompositionCurrentAccessor.OccurrenceAdapterRequired("Linked audio changes"));
    }

    public Task<RecipeRevision> RemoveAsync(Guid segmentId, CancellationToken cancellationToken) =>
        RemoveItemAsync(segmentId, cancellationToken);

    public Task<RecipeRevision> RemoveItemAsync(Guid itemId, CancellationToken cancellationToken) =>
        _editor.UpdateAsync(state => RemoveItem(state, itemId), cancellationToken);

    private static WorkingCompositionState RemoveItem(WorkingCompositionState state, Guid itemId)
    {
        var videoTrack = state.VideoTracks.SingleOrDefault(track => track.Items.Any(item => item.Id == itemId));
        var audioTrack = state.AudioTracks.SingleOrDefault(track => track.Items.Any(item => item.Id == itemId));
        if (videoTrack is null && audioTrack is null)
            throw new InvalidOperationException("The selected composition item no longer exists.");

        var linkGroupId = videoTrack is not null
            ? videoTrack.Items.Single(item => item.Id == itemId).LinkGroupId
            : audioTrack!.Items.Single(item => item.Id == itemId).LinkGroupId;
        var linkedVideoItem = linkGroupId is null ? null : state.VideoTracks
            .SelectMany(track => track.Items)
            .SingleOrDefault(item => item.LinkGroupId == linkGroupId && item.Id != itemId);
        var linkedAudioItem = linkGroupId is null ? null : state.AudioTracks
            .SelectMany(track => track.Items)
            .SingleOrDefault(item => item.LinkGroupId == linkGroupId && item.Id != itemId);
        var linkedVideoTrack = linkedVideoItem is null ? null : state.VideoTracks.Single(track => track.Items.Any(item => item.Id == linkedVideoItem.Id));
        var linkedAudioTrack = linkedAudioItem is null ? null : state.AudioTracks.Single(track => track.Items.Any(item => item.Id == linkedAudioItem.Id));

        if (videoTrack?.IsLocked == true || audioTrack?.IsLocked == true ||
            linkedVideoTrack?.IsLocked == true || linkedAudioTrack?.IsLocked == true)
            throw new InvalidOperationException("Unlock every affected track before removing this timeline item.");

        var ids = new HashSet<Guid> { itemId };
        if (linkedVideoItem is not null) ids.Add(linkedVideoItem.Id);
        if (linkedAudioItem is not null) ids.Add(linkedAudioItem.Id);
        return new WorkingCompositionState(
            state.VideoTracks.Select(track => new CompositionVideoTrack(track.Id, track.IsLocked, track.IsVisible,
                track.Items.Where(item => !ids.Contains(item.Id)), track.Name)),
            state.AudioTracks.Select(track => new CompositionAudioTrack(track.Id, track.IsLocked, track.IsMuted,
                track.Items.Where(item => !ids.Contains(item.Id)), track.Name)));
    }
}
