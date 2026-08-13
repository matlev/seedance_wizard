using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class AtlasCloudMiniMaxH3ProviderTests
{
    [Fact]
    public async Task SubmitTextToVideoUsesDocumentedModelAndSchema()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"code":200,"data":{"id":"h3-123","status":"processing","model":"minimax/h3/text-to-video"}}""");
        var provider = CreateProvider(handler);
        var request = new GenerationRequest
        {
            Prompt = "A glass airship crosses a sunrise sky.",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 15,
            AspectRatio = "16:9",
            Resolution = "2K"
        };

        var submission = await provider.SubmitAsync(request, [], TestAuthorization());

        Assert.Equal("h3-123", submission.ProviderJobId);
        Assert.Equal(GenerationStatus.Running, submission.Status);
        Assert.Equal("https://api.atlascloud.ai/api/v1/model/generateVideo", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("unit-test-key", handler.Authorization?.Parameter);
        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(AtlasCloudMiniMaxH3Provider.TextToVideoModel, document.RootElement.GetProperty("model").GetString());
        Assert.Equal("2K", document.RootElement.GetProperty("resolution").GetString());
        Assert.Equal(15, document.RootElement.GetProperty("duration").GetInt32());
        Assert.Equal("16:9", document.RootElement.GetProperty("ratio").GetString());
        Assert.False(document.RootElement.TryGetProperty("generate_audio", out _));
    }

    [Fact]
    public void ImageToVideoMapsFirstAndOptionalEndFrame()
    {
        var first = CreateAsset(MediaType.Image, "first.png", "data:image/png;base64,AAAA");
        var last = CreateAsset(MediaType.Image, "last.webp", "https://assets.example/last.webp");
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var request = new GenerationRequest
        {
            Prompt = "The scene transitions from morning to night.",
            Mode = GenerationMode.ImageToVideo,
            DurationSeconds = 8,
            AspectRatio = "adaptive",
            Resolution = "768P",
            ReferenceAssetIds = [first.Id, last.Id]
        };

        var payload = provider.BuildPayload(request, [first, last]);

        Assert.Equal(AtlasCloudMiniMaxH3Provider.ImageToVideoModel, payload["model"]);
        Assert.Equal("data:image/png;base64,AAAA", payload["image"]);
        Assert.Equal("https://assets.example/last.webp", payload["end_image"]);
        Assert.Equal("adaptive", payload["ratio"]);
    }

    [Fact]
    public void ReferenceToVideoMapsTypedHttpsReferencesInOrder()
    {
        var image = CreateAsset(MediaType.Image, "character.png", "https://assets.example/character.png");
        var video = CreateAsset(MediaType.Video, "motion.mp4", "https://assets.example/motion.mp4");
        var audio = CreateAsset(MediaType.Audio, "beat.wav", "https://assets.example/beat.wav");
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var request = new GenerationRequest
        {
            Prompt = "Keep the character consistent and synchronize the motion to the beat.",
            Mode = GenerationMode.ReferenceToVideo,
            DurationSeconds = 12,
            AspectRatio = "9:16",
            Resolution = "2K",
            ReferenceAssetIds = [image.Id, video.Id, audio.Id]
        };

        var payload = provider.BuildPayload(request, [image, video, audio]);
        var refers = Assert.IsType<Dictionary<string, string>[]>(payload["refers"]);

        Assert.Equal(AtlasCloudMiniMaxH3Provider.ReferenceToVideoModel, payload["model"]);
        Assert.Collection(
            refers,
            item => AssertReference(item, "https://assets.example/character.png", "image"),
            item => AssertReference(item, "https://assets.example/motion.mp4", "video"),
            item => AssertReference(item, "https://assets.example/beat.wav", "audio"));
    }

    [Fact]
    public void PreparedReferencesRemainDistinctWhenOneLogicalAssetAppearsTwice()
    {
        var image = CreateAsset(MediaType.Image, "character.png", "https://assets.example/fallback.png");
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var request = ValidReferenceRequest([image.Id, image.Id]);
        request.PreparedReferences =
        [
            new PreparedGenerationReference(
                Guid.NewGuid(), GenerationReferenceObjectKind.Asset, image.Id, MediaType.Image,
                GenerationReferenceRole.Character, 0, "https://uploads.example/front.png"),
            new PreparedGenerationReference(
                Guid.NewGuid(), GenerationReferenceObjectKind.Asset, image.Id, MediaType.Image,
                GenerationReferenceRole.Style, 1, "https://uploads.example/profile.png")
        ];

        var payload = provider.BuildPayload(request, [image]);
        var refers = Assert.IsType<Dictionary<string, string>[]>(payload["refers"]);

        Assert.Collection(
            refers,
            item => AssertReference(item, "https://uploads.example/front.png", "image"),
            item => AssertReference(item, "https://uploads.example/profile.png", "image"));
    }

    [Fact]
    public void ReferenceToVideoRejectsAudioOnlyAndNonHttpsReferences()
    {
        var audio = CreateAsset(MediaType.Audio, "beat.wav", "https://assets.example/beat.wav");
        var image = CreateAsset(MediaType.Image, "character.png", "data:image/png;base64,AAAA");
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));

        var audioOnly = ValidReferenceRequest([audio.Id]);
        var audioError = Assert.Throws<GenerationValidationException>(() => provider.BuildPayload(audioOnly, [audio]));
        Assert.Contains(audioError.Errors, error => error.Contains("audio alone", StringComparison.Ordinal));

        var inlineImage = ValidReferenceRequest([image.Id]);
        var urlError = Assert.Throws<GenerationValidationException>(() => provider.BuildPayload(inlineImage, [image]));
        Assert.Contains(urlError.Errors, error => error.Contains("HTTPS URL", StringComparison.Ordinal));
    }

    [Fact]
    public void ModeSpecificRatiosAreValidated()
    {
        var image = CreateAsset(MediaType.Image, "first.png", "https://assets.example/first.png");
        var provider = CreateProvider(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var textRequest = new GenerationRequest
        {
            Prompt = "A landscape.",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 8,
            AspectRatio = "adaptive",
            Resolution = "2K"
        };
        var imageRequest = new GenerationRequest
        {
            Prompt = "Animate the landscape.",
            Mode = GenerationMode.ImageToVideo,
            DurationSeconds = 8,
            AspectRatio = "16:9",
            Resolution = "2K",
            ReferenceAssetIds = [image.Id]
        };

        Assert.Throws<GenerationValidationException>(() => provider.BuildPayload(textRequest, []));
        Assert.Throws<GenerationValidationException>(() => provider.BuildPayload(imageRequest, [image]));
    }

    [Fact]
    public async Task SubmissionWithoutFreshAuthorizationCannotReachHandler()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler);
        var request = new GenerationRequest
        {
            Prompt = "A safe validation-only request.",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 8,
            AspectRatio = "1:1",
            Resolution = "768P"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SubmitAsync(request, []));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task FailedSubmissionWritesSanitizedVerboseDiagnostics()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), "ReelForge diagnostic tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var diagnosticLog = new FileApplicationDiagnosticLog(logDirectory);
            var handler = new RecordingHandler(
                HttpStatusCode.PaymentRequired,
                """{"code":"insufficient_balance","message":"Add funds.","help":"https://atlascloud.ai/billing?account=secret"}""");
            var provider = CreateProvider(handler, diagnosticLog);
            var image = CreateAsset(
                MediaType.Image,
                "first.png",
                "data:image/png;base64,THIS_INLINE_MEDIA_MUST_NOT_BE_LOGGED");
            var request = new GenerationRequest
            {
                Prompt = "A diagnostic failure test.",
                Mode = GenerationMode.ImageToVideo,
                DurationSeconds = 5,
                AspectRatio = "adaptive",
                Resolution = "768P",
                ReferenceAssetIds = [image.Id]
            };

            var exception = await Assert.ThrowsAsync<VideoGenerationProviderException>(() =>
                provider.SubmitAsync(request, [image], TestAuthorization()));

            Assert.Equal(402, exception.HttpStatus);
            Assert.Equal("insufficient_balance", exception.ProviderCode);
            Assert.Contains(logDirectory, exception.TechnicalDetails, StringComparison.Ordinal);
            Assert.Contains("event ", exception.TechnicalDetails, StringComparison.Ordinal);
            var logPath = Assert.Single(Directory.GetFiles(logDirectory, "*.jsonl"));
            var log = await File.ReadAllTextAsync(logPath);
            Assert.Contains("A diagnostic failure test.", log, StringComparison.Ordinal);
            Assert.Contains("insufficient_balance", log, StringComparison.Ordinal);
            Assert.Contains("[inline data omitted", log, StringComparison.Ordinal);
            Assert.Contains("https://atlascloud.ai/billing", log, StringComparison.Ordinal);
            Assert.DoesNotContain("THIS_INLINE_MEDIA_MUST_NOT_BE_LOGGED", log, StringComparison.Ordinal);
            Assert.DoesNotContain("account=secret", log, StringComparison.Ordinal);
            Assert.DoesNotContain("unit-test-key", log, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public void PlainTextDiagnosticsRemoveCredentialsInlineMediaAndUrlQueries()
    {
        const string input =
            "token=do-not-log data:image/png;base64,SECRETBYTES https://example.test/help?account=secret";

        var sanitized = ProviderDiagnosticSanitizer.SanitizeJsonOrText(input);

        Assert.Contains("token=[redacted]", sanitized, StringComparison.Ordinal);
        Assert.Contains("[inline data omitted", sanitized, StringComparison.Ordinal);
        Assert.Contains("https://example.test/help", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-log", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRETBYTES", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("account=secret", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollingUsesSharedAtlasCloudPredictionContract()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """{"data":{"id":"h3-123","status":"completed","outputs":["https://storage.example/h3.mp4"]}}""");
        var provider = CreateProvider(handler);

        var job = await provider.GetJobAsync("h3-123");

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://api.atlascloud.ai/api/v1/model/prediction/h3-123", handler.RequestUri?.AbsoluteUri);
        Assert.Equal(GenerationStatus.Succeeded, job.Status);
        Assert.Equal("https://storage.example/h3.mp4", Assert.Single(job.Outputs).DownloadUrl);
    }

    private static GenerationRequest ValidReferenceRequest(List<Guid> referenceIds) => new()
    {
        Prompt = "Use the supplied references.",
        Mode = GenerationMode.ReferenceToVideo,
        DurationSeconds = 8,
        AspectRatio = "adaptive",
        Resolution = "2K",
        ReferenceAssetIds = referenceIds
    };

    private static void AssertReference(Dictionary<string, string> item, string url, string type)
    {
        Assert.Equal(url, item["url"]);
        Assert.Equal(type, item["type"]);
    }

    private static GenerationSubmissionAuthorization TestAuthorization() =>
        GenerationSubmissionAuthorization.ForNetworkIsolatedTest(AtlasCloudMiniMaxH3Provider.ProviderId);

    private static AtlasCloudMiniMaxH3Provider CreateProvider(
        RecordingHandler handler,
        IApplicationDiagnosticLog? diagnosticLog = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.atlascloud.ai/") },
            new TestSecretStore("unit-test-key"),
            new ProjectAssetReferenceResolver(),
            diagnosticLog);

    private static ProjectAsset CreateAsset(MediaType type, string fileName, string providerReference)
    {
        var asset = new ProjectAsset { MediaType = type, FileName = fileName };
        asset.ProviderReferences[AtlasCloudMiniMaxH3Provider.ProviderId] = new ProviderAssetReference
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
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
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
