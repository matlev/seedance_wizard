using SeedanceWizard.Application;
using SeedanceWizard.Core;
using SeedanceWizard.Infrastructure;

namespace SeedanceWizard.Tests;

public sealed class GenerationSnapshotTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "Seedance Wizard generation snapshot tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SubmittedGenerationDeepCopiesMutableRequest()
    {
        var store = new PortableProjectStore();
        var inspector = new FfprobeMediaInspectionService(null, new ExternalProcessRunner());
        var workspace = new ProjectWorkspace(store, new AssetImportService(inspector));
        await workspace.CreateAsync(_temporaryRoot, "Snapshot test");
        var request = new GenerationRequest
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

        var record = await workspace.SubmitGenerationAsync(new FakeVideoGenerationProvider(TimeSpan.Zero), request);
        request.Prompt = "Mutated after submission";
        request.ProviderParameters["generateAudio"] = "false";

        Assert.Equal("Original prompt", record.RequestSnapshot.Prompt);
        Assert.Equal("true", record.RequestSnapshot.ProviderParameters["generateAudio"]);
        var (reopened, _) = await store.OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal("Original prompt", Assert.Single(reopened.Generations).RequestSnapshot.Prompt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }
}
