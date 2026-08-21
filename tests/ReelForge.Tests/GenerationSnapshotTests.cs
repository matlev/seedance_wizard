using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class GenerationSnapshotTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge generation snapshot tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerationWorkflowDeepCopiesMutableDraftIntoHistory()
    {
        var store = new PortableProjectStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Snapshot test");
        var workflow = new GenerationWorkflow(workspace, new UnusedMaterializer(), new UnusedOutputIngestion());
        var draft = new GenerationDraft
        {
            Prompt = "Original prompt",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 8,
            AspectRatio = "16:9",
            Resolution = "720p",
            ProviderParameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["generateAudio"] = "true"
            }
        };

        var record = await workflow.SubmitAsync(
            new FakeVideoGenerationProvider(TimeSpan.Zero),
            draft,
            authorization: null);
        draft.Prompt = "Mutated after submission";
        draft.ProviderParameters["generateAudio"] = "false";

        Assert.Equal("Original prompt", record.RequestSnapshot.Prompt);
        Assert.Equal("true", record.RequestSnapshot.ProviderParameters["generateAudio"]);
        var (reopened, _) = await store.OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal("Original prompt", Assert.Single(reopened.Generations).RequestSnapshot.Prompt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The snapshot test does not import assets.");
    }

    private sealed class UnusedMaterializer : IMediaMaterializer
    {
        public Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Text-to-video does not materialize references.");
    }

    private sealed class UnusedOutputIngestion : IGeneratedOutputIngestionService
    {
        public Task<IReadOnlyList<ProjectAsset>> IngestAsync(
            ProjectLocation location,
            Guid generationId,
            IReadOnlyList<ProviderGenerationOutput> outputs,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The synchronous fake provider produces no outputs.");
    }
}
