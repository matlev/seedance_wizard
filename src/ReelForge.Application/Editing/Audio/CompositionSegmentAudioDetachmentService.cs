using ReelForge.Application.Editing.Composition;
using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Compatibility surface for exact-occurrence audio detach. The legacy command derived a
/// floating timeline start and therefore must not create media or mutate a composition.
/// </summary>
public sealed class CompositionSegmentAudioDetachmentService
{
    private readonly ProjectWorkspace _workspace;

    public CompositionSegmentAudioDetachmentService(
        ProjectWorkspace workspace,
        ICompositionSegmentMaterializer segmentMaterializer,
        IAudioExtractionEngine extractionEngine,
        IContentHashService contentHashService,
        IMediaInspectionService mediaInspector)
    {
        _workspace = workspace;
    }

    public Task<DetachedCompositionAudioResult> DetachAsync(
        Guid segmentId,
        string requestedFileName,
        CancellationToken cancellationToken = default)
    {
        _ = _workspace;
        return Task.FromException<DetachedCompositionAudioResult>(
            CompositionCurrentAccessor.OccurrenceAdapterRequired("Audio detachment"));
    }
}

public sealed record DetachedCompositionAudioResult(
    ProjectAsset AudioAsset,
    RecipeRevision CompositionRevision,
    Guid AudioClipId,
    TimeSpan TimelineStart);
