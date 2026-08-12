using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class GenerationWorkflowTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge workflow tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompletedRemoteJobIsIngestedWithBidirectionalProvenance()
    {
        var (workspace, workflow) = await CreateWorkflowAsync(new SuccessfulIngestionService());
        var provider = new ScriptedAsyncProvider(GenerationProviderCostBehavior.NoCharge);
        var draft = new GenerationDraft
        {
            ProviderId = provider.Capabilities.ProviderId,
            Prompt = "A fox crosses a snowy clearing",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 8,
            AspectRatio = "16:9",
            Resolution = "720p"
        };

        var record = await workflow.RunAsync(
            provider,
            draft,
            authorization: null,
            new GenerationWorkflowOptions { PollInterval = TimeSpan.Zero, PollTimeout = TimeSpan.FromSeconds(5) });

        Assert.Equal(GenerationStatus.Succeeded, record.Status);
        Assert.Equal(OutputIngestionStatus.Succeeded, record.IngestionStatus);
        var outputId = Assert.Single(record.OutputAssetIds);
        var output = Assert.Single(workspace.Project!.Assets);
        Assert.Equal(outputId, output.Id);
        Assert.Equal(record.Id, output.Provenance?.GenerationId);
        Assert.Equal(output.Id, workspace.Project.MainVideoAssetId);
        var store = new PortableProjectStore();
        var (reopened, _) = await store.OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(outputId, Assert.Single(reopened.Generations).OutputAssetIds.Single());
    }

    [Fact]
    public async Task RemoteCompletionRemainsSucceededWhenLocalIngestionFails()
    {
        var (_, workflow) = await CreateWorkflowAsync(new FailingIngestionService());
        var provider = new ScriptedAsyncProvider(GenerationProviderCostBehavior.NoCharge);

        var record = await workflow.RunAsync(
            provider,
            CreateTextDraft(provider),
            authorization: null,
            new GenerationWorkflowOptions { PollInterval = TimeSpan.Zero, PollTimeout = TimeSpan.FromSeconds(5) });

        Assert.Equal(GenerationStatus.Succeeded, record.Status);
        Assert.Equal(OutputIngestionStatus.Failed, record.IngestionStatus);
        Assert.Equal("local_ingestion_failed", record.Error?.ProviderCode);
        Assert.Empty(record.OutputAssetIds);
    }

    [Fact]
    public async Task PotentiallyBillableProviderCannotSubmitWithoutAuthorization()
    {
        var (_, workflow) = await CreateWorkflowAsync(new SuccessfulIngestionService());
        var provider = new ScriptedAsyncProvider(GenerationProviderCostBehavior.PotentiallyBillable);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.RunAsync(provider, CreateTextDraft(provider), authorization: null));

        Assert.Equal(0, provider.SubmitCalls);
    }

    [Fact]
    public async Task SubmitAsyncReturnsAcceptedJobWithoutPollingIt()
    {
        var (_, workflow) = await CreateWorkflowAsync(new SuccessfulIngestionService());
        var provider = new ScriptedAsyncProvider(GenerationProviderCostBehavior.NoCharge);

        var record = await workflow.SubmitAsync(provider, CreateTextDraft(provider), authorization: null);

        Assert.Equal(GenerationStatus.Running, record.Status);
        Assert.Equal("intercepted-job", record.ProviderJobId);
        Assert.Equal(1, provider.SubmitCalls);
        Assert.Equal(0, provider.StatusCalls);
    }

    [Fact]
    public async Task QueuedGenerationDoesNotReachProviderUntilExplicitlySubmitted()
    {
        var (workspace, workflow) = await CreateWorkflowAsync(new SuccessfulIngestionService());
        var provider = new ScriptedAsyncProvider(GenerationProviderCostBehavior.NoCharge);

        var record = await workflow.QueueAsync(provider, CreateTextDraft(provider), authorization: null);

        Assert.Equal(GenerationStatus.Queued, record.Status);
        Assert.Null(record.ProviderJobId);
        Assert.Equal(0, provider.SubmitCalls);
        Assert.Single(workspace.Project!.Generations);

        await workflow.SubmitQueuedAsync(provider, record, authorization: null);

        Assert.Equal(GenerationStatus.Running, record.Status);
        Assert.Equal("intercepted-job", record.ProviderJobId);
        Assert.Equal(1, provider.SubmitCalls);
    }

    [Fact]
    public async Task StoppingLocalMonitoringDoesNotMarkRemoteJobCancelled()
    {
        var (_, workflow) = await CreateWorkflowAsync(new SuccessfulIngestionService());
        var provider = new ScriptedAsyncProvider(GenerationProviderCostBehavior.NoCharge, blockPolling: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var record = await workflow.RunAsync(
            provider,
            CreateTextDraft(provider),
            authorization: null,
            cancellationToken: cancellation.Token);

        Assert.Equal(GenerationStatus.Running, record.Status);
        Assert.Equal("stopped-by-user", record.ResponseMetadata["localMonitoring"]);
        Assert.Null(record.CompletedAt);
    }

    private async Task<(ProjectWorkspace Workspace, GenerationWorkflow Workflow)> CreateWorkflowAsync(
        IGeneratedOutputIngestionService ingestion)
    {
        var store = new PortableProjectStore();
        var inspector = new StubInspector();
        var workspace = new ProjectWorkspace(store, new AssetImportService(inspector));
        await workspace.CreateAsync(_temporaryRoot, "Workflow test");
        var workflow = new GenerationWorkflow(workspace, new UnusedMaterializer(), ingestion);
        return (workspace, workflow);
    }

    private static GenerationDraft CreateTextDraft(IVideoGenerationProvider provider) => new()
    {
        ProviderId = provider.Capabilities.ProviderId,
        Prompt = "A safe intercepted test",
        Mode = GenerationMode.TextToVideo,
        DurationSeconds = 8,
        AspectRatio = "16:9",
        Resolution = "720p"
    };

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class ScriptedAsyncProvider(
        GenerationProviderCostBehavior costBehavior,
        bool blockPolling = false) : IAsyncVideoGenerationProvider
    {
        public int SubmitCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public GenerationProviderCostBehavior CostBehavior => costBehavior;
        public GenerationProviderCapabilities Capabilities { get; } = new(
            "test.provider",
            "Network-isolated test provider",
            "test-model",
            [GenerationMode.TextToVideo],
            4,
            30,
            ["16:9"],
            ["720p"],
            0,
            0,
            0,
            new HashSet<MediaType>(),
            new Dictionary<string, IReadOnlyList<string>>());

        public Task<GenerationSubmission> SubmitAsync(
            GenerationRequest request,
            IReadOnlyCollection<ProjectAsset> projectAssets,
            GenerationSubmissionAuthorization? authorization = null,
            CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            return Task.FromResult(new GenerationSubmission
            {
                ProviderJobId = "intercepted-job",
                Status = GenerationStatus.Running
            });
        }

        public async Task<ProviderGenerationJob> GetJobAsync(
            string providerJobId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            if (blockPolling)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ProviderGenerationJob
            {
                ProviderJobId = providerJobId,
                Status = GenerationStatus.Succeeded,
                Outputs = [new ProviderGenerationOutput("https://output.example/video.mp4")]
            };
        }
    }

    private sealed class SuccessfulIngestionService : IGeneratedOutputIngestionService
    {
        public Task<IReadOnlyList<ProjectAsset>> IngestAsync(
            ProjectLocation location,
            Guid generationId,
            IReadOnlyList<ProviderGenerationOutput> outputs,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ProjectAsset> assets =
            [
                new ProjectAsset
                {
                    FileName = "generated.mp4",
                    DisplayName = "generated.mp4",
                    MediaType = MediaType.Video,
                    Origin = AssetOrigin.Generated,
                    Provenance = new AssetProvenance { Operation = "generation-output", GenerationId = generationId },
                    Physical = new PhysicalAssetStorage
                    {
                        RelativePath = "generated/generated.mp4",
                        Durability = PhysicalAssetDurability.Generated,
                        Availability = PhysicalAssetAvailability.Available,
                        ContentIdentity = new ContentIdentity
                        {
                            Sha256 = new string('a', 64),
                            Status = ContentHashStatus.Verified
                        }
                    }
                }
            ];
            return Task.FromResult(assets);
        }
    }

    private sealed class FailingIngestionService : IGeneratedOutputIngestionService
    {
        public Task<IReadOnlyList<ProjectAsset>> IngestAsync(
            ProjectLocation location,
            Guid generationId,
            IReadOnlyList<ProviderGenerationOutput> outputs,
            CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("Downloaded output was not a valid video.");
    }

    private sealed class UnusedMaterializer : IMediaMaterializer
    {
        public Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Text-to-video should not materialize references.");
    }

    private sealed class StubInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEncodingMetadata { Video = new VideoStreamMetadata() });
    }
}
