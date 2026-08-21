using System.Net;
using System.Security.Cryptography;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class OfflineGenerationAcceptanceTests : IDisposable
{
    private const string FixtureSha256 = "3F2F8F6AF7B559441724BF1F3F9532F9D79017049E174E173679811F30CB9FC8";
    private const string OutputUrl = "https://fixtures.reelforge.test/test_video.mp4";
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge offline generation acceptance tests",
        Guid.NewGuid().ToString("N"));

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "test_video.mp4");

    [Fact]
    public async Task SuccessfulReferenceGenerationDownloadsFixtureAndFreezesHistory()
    {
        var context = await CreateReferenceWorkflowAsync();
        var provider = ScriptedFixtureProvider.ForSuccessfulSubmission();
        var draft = CreateReferenceDraft(provider, context.Reference.Id);

        var generation = await context.Workflow.RunAsync(
            provider,
            draft,
            TestAuthorization(provider),
            new GenerationWorkflowOptions { PollInterval = TimeSpan.Zero, PollTimeout = TimeSpan.FromSeconds(5) });
        draft.Prompt = "Mutated after submission";
        draft.References[0].Label = "mutated label";
        draft.References[0].Notes = "mutated notes";

        Assert.Equal(1, provider.SubmitCalls);
        Assert.True(provider.StatusCalls >= 2);
        Assert.NotNull(provider.SubmittedRequest);
        Assert.Equal(context.Reference.Id, Assert.Single(provider.SubmittedRequest.ReferenceAssetIds));
        var preparedReference = Assert.Single(provider.SubmittedRequest.PreparedReferences);
        Assert.Equal(context.Reference.Id, preparedReference.LogicalObjectId);
        Assert.Equal("https://uploads.reelforge.test/reference.mp4", preparedReference.ProviderRepresentation);
        Assert.Equal(1, context.Preparation.CallCount);
        Assert.Equal(FixtureSha256, context.Preparation.LogicalReference?.ContentHash, ignoreCase: true);

        Assert.Equal(GenerationStatus.Succeeded, generation.Status);
        Assert.Equal(OutputIngestionStatus.Succeeded, generation.IngestionStatus);
        Assert.Equal("Original fixture reference prompt", generation.RequestSnapshot.Prompt);
        var frozenReference = Assert.Single(generation.RequestSnapshot.References);
        Assert.Equal(context.Reference.Id, frozenReference.LogicalObjectId);
        Assert.Equal(GenerationReferenceRole.Motion, frozenReference.Role);
        Assert.Equal(4, frozenReference.Order);
        Assert.Equal("Motion guide", frozenReference.Label);
        Assert.Equal("Use the movement and timing from this clip.", frozenReference.Notes);
        Assert.Equal("mock-upload", generation.ResponseMetadata[$"reference.{frozenReference.ReferenceId:N}.preparation"]);

        var outputId = Assert.Single(generation.OutputAssetIds);
        var output = context.Workspace.Project!.Assets.Single(asset => asset.Id == outputId);
        Assert.Equal(AssetOrigin.Generated, output.Origin);
        Assert.Equal(generation.Id, output.Provenance?.GenerationId);
        Assert.Equal(FixtureSha256, output.Physical?.ContentIdentity.Sha256, ignoreCase: true);
        Assert.Equal(10.125, output.DurationSeconds);
        Assert.Equal(1344, output.Width);
        Assert.Equal(768, output.Height);
        Assert.Equal("h264", output.Encoding?.Video?.Codec);
        Assert.Equal("aac", output.Encoding?.Audio?.Codec);
        Assert.Equal(FixtureSha256, await ComputeSha256Async(context.Workspace.GetAbsoluteAssetPath(output)), ignoreCase: true);

        var (reopened, _) = await new PortableProjectStore().OpenAsync(context.Workspace.Location!.ProjectFilePath);
        var persistedGeneration = Assert.Single(reopened.Generations);
        Assert.Equal(generation.Id, persistedGeneration.Id);
        Assert.Equal("Original fixture reference prompt", persistedGeneration.RequestSnapshot.Prompt);
        Assert.Equal("Motion guide", Assert.Single(persistedGeneration.RequestSnapshot.References).Label);
        Assert.Equal(outputId, Assert.Single(persistedGeneration.OutputAssetIds));
        Assert.Contains(reopened.Assets, asset => asset.Id == outputId);
    }

    [Fact]
    public async Task SavedFrameReferenceIsExtractedPreparedAndFrozenWithoutNetwork()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var inspector = new FixtureMediaInspector();
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new AssetImportService(inspector));
        await workspace.CreateAsync(_temporaryRoot, "Saved Frame fixture project");
        var source = Assert.Single(await workspace.ImportAssetsAsync([FixturePath]));
        var anchor = new FrameAnchor { DisplayLabel = "Final pose" };
        workspace.Project!.Anchors.Add(anchor);
        var revision = workspace.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            source.Id,
            source.Physical!.ContentIdentity.Sha256!,
            0,
            243,
            1,
            24,
            243));
        await workspace.SaveAsync();
        var provider = ScriptedFixtureProvider.ForSuccessfulSubmission();
        var preparation = new RecordingReferencePreparation();
        var ingestion = new HttpGeneratedOutputIngestionService(
            new HttpClient(new FixtureOutputHandler(FixturePath)),
            inspector);
        var workflow = new GenerationWorkflow(
            workspace,
            new PhysicalAssetMaterializer(exactFrameService: new FixtureExactFrameService()),
            ingestion,
            preparation);
        var draft = new GenerationDraft
        {
            ProviderId = provider.Capabilities.ProviderId,
            ModelVersion = provider.Capabilities.ModelVersion,
            Prompt = "Continue from this exact final pose",
            Mode = GenerationMode.ReferenceToVideo,
            DurationSeconds = 10,
            AspectRatio = "16:9",
            Resolution = "768P",
            References =
            [
                new GenerationReferenceDraft
                {
                    ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
                    LogicalObjectId = anchor.Id,
                    AnchorRevisionId = revision.Id,
                    Role = GenerationReferenceRole.StartFrame,
                    Order = 0,
                    Label = "Continuation frame"
                }
            ]
        };

        var generation = await workflow.RunAsync(
            provider,
            draft,
            TestAuthorization(provider),
            new GenerationWorkflowOptions { PollInterval = TimeSpan.Zero, PollTimeout = TimeSpan.FromSeconds(5) });

        var submittedReference = Assert.Single(provider.SubmittedRequest!.PreparedReferences);
        Assert.Equal(GenerationReferenceObjectKind.FrameAnchor, submittedReference.LogicalObjectKind);
        Assert.Equal(MediaType.Image, submittedReference.MediaType);
        Assert.Equal(GenerationReferenceRole.StartFrame, submittedReference.Role);
        var frozen = Assert.Single(generation.RequestSnapshot.References);
        Assert.Equal(revision.Id, frozen.Anchor?.AnchorRevisionId);
        Assert.Equal(243, frozen.Anchor?.PresentationTimestamp);
        Assert.Equal(FixtureSha256, frozen.Anchor?.SourceContentHash, ignoreCase: true);
        Assert.Equal(GenerationReferenceObjectKind.FrameAnchor, preparation.LogicalReference?.ObjectKind);
        var receipt = generation.ReferenceMaterializations[frozen.ReferenceId];
        Assert.Equal(revision.Id.ToString("N"), receipt.PlanHash);
        Assert.Equal(FixtureSha256, receipt.SourceContentHash, ignoreCase: true);
        Assert.Equal(FixtureSha256, receipt.ProducedContentHash, ignoreCase: true);
        Assert.Equal("offline-reference", receipt.ProviderReferenceId);

        var (reopened, _) = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(
            receipt.ProducedContentHash,
            Assert.Single(reopened.Generations).ReferenceMaterializations[frozen.ReferenceId].ProducedContentHash);
    }

    [Fact]
    public async Task RetryCreatesDistinctHistoryAndDoesNotOverwriteFailure()
    {
        var context = await CreateReferenceWorkflowAsync();
        var provider = ScriptedFixtureProvider.ForSuccessfulSubmission();
        var failed = await context.Workflow.QueueAsync(
            provider,
            CreateReferenceDraft(provider, context.Reference.Id),
            TestAuthorization(provider));
        failed.Status = GenerationStatus.Failed;
        failed.CompletedAt = DateTimeOffset.UtcNow;
        failed.Error = new GenerationError { ProviderCode = "intentional", Message = "Original failure" };
        await context.Workspace.SaveAsync();
        var retryDraft = GenerationWorkflow.CreateDerivedDraft(failed, GenerationRelationshipType.RetryOf);

        var retry = await context.Workflow.RunAsync(
            provider,
            retryDraft,
            TestAuthorization(provider),
            new GenerationWorkflowOptions { PollInterval = TimeSpan.Zero, PollTimeout = TimeSpan.FromSeconds(5) });

        Assert.NotEqual(failed.Id, retry.Id);
        Assert.Equal(failed.Id, retry.ParentGenerationId);
        Assert.Equal(GenerationRelationshipType.RetryOf, retry.RelationshipType);
        Assert.Equal(GenerationStatus.Failed, failed.Status);
        Assert.Equal("Original failure", failed.Error?.Message);
        Assert.Empty(failed.OutputAssetIds);
        Assert.Equal(GenerationStatus.Succeeded, retry.Status);
        Assert.Single(retry.OutputAssetIds);
        Assert.Equal(2, context.Workspace.Project!.Generations.Count);
    }

    [Fact]
    public async Task RestoredJobCompletesOwningProjectWhileAnotherProjectRemainsActive()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var projectStore = new PortableProjectStore();
        var inspector = new FixtureMediaInspector();
        var importer = new AssetImportService(inspector);
        var owningWorkspace = new ProjectWorkspace(projectStore, importer);
        await owningWorkspace.CreateAsync(_temporaryRoot, "Owning project");
        var provider = ScriptedFixtureProvider.ForRestoredSuccess();
        var generation = new GenerationRecord
        {
            ProviderJobId = "fixture-job",
            Status = GenerationStatus.Running,
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = provider.Capabilities.ProviderId,
                ModelVersion = provider.Capabilities.ModelVersion,
                Prompt = "Already accepted before restart",
                Mode = GenerationMode.TextToVideo,
                DurationSeconds = 10,
                AspectRatio = "16:9",
                Resolution = "768P"
            }
        };
        owningWorkspace.Project!.Generations.Add(generation);
        await owningWorkspace.SaveAsync();

        var otherWorkspace = new ProjectWorkspace(projectStore, importer);
        await otherWorkspace.CreateAsync(_temporaryRoot, "Currently viewed project");
        var handler = new FixtureOutputHandler(FixturePath);
        var ingestion = new HttpGeneratedOutputIngestionService(new HttpClient(handler), inspector);
        var finalizer = new PersistingFixtureFinalizer(projectStore, ingestion);
        var jobStore = new JsonGenerationJobStore(Path.Combine(_temporaryRoot, "active-jobs.json"));
        await jobStore.SaveAsync([
            new TrackedGenerationJob
            {
                GenerationId = generation.Id,
                ProjectFilePath = owningWorkspace.Location!.ProjectFilePath,
                ProjectName = owningWorkspace.Project.Name,
                ProviderId = provider.Capabilities.ProviderId,
                ProviderDisplayName = provider.Capabilities.DisplayName,
                ModelVersion = provider.Capabilities.ModelVersion,
                ProviderJobId = generation.ProviderJobId,
                RequestedAt = generation.RequestedAt,
                Status = GenerationStatus.Running
            }
        ]);
        await using var coordinator = new GenerationJobCoordinator(
            jobStore,
            id => id == provider.Capabilities.ProviderId ? provider : null,
            finalizer,
            TimeSpan.FromMilliseconds(5));
        var changed = new TaskCompletionSource<GenerationJobStatusChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.JobStatusChanged += (_, args) => changed.TrySetResult(args);

        await coordinator.RestoreAsync();
        var completed = await finalizer.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var notification = await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => coordinator.GetSnapshot().SingleOrDefault()?.IsReconciled == true);

        Assert.Equal(0, provider.SubmitCalls);
        Assert.True(provider.StatusCalls >= 1);
        Assert.Equal(GenerationStatus.Succeeded, completed.Status);
        Assert.Equal(generation.Id, notification.GenerationId);
        Assert.Equal(GenerationStatus.Succeeded, notification.CurrentStatus);
        Assert.Equal("Currently viewed project", otherWorkspace.Project!.Name);
        Assert.Empty(otherWorkspace.Project.Assets);
        var (updatedOwningProject, updatedLocation) = await projectStore.OpenAsync(owningWorkspace.Location.ProjectFilePath);
        var updatedGeneration = Assert.Single(updatedOwningProject.Generations);
        var outputId = Assert.Single(updatedGeneration.OutputAssetIds);
        var output = updatedOwningProject.Assets.Single(asset => asset.Id == outputId);
        Assert.Equal(FixtureSha256, output.Physical?.ContentIdentity.Sha256, ignoreCase: true);
        Assert.True(File.Exists(Path.Combine(updatedLocation.RootDirectory, output.Physical!.RelativePath)));
        Assert.Single(coordinator.GetSnapshot());

        await coordinator.DismissAsync([generation.Id]);

        Assert.Empty(coordinator.GetSnapshot());
    }

    [Fact]
    public async Task RemoteSuccessWithFailedDownloadNeverCreatesSuccessfulAsset()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var projectStore = new PortableProjectStore();
        var inspector = new FixtureMediaInspector();
        var workspace = new ProjectWorkspace(projectStore, new AssetImportService(inspector));
        await workspace.CreateAsync(_temporaryRoot, "Failed download project");
        var provider = ScriptedFixtureProvider.ForSuccessfulSubmission();
        var handler = new FixtureOutputHandler(FixturePath, HttpStatusCode.NotFound);
        var ingestion = new HttpGeneratedOutputIngestionService(new HttpClient(handler), inspector);
        var workflow = new GenerationWorkflow(workspace, new PhysicalAssetMaterializer(), ingestion);
        var draft = new GenerationDraft
        {
            ProviderId = provider.Capabilities.ProviderId,
            Prompt = "The remote task succeeds but the output download fails",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 10,
            AspectRatio = "16:9",
            Resolution = "768P"
        };

        var generation = await workflow.RunAsync(
            provider,
            draft,
            TestAuthorization(provider),
            new GenerationWorkflowOptions { PollInterval = TimeSpan.Zero, PollTimeout = TimeSpan.FromSeconds(5) });

        Assert.Equal(GenerationStatus.Succeeded, generation.Status);
        Assert.Equal(OutputIngestionStatus.Failed, generation.IngestionStatus);
        Assert.Equal("local_ingestion_failed", generation.Error?.ProviderCode);
        Assert.Empty(generation.OutputAssetIds);
        Assert.Empty(workspace.Project!.Assets);
        var generatedDirectory = Path.Combine(workspace.Location!.RootDirectory, "generated");
        Assert.Empty(Directory.EnumerateFiles(generatedDirectory));
    }

    private async Task<ReferenceWorkflowContext> CreateReferenceWorkflowAsync()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var inspector = new FixtureMediaInspector();
        var workspace = new ProjectWorkspace(
            new PortableProjectStore(),
            new AssetImportService(inspector));
        await workspace.CreateAsync(_temporaryRoot, "Reference fixture project");
        var reference = Assert.Single(await workspace.ImportAssetsAsync([FixturePath]));
        var handler = new FixtureOutputHandler(FixturePath);
        var ingestion = new HttpGeneratedOutputIngestionService(new HttpClient(handler), inspector);
        var preparation = new RecordingReferencePreparation();
        var workflow = new GenerationWorkflow(
            workspace,
            new PhysicalAssetMaterializer(),
            ingestion,
            preparation);
        return new ReferenceWorkflowContext(workspace, workflow, reference, preparation);
    }

    private static GenerationDraft CreateReferenceDraft(IVideoGenerationProvider provider, Guid referenceId) => new()
    {
        ProviderId = provider.Capabilities.ProviderId,
        ModelVersion = provider.Capabilities.ModelVersion,
        Prompt = "Original fixture reference prompt",
        Mode = GenerationMode.ReferenceToVideo,
        DurationSeconds = 10,
        AspectRatio = "16:9",
        Resolution = "768P",
        References =
        [
            new GenerationReferenceDraft
            {
                LogicalObjectId = referenceId,
                Role = GenerationReferenceRole.Motion,
                Order = 4,
                Label = "Motion guide",
                Notes = "Use the movement and timing from this clip."
            }
        ],
        ProviderParameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["generate_audio"] = "true"
        }
    };

    private static GenerationSubmissionAuthorization TestAuthorization(IVideoGenerationProvider provider) =>
        GenerationSubmissionAuthorization.ForNetworkIsolatedTest(provider.Capabilities.ProviderId);

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The expected offline workflow state was not reached.");
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed record ReferenceWorkflowContext(
        ProjectWorkspace Workspace,
        GenerationWorkflow Workflow,
        ProjectAsset Reference,
        RecordingReferencePreparation Preparation);

    private sealed class ScriptedFixtureProvider : IAsyncVideoGenerationProvider
    {
        private readonly Queue<ProviderGenerationJob> _states;

        private ScriptedFixtureProvider(IEnumerable<ProviderGenerationJob> states)
        {
            _states = new Queue<ProviderGenerationJob>(states);
        }

        public int SubmitCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public GenerationRequest? SubmittedRequest { get; private set; }
        public GenerationProviderCostBehavior CostBehavior => GenerationProviderCostBehavior.PotentiallyBillable;
        public GenerationProviderCapabilities Capabilities { get; } = new(
            "test.offline-reference-provider",
            "Offline fixture provider",
            "fixture/model-v1",
            [GenerationMode.TextToVideo, GenerationMode.ReferenceToVideo],
            4,
            30,
            ["16:9"],
            ["768P"],
            5,
            5,
            5,
            new HashSet<MediaType> { MediaType.Image, MediaType.Video, MediaType.Audio },
            new Dictionary<string, IReadOnlyList<string>>());

        public static ScriptedFixtureProvider ForSuccessfulSubmission() => new(
        [
            new ProviderGenerationJob { ProviderJobId = "fixture-job", Status = GenerationStatus.Running },
            SuccessfulJob()
        ]);

        public static ScriptedFixtureProvider ForRestoredSuccess() => new([SuccessfulJob()]);

        public Task<GenerationSubmission> SubmitAsync(
            GenerationRequest request,
            IReadOnlyCollection<ProjectAsset> projectAssets,
            GenerationSubmissionAuthorization? authorization = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            authorization?.Demand(Capabilities.ProviderId, allowNetworkIsolatedTest: true);
            SubmitCalls++;
            SubmittedRequest = request;
            return Task.FromResult(new GenerationSubmission
            {
                ProviderJobId = "fixture-job",
                Status = GenerationStatus.Running,
                ResponseMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["transport"] = "strictly-offline"
                }
            });
        }

        public Task<ProviderGenerationJob> GetJobAsync(
            string providerJobId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusCalls++;
            if (_states.Count == 0) throw new InvalidOperationException("No scripted provider state remains.");
            return Task.FromResult(_states.Dequeue());
        }

        private static ProviderGenerationJob SuccessfulJob() => new()
        {
            ProviderJobId = "fixture-job",
            Status = GenerationStatus.Succeeded,
            Outputs = [new ProviderGenerationOutput(OutputUrl)],
            ResponseMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["providerFixture"] = "test_video.mp4"
            }
        };
    }

    private sealed class RecordingReferencePreparation : IProviderAssetPreparationService
    {
        public int CallCount { get; private set; }
        public GenerationReferenceSnapshot? LogicalReference { get; private set; }

        public Task<PreparedProviderReference> PrepareAsync(
            string providerId,
            GenerationReferenceSnapshot logicalReference,
            MaterializedMediaLease media,
            GenerationSubmissionAuthorization authorization,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            authorization.Demand(providerId, allowNetworkIsolatedTest: true);
            Assert.True(File.Exists(media.Path));
            Assert.Equal(FixtureSha256, media.ContentIdentity.Sha256, ignoreCase: true);
            CallCount++;
            LogicalReference = logicalReference;
            return Task.FromResult(new PreparedProviderReference(
                logicalReference,
                "https://uploads.reelforge.test/reference.mp4",
                new MaterializationReceipt
                {
                    SourceContentHash = media.ContentIdentity.Sha256,
                    ProviderReferenceId = "offline-reference",
                    ProviderScope = "mock-upload"
                }));
        }
    }

    private sealed class FixtureExactFrameService : IExactVideoFrameService
    {
        public Task<IReadOnlyList<VideoPresentationFrame>> IndexAsync(
            string mediaPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VideoPresentationFrame>>([]);

        public Task<IReadOnlyList<VideoPresentationFrame>> IndexWindowAsync(
            string mediaPath,
            double centerSeconds,
            double radiusSeconds = 2,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VideoPresentationFrame>>([]);

        public Task<MaterializedMediaLease> ExtractAsync(
            string mediaPath,
            string sourceContentHash,
            FrameAnchorRevision revision,
            MaterializationPurpose purpose,
            string? profile = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new MaterializedMediaLease(
                FixturePath,
                new ContentIdentity { Sha256 = FixtureSha256, Status = ContentHashStatus.Verified },
                new MediaEncodingMetadata { ContainerFormat = "png" },
                isDurableSource: false));
        }
    }

    private sealed class FixtureOutputHandler(string fixturePath, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Method != HttpMethod.Get || request.RequestUri?.AbsoluteUri != OutputUrl)
                throw new InvalidOperationException($"Unexpected network request: {request.Method} {request.RequestUri}");
            var response = new HttpResponseMessage(statusCode) { RequestMessage = request };
            if (statusCode == HttpStatusCode.OK)
                response.Content = new ByteArrayContent(await File.ReadAllBytesAsync(fixturePath, cancellationToken));
            return response;
        }
    }

    private sealed class FixtureMediaInspector : IMediaInspectionService
    {
        public async Task<MediaEncodingMetadata> InspectAsync(
            string mediaPath,
            CancellationToken cancellationToken = default)
        {
            await using var stream = File.OpenRead(mediaPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!hash.Equals(FixtureSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The media bytes do not match the committed test fixture.");
            return new MediaEncodingMetadata
            {
                ContainerFormat = "mp4",
                DurationSeconds = 10.125,
                SizeBytes = 1_642_405,
                BitRate = 1_297_702,
                Video = new VideoStreamMetadata
                {
                    Codec = "h264",
                    CodecProfile = "High",
                    Width = 1344,
                    Height = 768,
                    PixelFormat = "yuv420p",
                    FrameRate = "24/1"
                },
                Audio = new AudioStreamMetadata
                {
                    Codec = "aac",
                    SampleRate = 32_000,
                    Channels = 2,
                    ChannelLayout = "stereo"
                }
            };
        }
    }

    private sealed class PersistingFixtureFinalizer(
        IProjectStore projectStore,
        IGeneratedOutputIngestionService ingestion) : IGenerationJobFinalizer
    {
        public TaskCompletionSource<TrackedGenerationJob> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task FinalizeAsync(TrackedGenerationJob job, CancellationToken cancellationToken = default)
        {
            var (project, location) = await projectStore.OpenAsync(job.ProjectFilePath, cancellationToken);
            var generation = project.Generations.Single(item => item.Id == job.GenerationId);
            generation.Status = job.Status;
            generation.Error = job.Error;
            foreach (var pair in job.ResponseMetadata) generation.ResponseMetadata[pair.Key] = pair.Value;
            if (job.Status == GenerationStatus.Succeeded)
            {
                generation.CompletedAt = DateTimeOffset.UtcNow;
                generation.IngestionStatus = OutputIngestionStatus.Running;
                await projectStore.SaveAsync(project, location, cancellationToken);
                var assets = await ingestion.IngestAsync(location, generation.Id, job.Outputs, cancellationToken);
                foreach (var asset in assets)
                {
                    project.AddAsset(asset);
                    generation.OutputAssetIds.Add(asset.Id);
                }
                generation.IngestionStatus = OutputIngestionStatus.Succeeded;
            }
            await projectStore.SaveAsync(project, location, cancellationToken);
            Completed.TrySetResult(job);
        }
    }
}
