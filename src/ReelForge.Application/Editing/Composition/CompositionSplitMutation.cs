using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

/// <summary>
/// Retains the public split boundary while the former floating-second segment model is
/// replaced by exact occurrence adapters. It intentionally mutates nothing.
/// </summary>
internal sealed class CompositionSplitMutation
{
    private readonly CompositionCurrentAccessor _current;

    public CompositionSplitMutation(CompositionCurrentAccessor current, TransactionalCompositionRevisionEditor editor)
    {
        _current = current;
    }

    public Task<CompositionSegmentSplitResult> SplitAsync(
        Guid segmentId,
        ExactFramePosition position,
        AnchorBoundaryEdge boundaryEdge,
        double boundaryTimestampSeconds,
        CancellationToken cancellationToken)
    {
        _ = _current;
        return Task.FromException<CompositionSegmentSplitResult>(
            CompositionCurrentAccessor.OccurrenceAdapterRequired("Splitting"));
    }
}
