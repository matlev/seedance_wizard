using System.Net;
using System.Security.Cryptography;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class CloudflareR2TemporaryAssetHostTests
{
    [Fact]
    public async Task IdenticalContentUploadsOnceAndGetsFreshReadUrlEachTime()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "same media bytes");
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
            var client = new FakeR2Client();
            var host = CreateHost(client);
            var logical = new GenerationReferenceSnapshot
            {
                ObjectKind = GenerationReferenceObjectKind.Asset,
                LogicalObjectId = Guid.NewGuid(),
                RecipeRevisionId = Guid.NewGuid(),
                ContentHash = hash
            };
            await using var media = new MaterializedMediaLease(
                path,
                new ContentIdentity { Sha256 = hash, Status = ContentHashStatus.Verified },
                null,
                false);
            var request = new TemporaryAssetHostRequest(logical, media, "video/mp4", TimeSpan.FromMinutes(30));

            var first = await host.EnsureHostedAsync(request);
            var second = await host.EnsureHostedAsync(request);

            Assert.True(first.Uploaded);
            Assert.False(second.Uploaded);
            Assert.Equal(1, client.UploadCalls);
            Assert.Equal(2, client.PresignCalls);
            Assert.Equal(first.ObjectKey, second.ObjectKey);
            Assert.StartsWith($"references/sha256/{hash[..2]}/{hash}", first.ObjectKey, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(GenerationReferenceObjectKind.Asset, true)]
    [InlineData(GenerationReferenceObjectKind.FrameAnchor, false)]
    public async Task BytePlusPreparationPreservesLogicalReferenceAndHidesSignedUrlFromReceipt(
        GenerationReferenceObjectKind objectKind,
        bool hasRecipeRevision)
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".png");
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
            const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var logical = new GenerationReferenceSnapshot
            {
                ObjectKind = objectKind,
                LogicalObjectId = Guid.NewGuid(),
                RecipeRevisionId = hasRecipeRevision ? Guid.NewGuid() : null,
                ContentHash = hash
            };
            await using var media = new MaterializedMediaLease(
                path,
                new ContentIdentity { Sha256 = hash, Status = ContentHashStatus.Verified },
                null,
                false);
            var service = new BytePlusModelArkAssetPreparationService(CreateHost(new FakeR2Client()));

            var prepared = await service.PrepareAsync(
                BytePlusModelArkSeedance25Provider.ProviderId,
                logical,
                media,
                GenerationSubmissionAuthorization.ForNetworkIsolatedTest(BytePlusModelArkSeedance25Provider.ProviderId));

            Assert.Same(logical, prepared.LogicalReference);
            Assert.StartsWith("https://r2.test/", prepared.ProviderRepresentation, StringComparison.Ordinal);
            Assert.Equal(hash, prepared.Receipt!.ProducedContentHash);
            Assert.Equal("temporary-host:cloudflare-r2", prepared.Receipt.ProviderScope);
            Assert.DoesNotContain("?signature=", prepared.Receipt.ProviderReferenceId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task R2SignerUsesMockedHttpAndNeverPlacesSecretInPresignedUrl()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);
        var client = new CloudflareR2S3Client(
            httpClient,
            new Uri("https://account.r2.cloudflarestorage.com"),
            "access-id",
            "super-secret-value",
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero)));

        await client.ProbeBucketAsync("private-bucket");
        var url = client.CreatePresignedGetUrl("private-bucket", "references/sha256/aa/file.mp4", TimeSpan.FromHours(1));

        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Head, handler.Requests[0].Method);
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=access-id/", handler.Requests[0].Authorization, StringComparison.Ordinal);
        Assert.Contains("X-Amz-Signature=", url.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", url.AbsoluteUri, StringComparison.Ordinal);
    }

    private static CloudflareR2TemporaryAssetHost CreateHost(FakeR2Client client)
    {
        var settings = new ApplicationSettings();
        settings.TemporaryAssetHosting.CloudflareR2.AccountId = "account";
        settings.TemporaryAssetHosting.CloudflareR2.BucketName = "private-bucket";
        settings.TemporaryAssetHosting.CloudflareR2.Endpoint = "https://account.r2.cloudflarestorage.com";
        var secrets = new DictionarySecretStore(new Dictionary<string, string>
        {
            [CloudflareR2TemporaryAssetHost.AccessKeyCredentialKey] = "access",
            [CloudflareR2TemporaryAssetHost.SecretAccessKeyCredentialKey] = "secret"
        });
        return new CloudflareR2TemporaryAssetHost(
            new StaticSettingsStore(settings),
            secrets,
            new FakeR2ClientFactory(client),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero)));
    }

    private sealed class StaticSettingsStore(ApplicationSettings settings) : IApplicationSettingsStore
    {
        public string LocalSettingsPath => "memory";
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task SaveAsync(ApplicationSettings value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DictionarySecretStore(Dictionary<string, string> values) : ISecretStore
    {
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            values[key] = value;
            return Task.CompletedTask;
        }
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(values.GetValueOrDefault(key));
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeR2ClientFactory(FakeR2Client client) : ICloudflareR2ClientFactory
    {
        public ICloudflareR2Client Create(CloudflareR2Settings settings, string accessKeyId, string secretAccessKey) => client;
    }

    private sealed class FakeR2Client : ICloudflareR2Client
    {
        private readonly HashSet<string> _objects = new(StringComparer.Ordinal);
        public int UploadCalls { get; private set; }
        public int PresignCalls { get; private set; }
        public Task<bool> ObjectExistsAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_objects.Contains(objectKey));
        public Task UploadAsync(string bucketName, string objectKey, string filePath, string contentType, string contentSha256, CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            _objects.Add(objectKey);
            return Task.CompletedTask;
        }
        public Uri CreatePresignedGetUrl(string bucketName, string objectKey, TimeSpan lifetime)
        {
            PresignCalls++;
            return new Uri($"https://r2.test/{objectKey}?signature={PresignCalls}");
        }
        public Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
        {
            _objects.Remove(objectKey);
            return Task.CompletedTask;
        }
        public Task ProbeBucketAsync(string bucketName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Authorization)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.Headers.Authorization?.ToString() ?? request.Headers.GetValues("Authorization").Single()));
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(string.Empty) });
        }
    }
}
