using System.IO;
using System.Windows.Media.Imaging;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.Generation;

internal sealed class GenerationContinuationCoordinator
{
    private readonly ProjectWorkspace _workspace;
    private readonly ExactVideoFrameService _exactFrameService;
    private readonly IMediaMaterializer _mediaMaterializer;
    private readonly IGenerationContinuationPresentation _presentation;
    private readonly Action<GenerationDraft> _loadDraft;
    private readonly Func<IReadOnlyList<GenerationProviderChoice>> _providerChoices;
    private readonly Func<IVideoGenerationProvider> _currentProvider;

    public GenerationContinuationCoordinator(
        ProjectWorkspace workspace,
        ExactVideoFrameService exactFrameService,
        IMediaMaterializer mediaMaterializer,
        IGenerationContinuationPresentation presentation,
        Action<GenerationDraft> loadDraft,
        Func<IReadOnlyList<GenerationProviderChoice>> providerChoices,
        Func<IVideoGenerationProvider> currentProvider)
    {
        _workspace = workspace;
        _exactFrameService = exactFrameService;
        _mediaMaterializer = mediaMaterializer;
        _presentation = presentation;
        _loadDraft = loadDraft;
        _providerChoices = providerChoices;
        _currentProvider = currentProvider;
    }

    public async Task PrepareAsync(GenerationRecord sourceGeneration, GenerationRelationshipType relationship)
    {
        if (_workspace.Project is not { } project || _workspace.Location is not { } location) return;

        var origin = new ContinuationOrigin(project, location, project.Id);
        var outputs = project.Assets
            .Where(asset => sourceGeneration.OutputAssetIds.Contains(asset.Id))
            .Where(asset => asset.MediaType == MediaType.Video && asset.StorageKind == AssetStorageKind.Physical)
            .ToArray();
        if (outputs.Length == 0)
        {
            if (IsOriginCurrent(origin))
                _presentation.SetStatus("This generation has no durable video output to continue from.");
            return;
        }

        var source = outputs.Length == 1 ? outputs[0] : _presentation.SelectOutput(outputs);
        if (source is null || !IsOriginCurrent(origin)) return;

        await _presentation.RunUiActionAsync("Finding the exact continuation boundary…", async () =>
        {
            var indexedSource = await IndexSourceFramesAsync(origin, source, CancellationToken.None);
            if (indexedSource is null || !IsOriginCurrent(origin)) return;

            var frame = relationship == GenerationRelationshipType.ContinueAfter
                ? indexedSource.Value.Frames[^1]
                : indexedSource.Value.Frames[0];
            await CreateDraftAsync(origin, source, frame, indexedSource.Value.ContentHash, relationship, sourceGeneration);
        });
    }

    private async Task<IndexedSource?> IndexSourceFramesAsync(
        ContinuationOrigin origin,
        ProjectAsset source,
        CancellationToken cancellationToken)
    {
        if (!IsOriginCurrent(origin)) return null;

        await using var materialized = await _mediaMaterializer.MaterializeAsync(
            origin.Project,
            origin.Location,
            new MaterializationRequest(
                new AssetMaterializationTarget(source.Id),
                MaterializationPurpose.Preview),
            cancellationToken);
        if (!IsOriginCurrent(origin)) return null;

        var contentHash = materialized.ContentIdentity.Sha256
            ?? throw new InvalidDataException("The continuation source has no verified content identity.");
        var frames = await _exactFrameService.IndexAsync(materialized.Path, cancellationToken);
        if (!IsOriginCurrent(origin)) return null;

        await SaveOriginAsync(origin, cancellationToken);
        return IsOriginCurrent(origin) ? new IndexedSource(frames, contentHash) : null;
    }

