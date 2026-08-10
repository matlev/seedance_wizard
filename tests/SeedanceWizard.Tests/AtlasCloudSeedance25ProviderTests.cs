using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SeedanceWizard.Application;
using SeedanceWizard.Core;
using SeedanceWizard.Infrastructure;

namespace SeedanceWizard.Tests;

public sealed class AtlasCloudSeedance25ProviderTests
{
    [Fact]
    public async Task SubmitTextToVideoUsesVerifiedEndpointAndSchema()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"code":200,"data":{"id":"prediction-123","status":"processing","model":"bytedance/seedance-2.5/text-to-video"}}""");
        var provider = CreateProvider(handler);
        var request = new GenerationRequest
        {
            Prompt = "A moonlit crane crossing a misty lake",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 30,
            AspectRatio = "21:9",
            Resolution = "720p",
            ProviderParameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["generate_audio"] = "false",
                ["output_format"] = "mov"
            }
        };

        var submission = await provider.SubmitAsync(request, []);

        Assert.Equal("prediction-123", submission.ProviderJobId);
        Assert.Equal(GenerationStatus.Running, submission.Status);
        Assert.Equal("https://api.atlascloud.ai/api/v1/model/generateVideo", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("unit-test-key", handler.Authorization?.Parameter);
        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(AtlasCloudSeedance25Provider.TextToVideoModel, document.RootElement.GetProperty("model").GetString());
        Assert.Equal(30, document.RootElement.GetProperty("duration").GetInt32());
        Assert.False(document.RootElement.GetProperty("generate_audio").GetBoolean());
        Assert.Equal("mov", document.RootElement.GetProperty("output_format").GetString());
    }

    [Fact]
    public void ReferenceToVideoMapsProviderReferencesByMediaType()
    {
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var image = CreateAsset(MediaType.Image, "image.png", "https://assets.example/image.png");
        var video = CreateAsset(MediaType.Video, "video.mp4", "atlas-asset://video-1");
        var audio = CreateAsset(MediaType.Audio, "audio.wav", "data:audio/wav;base64,AAAA");
        var request = new GenerationRequest
        {
            Prompt = "@Image1 transforms while @Video1 supplies motion and @Audio1 sets rhythm",
            Mode = GenerationMode.ReferenceToVideo,
            DurationSeconds = 12,
            AspectRatio = "adaptive",
            Resolution = "480p",
            ReferenceAssetIds = [image.Id, video.Id, audio.Id]
        };

        var payload = provider.BuildPayload(request, [image, video, audio]);

        Assert.Equal(AtlasCloudSeedance25Provider.ReferenceToVideoModel, payload["model"]);
        Assert.Equal(["https://assets.example/image.png"], Assert.IsType<string[]>(payload["reference_images"]));
        Assert.Equal(["atlas-asset://video-1"], Assert.IsType<string[]>(payload["reference_videos"]));
        Assert.Equal(["data:audio/wav;base64,AAAA"], Assert.IsType<string[]>(payload["reference_audios"]));
    }

    [Fact]
    public void ImageToVideoRejectsUndocumentedNonAdaptiveRatio()
    {
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var image = CreateAsset(MediaType.Image, "first.png", "https://assets.example/first.png");
        var request = new GenerationRequest
        {
            Prompt = "A slow push in",
            Mode = GenerationMode.ImageToVideo,
            DurationSeconds = 8,
            AspectRatio = "16:9",
            Resolution = "720p",
            ReferenceAssetIds = [image.Id]
        };

        var exception = Assert.Throws<GenerationValidationException>(() => provider.BuildPayload(request, [image]));

        Assert.Contains(exception.Errors, error => error.Contains("adaptive ratio", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SubmitTurnsProviderErrorIntoStructuredException()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.TooManyRequests,
            """{"code":"RATE_LIMITED","message":"Quota exceeded."}""");
        var provider = CreateProvider(handler);
        var request = new GenerationRequest
        {
            Prompt = "A harmless test that is intercepted by an in-memory HTTP handler",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 4,
            AspectRatio = "16:9",
            Resolution = "480p"
        };

        var exception = await Assert.ThrowsAsync<VideoGenerationProviderException>(
            () => provider.SubmitAsync(request, []));

        Assert.Equal(429, exception.HttpStatus);
        Assert.Equal("RATE_LIMITED", exception.ProviderCode);
        Assert.Equal("Quota exceeded.", exception.Message);
    }

    private static AtlasCloudSeedance25Provider CreateProvider(RecordingHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.atlascloud.ai/") },
            new TestSecretStore("unit-test-key"),
            new ProjectAssetReferenceResolver());

    private static ProjectAsset CreateAsset(MediaType type, string fileName, string providerReference)
    {
        var asset = new ProjectAsset { MediaType = type, FileName = fileName };
        asset.ProviderReferences[AtlasCloudSeedance25Provider.ProviderId] = providerReference;
        return asset;
    }

    private sealed class TestSecretStore(string value) : ISecretStore
    {
        public Task SetAsync(string key, string newValue, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(value);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
