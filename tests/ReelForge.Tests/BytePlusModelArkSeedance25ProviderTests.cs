using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class BytePlusModelArkSeedance25ProviderTests
{
    [Fact]
    public async Task SubmitTextToVideoUsesOfficialEndpointModelAndContentSchema()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"cgt-test-123"}""");
        var provider = CreateProvider(handler);
        var request = new GenerationRequest
        {
            Prompt = "A storm rolls across a glass desert",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 30,
            AspectRatio = "21:9",
            Resolution = "720p",
            ProviderParameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["generate_audio"] = "true",
                ["watermark"] = "false",
                ["output_format"] = "mov"
            }
        };

        var submission = await provider.SubmitAsync(request, [], TestAuthorization());

        Assert.Equal("cgt-test-123", submission.ProviderJobId);
        Assert.Equal(GenerationStatus.Queued, submission.Status);
        Assert.Equal("https://ark.ap-southeast.bytepluses.com/api/v3/contents/generations/tasks", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("unit-test-key", handler.Authorization?.Parameter);
        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        Assert.Equal(BytePlusModelArkSeedance25Provider.ModelId, root.GetProperty("model").GetString());
        Assert.Equal(30, root.GetProperty("duration").GetInt32());
        Assert.Equal("mov", root.GetProperty("output_format").GetString());
        var content = Assert.Single(root.GetProperty("content").EnumerateArray());
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Equal(request.Prompt, content.GetProperty("text").GetString());
    }

    [Fact]
    public void ReferenceToVideoBuildsTypedOrderedContentItems()
    {
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var image = CreateAsset(MediaType.Image, "image.png", "data:image/png;base64,AAAA");
        var video = CreateAsset(MediaType.Video, "video.mp4", "https://assets.example/video.mp4");
        var audio = CreateAsset(MediaType.Audio, "audio.wav", "asset://audio-1");
        var request = new GenerationRequest
        {
            Prompt = "Use @Image 1, @Video 1, and @Audio 1",
            Mode = GenerationMode.ReferenceToVideo,
            DurationSeconds = 15,
            AspectRatio = "16:9",
            Resolution = "480p",
            ReferenceAssetIds = [image.Id, video.Id, audio.Id]
        };

        var payload = provider.BuildPayload(request, [image, video, audio]);
        var content = Assert.IsType<List<object>>(payload["content"]);

        Assert.Equal(4, content.Count);
        AssertContentItem(content[1], "image_url", "reference_image", "data:image/png;base64,AAAA");
        AssertContentItem(content[2], "video_url", "reference_video", "https://assets.example/video.mp4");
        AssertContentItem(content[3], "audio_url", "reference_audio", "asset://audio-1");
    }

    [Fact]
    public void ImageToVideoMapsOrderedImagesToFirstAndLastFrame()
    {
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var first = CreateAsset(MediaType.Image, "first.png", "https://assets.example/first.png");
        var last = CreateAsset(MediaType.Image, "last.png", "https://assets.example/last.png");
        var request = new GenerationRequest
        {
            Prompt = "Orbit around the subject",
            Mode = GenerationMode.ImageToVideo,
            DurationSeconds = 8,
            AspectRatio = "adaptive",
            Resolution = "720p",
            ReferenceAssetIds = [first.Id, last.Id]
        };

        var content = Assert.IsType<List<object>>(provider.BuildPayload(request, [first, last])["content"]);

        AssertContentItem(content[1], "image_url", "first_frame", "https://assets.example/first.png");
        AssertContentItem(content[2], "image_url", "last_frame", "https://assets.example/last.png");
    }

    [Fact]
    public async Task SubmissionWithoutInteractiveAuthorizationCannotReachHttpHandler()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler);
        var request = CreateTextRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SubmitAsync(request, []));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task PollMapsSucceededTaskOutputAndUsage()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"id":"cgt-test-123","model":"dreamina-seedance-2-5-260628","status":"succeeded","content":{"video_url":"https://output.example/result.mov"},"usage":{"completion_tokens":12345,"total_tokens":12345},"duration":8,"resolution":"720p","ratio":"16:9","output_format":"mov"}""");
        var provider = CreateProvider(handler);

        var job = await provider.GetJobAsync("cgt-test-123");

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://ark.ap-southeast.bytepluses.com/api/v3/contents/generations/tasks/cgt-test-123", handler.RequestUri?.AbsoluteUri);
        Assert.Equal(GenerationStatus.Succeeded, job.Status);
        Assert.Equal("https://output.example/result.mov", Assert.Single(job.Outputs).DownloadUrl);
        Assert.Equal("12345", job.ResponseMetadata["usage.completion_tokens"]);
    }

    [Fact]
    public async Task ProviderErrorBecomesStructuredException()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.BadRequest,
            """{"error":{"code":"InvalidParameter","message":"Duration is invalid."}}""");
        var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<VideoGenerationProviderException>(
            () => provider.SubmitAsync(CreateTextRequest(), [], TestAuthorization()));

        Assert.Equal(400, exception.HttpStatus);
        Assert.Equal("InvalidParameter", exception.ProviderCode);
        Assert.Equal("Duration is invalid.", exception.Message);
    }

    private static GenerationRequest CreateTextRequest() => new()
    {
        Prompt = "A network-isolated contract test",
        Mode = GenerationMode.TextToVideo,
        DurationSeconds = 4,
        AspectRatio = "16:9",
        Resolution = "480p"
    };

    private static void AssertContentItem(object item, string type, string role, string url)
    {
        var dictionary = Assert.IsType<Dictionary<string, object?>>(item);
        Assert.Equal(type, dictionary["type"]);
        Assert.Equal(role, dictionary["role"]);
        var urlObject = Assert.IsType<Dictionary<string, string>>(dictionary[type]);
        Assert.Equal(url, urlObject["url"]);
    }

    private static GenerationSubmissionAuthorization TestAuthorization() =>
        GenerationSubmissionAuthorization.ForNetworkIsolatedTest(BytePlusModelArkSeedance25Provider.ProviderId);

    private static BytePlusModelArkSeedance25Provider CreateProvider(RecordingHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://ark.ap-southeast.bytepluses.com/api/v3/") },
            new TestSecretStore("unit-test-key"),
            new ProjectAssetReferenceResolver());

    private static ProjectAsset CreateAsset(MediaType type, string fileName, string providerReference)
    {
        var asset = new ProjectAsset { MediaType = type, FileName = fileName };
        asset.ProviderReferences[BytePlusModelArkSeedance25Provider.ProviderId] = new ProviderAssetReference
        {
            Value = providerReference
        };
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
        public HttpMethod? Method { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? RequestBody { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            Method = request.Method;
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
