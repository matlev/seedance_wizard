using ReelForge.Application.Editing.Audio;
using ReelForge.Application.Editing.Composition;
using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Compatibility facade for Working Composition editing commands.
/// </summary>
public sealed class WorkingCompositionService
{
    private readonly CompositionCurrentAccessor _current;
    private readonly CompositionLifecycleCreator _lifecycle;
    private readonly CompositionSegmentCommands _segments;
    private readonly CompositionAudioCommands _audio;
    private readonly CompositionSplitMutation _split;

    public WorkingCompositionService(ProjectWorkspace workspace)
    {
        _current = new CompositionCurrentAccessor(workspace);

        var editor = new TransactionalCompositionRevisionEditor(workspace, _current);
        _lifecycle = new CompositionLifecycleCreator(workspace, _current);
        _segments = new CompositionSegmentCommands(_current, editor);
        _audio = new CompositionAudioCommands(_current, editor);
        _split = new CompositionSplitMutation(_current, editor);
    }

    public async Task<ProjectAsset> CreateInitialAsync(
        Guid sourceAssetId,
        CancellationToken cancellationToken = default) =>
        await _lifecycle.CreateInitialAsync(sourceAssetId, cancellationToken);

    public async Task<RecipeRevision> AddSegmentAsync(
        Guid sourceAssetId,
        CancellationToken cancellationToken = default) =>
        await _segments.AddAsync(sourceAssetId, null, cancellationToken);

    public async Task<RecipeRevision> AddSegmentAsync(
        Guid sourceAssetId,
        int? insertionIndex,
        CancellationToken cancellationToken = default) =>
        await _segments.AddAsync(sourceAssetId, insertionIndex, cancellationToken);

    public async Task<RecipeRevision> AddAudioClipAsync(
        Guid sourceAssetId,
        TimeSpan timelineStart,
        CancellationToken cancellationToken = default) =>
        await _audio.AddAsync(sourceAssetId, timelineStart, cancellationToken);

    public async Task<RecipeRevision> MoveSegmentAsync(
        Guid segmentId,
        int offset,
        CancellationToken cancellationToken = default) =>
        await _segments.MoveAsync(segmentId, offset, cancellationToken);

    public async Task<RecipeRevision> MoveSegmentToIndexAsync(
        Guid segmentId,
        int targetIndex,
        CancellationToken cancellationToken = default) =>
        await _segments.MoveToIndexAsync(segmentId, targetIndex, cancellationToken);

    public async Task<RecipeRevision> SetSegmentAudioEnabledAsync(
        Guid segmentId,
        bool audioEnabled,
        CancellationToken cancellationToken = default) =>
        await _segments.SetAudioEnabledAsync(segmentId, audioEnabled, cancellationToken);

    public async Task<CompositionAudioDetachmentResult> AddDetachedSegmentAudioAsync(
        Guid segmentId,
        Guid audioAssetId,
        TimeSpan timelineStart,
        CancellationToken cancellationToken = default) =>
        await _audio.AddDetachedAsync(segmentId, audioAssetId, timelineStart, cancellationToken);

    public async Task<CompositionSegmentSplitResult> SplitSegmentAtFrameAsync(
        Guid segmentId,
        ExactFramePosition position,
        AnchorBoundaryEdge boundaryEdge,
        double boundaryTimestampSeconds,
        CancellationToken cancellationToken = default) =>
        await _split.SplitAsync(
            segmentId,
            position,
            boundaryEdge,
            boundaryTimestampSeconds,
            cancellationToken);

    public async Task<RecipeRevision> SetAudioClipTimelineStartAsync(
        Guid audioClipId,
        TimeSpan timelineStart,
        CancellationToken cancellationToken = default) =>
        await _audio.SetTimelineStartAsync(audioClipId, timelineStart, cancellationToken);

    public async Task<RecipeRevision> SetAudioClipMixAsync(
        Guid audioClipId,
        bool isMuted,
        double gainDecibels,
        CancellationToken cancellationToken = default) =>
        await _audio.SetMixAsync(audioClipId, isMuted, gainDecibels, cancellationToken);

    public async Task<RecipeRevision> SetAudioClipFadesAsync(
        Guid audioClipId,
        TimeSpan fadeIn,
        TimeSpan fadeOut,
        CancellationToken cancellationToken = default) =>
        await _audio.SetFadesAsync(audioClipId, fadeIn, fadeOut, cancellationToken);

    public async Task<RecipeRevision> SetAudioClipPanAsync(
        Guid audioClipId,
        double pan,
        CancellationToken cancellationToken = default) =>
        await _audio.SetPanAsync(audioClipId, pan, cancellationToken);

    public async Task<RecipeRevision> RemoveSegmentAsync(
        Guid segmentId,
        CancellationToken cancellationToken = default) =>
        await _segments.RemoveAsync(segmentId, cancellationToken);

    public async Task<RecipeRevision> RemoveItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        await _segments.RemoveItemAsync(itemId, cancellationToken);

    public (ProjectAsset Asset, RecipeRevision Revision, CompositionRecipe Recipe) GetCurrent() =>
        _current.GetCurrent();
}

public sealed record CompositionSegmentSplitResult(
    RecipeRevision Revision,
    Guid LeadingSegmentId,
    Guid TrailingSegmentId,
    Guid LeadingClipAssetId,
    Guid TrailingClipAssetId,
    Guid BoundaryAnchorId,
    Guid BoundaryAnchorRevisionId,
    double SourceTimestampSeconds);

public sealed record CompositionAudioDetachmentResult(
    RecipeRevision Revision,
    Guid AudioClipId);