    private async Task CreateDraftAsync(
        ContinuationOrigin origin,
        ProjectAsset source,
        VideoPresentationFrame frame,
        string contentHash,
        GenerationRelationshipType relationship,
        GenerationRecord parent)
    {
        if (!IsOriginCurrent(origin) || source.Physical is null) return;

        var transientRevision = TransientFrameAnchorRevisionFactory.Create(source.Id, contentHash, frame);
        var sourcePath = ProjectPathPolicy.ResolveContainedPath(origin.Location, source.Physical.RelativePath);
        await using var preview = await _exactFrameService.ExtractAsync(
            sourcePath,
            contentHash,
            transientRevision,
            MaterializationPurpose.Preview,
            "continuation-confirmation");
        if (!IsOriginCurrent(origin)) return;

        var heading = relationship == GenerationRelationshipType.ContinueAfter
            ? "Continue after this exact frame?"
            : "Continue before this exact frame?";
        if (!_presentation.ConfirmFrame(
                _presentation.LoadBitmap(preview.Path),
                heading,
                source.EffectiveDisplayName,
                frame.TimestampSeconds,
                frame.PresentationTimestamp,
                frame.TimeBaseNumerator,
                frame.TimeBaseDenominator) ||
            !IsOriginCurrent(origin)) return;

        var anchor = new FrameAnchor
        {
            DisplayLabel = relationship == GenerationRelationshipType.ContinueAfter
                ? $"Final frame of {source.EffectiveDisplayName}"
                : $"First frame of {source.EffectiveDisplayName}"
        };
        origin.Project.Anchors.Add(anchor);
        var revision = origin.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            source.Id,
            contentHash,
            frame.VideoStreamIndex,
            frame.PresentationTimestamp,
            frame.TimeBaseNumerator,
            frame.TimeBaseDenominator,
            frame.FrameNumber));

        var draft = GenerationWorkflow.CreateDerivedDraft(parent, relationship);
        draft.References =
        [
            new GenerationReferenceDraft
            {
                ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
                LogicalObjectId = anchor.Id,
                AnchorRevisionId = revision.Id,
                Role = relationship == GenerationRelationshipType.ContinueAfter
                    ? GenerationReferenceRole.StartFrame
                    : GenerationReferenceRole.EndFrame,
                Order = 0,
                Label = anchor.DisplayLabel
            }
        ];
        RecommendMode(draft, relationship);
        if (!IsOriginCurrent(origin)) return;

        draft.ModifiedAt = DateTimeOffset.UtcNow;
        origin.Project.CurrentGenerationDraft = draft;
        await SaveOriginAsync(origin, CancellationToken.None);
        if (!IsOriginCurrent(origin)) return;

        _presentation.RefreshProjectCollections();
        if (!IsOriginCurrent(origin)) return;
        _loadDraft(draft);
        if (!IsOriginCurrent(origin)) return;
        if (_presentation.HasCurrentFrameSource(source.Id))
        {
            await _presentation.RefreshSavedFramesAsync();
            if (!IsOriginCurrent(origin)) return;
        }
        _presentation.SelectGenerateTab();
        if (!IsOriginCurrent(origin)) return;
        _presentation.SetStatus(
            $"Drafted {relationship} from generation {parent.Id}. Review the exact Saved Frame reference before submitting.");
    }

    private async Task SaveOriginAsync(ContinuationOrigin origin, CancellationToken cancellationToken)
    {
        if (!IsOriginCurrent(origin)) return;
        _ = await _workspace
            .SaveIfCurrentAsync(origin.Project, origin.Location, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsOriginCurrent(ContinuationOrigin origin) =>
        ReferenceEquals(_workspace.Project, origin.Project) &&
        _workspace.Project?.Id == origin.ProjectId &&
        _workspace.Location is { } currentLocation &&
        string.Equals(currentLocation.ProjectFilePath, origin.Location.ProjectFilePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(currentLocation.RootDirectory, origin.Location.RootDirectory, StringComparison.OrdinalIgnoreCase);

    private void RecommendMode(GenerationDraft draft, GenerationRelationshipType relationship)
    {
        var provider = _providerChoices()
                           .FirstOrDefault(choice => choice.Provider.Capabilities.ProviderId.Equals(
                               draft.ProviderId,
                               StringComparison.Ordinal))
                           ?.Provider
                       ?? _currentProvider();
        if (relationship == GenerationRelationshipType.ContinueBefore &&
            provider.Capabilities.Modes.Contains(GenerationMode.ReferenceToVideo))
        {
            draft.Mode = GenerationMode.ReferenceToVideo;
            return;
        }
        if (provider.Capabilities.Modes.Contains(GenerationMode.ImageToVideo))
        {
            draft.Mode = GenerationMode.ImageToVideo;
            if (provider.Capabilities.AspectRatios.Contains("adaptive", StringComparer.OrdinalIgnoreCase))
            {
                draft.AspectRatio = provider.Capabilities.AspectRatios.First(ratio => ratio.Equals(
                    "adaptive",
                    StringComparison.OrdinalIgnoreCase));
            }
            return;
        }
        if (provider.Capabilities.Modes.Contains(GenerationMode.ReferenceToVideo))
        {
            draft.Mode = GenerationMode.ReferenceToVideo;
            return;
        }
        throw new InvalidOperationException(
            $"{provider.Capabilities.DisplayName} does not support a continuation-compatible image reference mode.");
    }

    private readonly record struct ContinuationOrigin(VideoProject Project, ProjectLocation Location, Guid ProjectId);
    private readonly record struct IndexedSource(IReadOnlyList<VideoPresentationFrame> Frames, string ContentHash);
}

internal interface IGenerationContinuationPresentation
{
    ProjectAsset? SelectOutput(IReadOnlyList<ProjectAsset> outputs);
    bool ConfirmFrame(BitmapSource bitmap, string heading, string sourceName, double timestampSeconds,
        long presentationTimestamp, int timeBaseNumerator, int timeBaseDenominator);
    BitmapSource LoadBitmap(string path);
    Task RunUiActionAsync(string status, Func<Task> action);
    void RefreshProjectCollections();
    bool HasCurrentFrameSource(Guid assetId);
    Task RefreshSavedFramesAsync();
    void SelectGenerateTab();
    void SetStatus(string status);
}
