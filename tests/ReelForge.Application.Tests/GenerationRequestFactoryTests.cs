using System.Collections.ObjectModel;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class GenerationRequestFactoryTests
{
    [Fact]
    public void CreateSnapshotRejectsDeletedDraftReferenceBeforeARequestCanBeCreated()
    {
        var asset = DeletedAsset();
        var project = new VideoProject { Assets = [asset] };
        var draft = new GenerationDraft
        {
            ProviderId = "test.provider",
            References = [new GenerationReferenceDraft { LogicalObjectId = asset.Id }]
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GenerationRequestFactory.CreateSnapshot(new TestProvider(), draft, project));

        Assert.Equal(
            "'deleted.mp4' was deleted from the project and cannot be submitted as a generation reference.",
            exception.Message);
    }

    [Fact]
    public void CreateProviderRequestRejectsDeletedReferenceFromAnAlreadyQueuedSnapshot()
    {
        var asset = DeletedAsset();
        var snapshot = new GenerationRequestSnapshot
        {
            References =
            [
                new GenerationReferenceSnapshot
                {
                    ObjectKind = GenerationReferenceObjectKind.Asset,
                    LogicalObjectId = asset.Id
                }
            ],
            ProviderParameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>())
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GenerationRequestFactory.CreateProviderRequest(snapshot, [asset]));

        Assert.Equal(
            "'deleted.mp4' was deleted from the project and cannot be submitted as a generation reference.",
            exception.Message);
    }

    [Fact]
    public void SavedFrameBackedByDeletedSourceCannotBeSnapshottedOrPreparedForProviderSubmission()
    {
        var source = DeletedAsset();
        var anchor = new FrameAnchor();
        var revision = new FrameAnchorRevision
        {
            AnchorId = anchor.Id,
            SourceAssetId = source.Id,
            SourceContentHash = new string('a', 64),
            VideoStreamIndex = 0,
            PresentationTimestamp = 1,
            TimeBaseNumerator = 1,
            TimeBaseDenominator = 24
        };
        anchor.CurrentRevisionId = revision.Id;
        var project = new VideoProject { Assets = [source], Anchors = [anchor], AnchorRevisions = [revision] };
        var draft = new GenerationDraft
        {
            ProviderId = "test.provider",
            References =
            [
                new GenerationReferenceDraft
                {
                    ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
                    LogicalObjectId = anchor.Id,
                    AnchorRevisionId = revision.Id
                }
            ]
        };

        var snapshotException = Assert.Throws<InvalidOperationException>(() =>
            GenerationRequestFactory.CreateSnapshot(new TestProvider(), draft, project));

        Assert.Equal(
            "'deleted.mp4' was deleted from the project and cannot be submitted as a generation reference.",
            snapshotException.Message);

        var queuedSnapshot = new GenerationRequestSnapshot
        {
            References =
            [
                new GenerationReferenceSnapshot
                {
                    ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
                    LogicalObjectId = anchor.Id,
                    Anchor = new FrameAnchorReferenceSnapshot { SourceAssetId = source.Id }
                }
            ],
            ProviderParameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>())
        };

        var queuedException = Assert.Throws<InvalidOperationException>(() =>
            GenerationRequestFactory.CreateProviderRequest(queuedSnapshot, [source]));

        Assert.Equal(snapshotException.Message, queuedException.Message);
    }

    private static ProjectAsset DeletedAsset() => new()
    {
        FileName = "deleted.mp4",
        DisplayName = "deleted.mp4",
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        IsDeleted = true,
        Physical = new PhysicalAssetStorage { RelativePath = "deleted.mp4" }
    };

    private sealed class TestProvider : IVideoGenerationProvider
    {
        public GenerationProviderCapabilities Capabilities { get; } = new(
            "test.provider", "Test", "test-model",
            [GenerationMode.ReferenceToVideo], 1, 60,
            ["16:9"], ["720p"], 1, 1, 1,
            new HashSet<MediaType> { MediaType.Video },
            new Dictionary<string, IReadOnlyList<string>>());

        public GenerationProviderCostBehavior CostBehavior => GenerationProviderCostBehavior.NoCharge;

        public Task<GenerationSubmission> SubmitAsync(
            GenerationRequest request,
            IReadOnlyCollection<ProjectAsset> projectAssets,
            GenerationSubmissionAuthorization? authorization = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A rejected reference must never call the provider.");
    }
}
