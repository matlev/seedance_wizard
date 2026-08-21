using System.Net;
using System.Text;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class GenerationMediaNetworkTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge media network tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(AtlasCloudSeedance25Provider.ProviderId)]
    [InlineData(AtlasCloudMiniMaxH3Provider.ProviderId)]
    public async Task AtlasUploadUsesMultipartAndParsesCurrentNestedResponse(string providerId)
    {
        Directory.CreateDirectory(_temporaryRoot);
        var path = Path.Combine(_temporaryRoot, "reference image.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"code":200,"message":"success","data":{"type":"image","download_url":"https://uploads.example/temporary.png","filename":"temporary.png","size":4}}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var service = new AtlasCloudAssetPreparationService(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.atlascloud.ai/") },
            new TestSecretStore());
        await using var lease = new MaterializedMediaLease(
            path,
            new ContentIdentity { Sha256 = new string('b', 64), Status = ContentHashStatus.Verified },
            null,
            true);
        var logical = new GenerationReferenceSnapshot
        {
            ObjectKind = GenerationReferenceObjectKind.Asset,
            LogicalObjectId = Guid.NewGuid(),
            ContentHash = new string('b', 64),
            Order = 0
        };

        var prepared = await service.PrepareAsync(
            providerId,
            logical,
            lease,
            GenerationSubmissionAuthorization.ForNetworkIsolatedTest(providerId));

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.atlascloud.ai/api/v1/model/uploadMedia", handler.Uri?.AbsoluteUri);
        Assert.StartsWith("multipart/form-data", handler.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://uploads.example/temporary.png", prepared.ProviderRepresentation);
        Assert.Equal("temporary-upload", prepared.Receipt?.ProviderScope);
    }

    [Fact]
    public async Task BytePlusImagePreparationCreatesInlineDataUrlWithoutNetwork()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var path = Path.Combine(_temporaryRoot, "reference.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var service = new BytePlusModelArkAssetPreparationService();
        await using var lease = new MaterializedMediaLease(
            path,
            new ContentIdentity { Sha256 = new string('c', 64), Status = ContentHashStatus.Verified },
            null,
            true);
        var logical = new GenerationReferenceSnapshot
        {
            ObjectKind = GenerationReferenceObjectKind.Asset,
            LogicalObjectId = Guid.NewGuid(),
            ContentHash = new string('c', 64),
            Order = 0
        };

        var prepared = await service.PrepareAsync(
            BytePlusModelArkSeedance25Provider.ProviderId,
            logical,
            lease,
            GenerationSubmissionAuthorization.ForNetworkIsolatedTest(BytePlusModelArkSeedance25Provider.ProviderId));

        Assert.Equal("data:image/png;base64,AQIDBA==", prepared.ProviderRepresentation);
        Assert.Equal("inline-base64", prepared.Receipt?.ProviderScope);
    }

    [Fact]
    public async Task BytePlusLocalVideoPreparationRefusesUndocumentedUploadPath()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var path = Path.Combine(_temporaryRoot, "reference.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var service = new BytePlusModelArkAssetPreparationService();
        await using var lease = new MaterializedMediaLease(
            path,
            new ContentIdentity { Sha256 = new string('d', 64), Status = ContentHashStatus.Verified },
            null,
            true);

        var exception = await Assert.ThrowsAsync<GenerationValidationException>(() => service.PrepareAsync(
            BytePlusModelArkSeedance25Provider.ProviderId,
            new GenerationReferenceSnapshot { LogicalObjectId = Guid.NewGuid() },
            lease,
            GenerationSubmissionAuthorization.ForNetworkIsolatedTest(BytePlusModelArkSeedance25Provider.ProviderId)));

        Assert.Contains(exception.Errors, error => error.Contains("public HTTPS URL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutputDownloaderUsesHttpsAndCreatesDurableGeneratedAsset()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0, 1, 2, 3, 4, 5])
            });
        var service = new HttpGeneratedOutputIngestionService(
            new HttpClient(handler),
            new StubInspector());
        var generationId = Guid.NewGuid();

        var assets = await service.IngestAsync(
            new ProjectLocation(_temporaryRoot, Path.Combine(_temporaryRoot, "Network test.rfp")),
            generationId,
            [new ProviderGenerationOutput("https://storage.example/result.mp4")]);

        var asset = Assert.Single(assets);
        Assert.Equal(AssetOrigin.Generated, asset.Origin);
        Assert.Equal(generationId, asset.Provenance?.GenerationId);
        Assert.Equal(ContentHashStatus.Verified, asset.Physical?.ContentIdentity.Status);
        Assert.True(File.Exists(Path.Combine(_temporaryRoot, asset.Physical!.RelativePath)));
        Assert.Equal(HttpMethod.Get, handler.Method);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class TestSecretStore : ISecretStore
    {
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("unit-test-key");
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEncodingMetadata
            {
                ContainerFormat = "mp4",
                DurationSeconds = 8,
                Video = new VideoStreamMetadata { Width = 1280, Height = 720 }
            });
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ContentType { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Method = request.Method;
            ContentType = request.Content?.Headers.ContentType?.ToString();
            var response = responseFactory(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
