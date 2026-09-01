using ReelForge.Application.Editing.Composition;
using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Compatibility surface for the retired sequential-segment split command. Exact split
/// behavior will return with the occurrence adapter; no media is materialized meanwhile.
/// </summary>
public sealed class CompositionSegmentSplitService
{
    private readonly ProjectWorkspace _workspace;

    public CompositionSegmentSplitService(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IExactVideoFrameService exactFrameService)
    {
        _workspace = workspace;
    }

    public Task<CompositionSegmentSplitResult> SplitAsync(
        Guid segmentId,
        TimeSpan offsetWithinSegment,
        AnchorBoundaryEdge boundaryEdge = AnchorBoundaryEdge.BeforeFrame,
        CancellationToken cancellationToken = default)
    {
        _ = _workspace;
        return Task.FromException<CompositionSegmentSplitResult>(
            CompositionCurrentAccessor.OccurrenceAdapterRequired("Splitting"));
    }
}
