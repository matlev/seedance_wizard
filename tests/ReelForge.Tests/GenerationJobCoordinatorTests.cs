using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class GenerationJobCoordinatorTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge job coordinator tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CoordinatorPollsAndFinalizesWithoutSubmittingAnotherGeneration()
    {
        var provider = new PollOnlyProvider();
        var finalizer = new RecordingFinalizer();
        var store = new JsonGenerationJobStore(Path.Combine(_temporaryRoot, "active-jobs.json"));
        await using var coordinator = new GenerationJobCoordinator(
            store,
            providerId => providerId == provider.Capabilities.ProviderId ? provider : null,
            finalizer,
            TimeSpan.FromMilliseconds(5));
        var statusChanged = new TaskCompletionSource<GenerationJobStatusChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.JobStatusChanged += (_, change) => statusChanged.TrySetResult(change);
        var generation = CreateAcceptedGeneration(provider);

        await coordinator.TrackAsync(
            generation,
            new ProjectLocation(_temporaryRoot, Path.Combine(_temporaryRoot, "test.rfp")),
            "Coordinator test",
            provider.Capabilities.DisplayName);
        var finalized = await finalizer.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var change = await statusChanged.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => coordinator.GetSnapshot().Count == 0);

        Assert.Equal(generation.Id, finalized.GenerationId);
        Assert.Equal(GenerationStatus.Succeeded, finalized.Status);
        Assert.Equal(generation.Id, change.GenerationId);
        Assert.Equal(GenerationStatus.Running, change.PreviousStatus);
        Assert.Equal(GenerationStatus.Succeeded, change.CurrentStatus);
        Assert.Equal(0, provider.SubmitCalls);
        Assert.True(provider.StatusCalls >= 1);
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task RestoreReloadsPersistedJobsBeforePolling()
    {
        var provider = new BlockingPollProvider();
        var store = new JsonGenerationJobStore(Path.Combine(_temporaryRoot, "restore-jobs.json"));
        var generation = CreateAcceptedGeneration(provider);
        await store.SaveAsync([
            new TrackedGenerationJob
            {
                GenerationId = generation.Id,
                ProjectFilePath = Path.Combine(_temporaryRoot, "restored.rfp"),
                ProjectName = "Restored project",
                ProviderId = provider.Capabilities.ProviderId,
                ProviderDisplayName = provider.Capabilities.DisplayName,
                ModelVersion = provider.Capabilities.ModelVersion,
                ProviderJobId = generation.ProviderJobId!,
                RequestedAt = generation.RequestedAt,
                Status = GenerationStatus.Running
            }
        ]);

        await using var coordinator = new GenerationJobCoordinator(
            store,
            _ => provider,
            new RecordingFinalizer(),
            TimeSpan.FromMilliseconds(5));
        await coordinator.RestoreAsync();

        var restored = Assert.Single(coordinator.GetSnapshot());
        Assert.Equal("Restored project", restored.ProjectName);
        Assert.Equal(generation.Id, restored.GenerationId);
    }

    private static GenerationRecord CreateAcceptedGeneration(IVideoGenerationProvider provider) => new()
    {
        ProviderJobId = "accepted-job",
        Status = GenerationStatus.Running,
        RequestSnapshot = new GenerationRequestSnapshot
        {
            ProviderId = provider.Capabilities.ProviderId,
            ModelVersion = provider.Capabilities.ModelVersion,
            Prompt = "Already submitted by a human-confirmed application action",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 5,
            AspectRatio = "16:9",
            Resolution = "720p"
        }
    };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The coordinator did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class RecordingFinalizer : IGenerationJobFinalizer
    {
        public TaskCompletionSource<TrackedGenerationJob> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FinalizeAsync(TrackedGenerationJob job, CancellationToken cancellationToken = default)
        {
            Completed.TrySetResult(job);
            return Task.CompletedTask;
        }
    }

    private class PollOnlyProvider : IAsyncVideoGenerationProvider
    {
        public int SubmitCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public GenerationProviderCostBehavior CostBehavior => GenerationProviderCostBehavior.PotentiallyBillable;
        public GenerationProviderCapabilities Capabilities { get; } = new(
            "test.poll-only",
            "Poll-only test provider",
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
            throw new InvalidOperationException("The job coordinator must never submit a generation.");
        }

        public virtual Task<ProviderGenerationJob> GetJobAsync(
            string providerJobId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            return Task.FromResult(new ProviderGenerationJob
            {
                ProviderJobId = providerJobId,
                Status = GenerationStatus.Succeeded,
                Outputs = [new ProviderGenerationOutput("https://output.example/video.mp4")]
            });
        }
    }

    private sealed class BlockingPollProvider : PollOnlyProvider
    {
        public override async Task<ProviderGenerationJob> GetJobAsync(
            string providerJobId,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
