using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public interface ICloudflareR2Client
{
    Task<bool> ObjectExistsAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default);
    Task UploadAsync(
        string bucketName,
        string objectKey,
        string filePath,
        string contentType,
        string contentSha256,
        CancellationToken cancellationToken = default);
    Uri CreatePresignedGetUrl(string bucketName, string objectKey, TimeSpan lifetime);
    Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default);
    Task ProbeBucketAsync(string bucketName, CancellationToken cancellationToken = default);
}

public interface ICloudflareR2ClientFactory
{
    ICloudflareR2Client Create(CloudflareR2Settings settings, string accessKeyId, string secretAccessKey);
}

public sealed class CloudflareR2ClientFactory : ICloudflareR2ClientFactory
{
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public CloudflareR2ClientFactory(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ICloudflareR2Client Create(CloudflareR2Settings settings, string accessKeyId, string secretAccessKey) =>
        new CloudflareR2S3Client(
            _httpClient,
            new Uri(settings.Endpoint, UriKind.Absolute),
            accessKeyId,
            secretAccessKey,
            _timeProvider);
}

public sealed class CloudflareR2TemporaryAssetHost : ITemporaryAssetHost
{
    public const string HostingProviderId = "cloudflare-r2";
    public const string AccessKeyCredentialKey = "cloudflare.r2.access-key-id";
    public const string SecretAccessKeyCredentialKey = "cloudflare.r2.secret-access-key";

    private readonly IApplicationSettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;
    private readonly ICloudflareR2ClientFactory _clientFactory;
    private readonly TimeProvider _timeProvider;

    public CloudflareR2TemporaryAssetHost(
        IApplicationSettingsStore settingsStore,
        ISecretStore secretStore,
        ICloudflareR2ClientFactory clientFactory,
        TimeProvider? timeProvider = null)
    {
        _settingsStore = settingsStore;
        _secretStore = secretStore;
        _clientFactory = clientFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string ProviderId => HostingProviderId;

    public async Task<HostedAssetReference> EnsureHostedAsync(
        TemporaryAssetHostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (settings, client) = await CreateConfiguredClientAsync(cancellationToken).ConfigureAwait(false);
        var hash = NormalizeHash(request.Media.ContentIdentity.Sha256);
        var extension = NormalizeExtension(Path.GetExtension(request.Media.Path));
        var objectKey = $"references/sha256/{hash[..2]}/{hash}{extension}";

        var exists = await client.ObjectExistsAsync(settings.BucketName, objectKey, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            await client.UploadAsync(
                    settings.BucketName,
                    objectKey,
                    request.Media.Path,
                    request.ContentType,
                    hash,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var lifetime = request.ReadUrlLifetime <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(settings.PresignedUrlLifetimeMinutes)
            : request.ReadUrlLifetime;
        if (lifetime < TimeSpan.FromSeconds(1) || lifetime > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(request), "Presigned URL lifetime must be between 1 second and 7 days.");

        var url = client.CreatePresignedGetUrl(settings.BucketName, objectKey, lifetime);
        return new HostedAssetReference(
            ProviderId,
            objectKey,
            hash,
            url,
            _timeProvider.GetUtcNow().Add(lifetime),
            Uploaded: !exists);
    }

    public async Task RemoveAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var (settings, client) = await CreateConfiguredClientAsync(cancellationToken).ConfigureAwait(false);
        await client.DeleteAsync(settings.BucketName, objectKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (settings, client) = await CreateConfiguredClientAsync(cancellationToken).ConfigureAwait(false);
            await client.ProbeBucketAsync(settings.BucketName, cancellationToken).ConfigureAwait(false);
            return new ConnectionTestResult(
                true,
                $"Successfully connected to R2 bucket '{settings.BucketName}'.");
        }
        catch (TemporaryAssetHostException exception)
        {
            return new ConnectionTestResult(false, exception.Message, exception.FailureKind);
        }
        catch (HttpRequestException exception)
        {
            return new ConnectionTestResult(false, $"R2 network request failed: {exception.Message}", ConnectionFailureKind.NetworkFailure);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, $"R2 connection test failed: {exception.Message}", ConnectionFailureKind.Unknown);
        }
    }

    private async Task<(CloudflareR2Settings Settings, ICloudflareR2Client Client)> CreateConfiguredClientAsync(
        CancellationToken cancellationToken)
    {
        var applicationSettings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!applicationSettings.TemporaryAssetHosting.Provider.Equals("CloudflareR2", StringComparison.OrdinalIgnoreCase))
            throw new TemporaryAssetHostException(
                $"Temporary asset hosting provider '{applicationSettings.TemporaryAssetHosting.Provider}' is not available.",
                ConnectionFailureKind.MissingConfiguration);
        var settings = applicationSettings.TemporaryAssetHosting.CloudflareR2;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.AccountId)) missing.Add("Account ID");
        if (string.IsNullOrWhiteSpace(settings.BucketName)) missing.Add("Bucket name");
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            missing.Add("valid HTTPS S3 endpoint");
        if (missing.Count > 0)
            throw new TemporaryAssetHostException(
                $"Cloudflare R2 is not fully configured. Missing: {string.Join(", ", missing)}.",
                ConnectionFailureKind.MissingConfiguration);

        var accessKeyId = await _secretStore.GetAsync(AccessKeyCredentialKey, cancellationToken).ConfigureAwait(false);
        var secretAccessKey = await _secretStore.GetAsync(SecretAccessKeyCredentialKey, cancellationToken).ConfigureAwait(false);
        var missingCredentials = new List<string>();
        if (string.IsNullOrWhiteSpace(accessKeyId)) missingCredentials.Add("Access Key ID");
        if (string.IsNullOrWhiteSpace(secretAccessKey)) missingCredentials.Add("Secret Access Key");
        if (missingCredentials.Count > 0)
            throw new TemporaryAssetHostException(
                $"Cloudflare R2 is missing credential: {string.Join(", ", missingCredentials)}.",
                ConnectionFailureKind.MissingCredential);

        return (settings, _clientFactory.Create(settings, accessKeyId!, secretAccessKey!));
    }

    private static string NormalizeHash(string? hash)
    {
        if (hash is null || hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Temporary hosting requires a verified SHA-256 content identity.");
        return hash.ToLowerInvariant();
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.ToLowerInvariant();
        return extension.Length is >= 2 and <= 10 && extension[0] == '.' &&
               extension.Skip(1).All(character => char.IsAsciiLetterOrDigit(character))
            ? extension
            : ".bin";
    }
}

public sealed class TemporaryAssetHostException : Exception
{
    public TemporaryAssetHostException(
        string message,
        ConnectionFailureKind failureKind,
        int? httpStatus = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        HttpStatus = httpStatus;
    }

    public ConnectionFailureKind FailureKind { get; }
    public int? HttpStatus { get; }
}

public sealed class CloudflareR2S3Client : ICloudflareR2Client
{
    private const string Region = "auto";
    private const string Service = "s3";
    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";
    private static readonly string EmptyPayloadHash = Hex(SHA256.HashData([]));

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly TimeProvider _timeProvider;

    public CloudflareR2S3Client(
        HttpClient httpClient,
        Uri endpoint,
        string accessKeyId,
        string secretAccessKey,
        TimeProvider? timeProvider = null)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new ArgumentException("Cloudflare R2 endpoint must be a credential-free HTTPS origin.", nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretAccessKey);
        _httpClient = httpClient;
        _endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/", UriKind.Absolute);
        _accessKeyId = accessKeyId;
        _secretAccessKey = secretAccessKey;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> ObjectExistsAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendSignedAsync(
                HttpMethod.Head,
                ObjectUri(bucketName, objectKey),
                EmptyPayloadHash,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        EnsureSuccess(response, "inspect the temporary object");
        return true;
    }

    public async Task UploadAsync(
        string bucketName,
        string objectKey,
        string filePath,
        string contentType,
        string contentSha256,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var response = await SendSignedAsync(
                HttpMethod.Put,
                ObjectUri(bucketName, objectKey),
                contentSha256,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, "upload the temporary object");
    }

    public Uri CreatePresignedGetUrl(string bucketName, string objectKey, TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.FromSeconds(1) || lifetime > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var timestamp = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var credentialScope = $"{date}/{Region}/{Service}/aws4_request";
        var canonicalUri = ObjectUri(bucketName, objectKey).AbsolutePath;
        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = "AWS4-HMAC-SHA256",
            ["X-Amz-Content-Sha256"] = UnsignedPayload,
            ["X-Amz-Credential"] = $"{_accessKeyId}/{credentialScope}",
            ["X-Amz-Date"] = timestamp,
            ["X-Amz-Expires"] = ((int)lifetime.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            ["X-Amz-SignedHeaders"] = "host"
        };
        var canonicalQuery = CanonicalQuery(query);
        var canonicalHeaders = $"host:{HostHeader(_endpoint)}\n";
        var canonicalRequest = $"GET\n{canonicalUri}\n{canonicalQuery}\n{canonicalHeaders}\nhost\n{UnsignedPayload}";
        var stringToSign = StringToSign(timestamp, credentialScope, canonicalRequest);
        query["X-Amz-Signature"] = Hex(Hmac(SigningKey(date), stringToSign));
        return new Uri($"{_endpoint.GetLeftPart(UriPartial.Authority)}{canonicalUri}?{CanonicalQuery(query)}", UriKind.Absolute);
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        using var response = await SendSignedAsync(
                HttpMethod.Delete,
                ObjectUri(bucketName, objectKey),
                EmptyPayloadHash,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, "delete the temporary object");
    }

    public async Task ProbeBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        using var response = await SendSignedAsync(
                HttpMethod.Head,
                BucketUri(bucketName),
                EmptyPayloadHash,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, "access the configured bucket");
    }

    private async Task<HttpResponseMessage> SendSignedAsync(
        HttpMethod method,
        Uri uri,
        string payloadHash,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var timestamp = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = $"{date}/{Region}/{Service}/aws4_request";
        var canonicalHeaders = $"host:{HostHeader(uri)}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{timestamp}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"{method.Method}\n{uri.AbsolutePath}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var signature = Hex(Hmac(SigningKey(date), StringToSign(timestamp, scope, canonicalRequest)));

        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Host = HostHeader(uri);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", timestamp);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={_accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            request.Content = null;
            request.Dispose();
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ConnectionFailureKind.AuthenticationRejected,
            HttpStatusCode.Forbidden => ConnectionFailureKind.InsufficientPermissions,
            HttpStatusCode.NotFound => ConnectionFailureKind.EndpointUnavailable,
            _ when status >= 500 => ConnectionFailureKind.EndpointUnavailable,
            _ => ConnectionFailureKind.Unknown
        };
        throw new TemporaryAssetHostException(
            $"Cloudflare R2 could not {operation} (HTTP {status}).",
            kind,
            status);
    }

    private Uri BucketUri(string bucketName) =>
        new($"{_endpoint.GetLeftPart(UriPartial.Authority)}/{EncodeSegment(bucketName)}", UriKind.Absolute);

    private Uri ObjectUri(string bucketName, string objectKey) =>
        new($"{_endpoint.GetLeftPart(UriPartial.Authority)}/{EncodeSegment(bucketName)}/{EncodePath(objectKey)}", UriKind.Absolute);

    private static string EncodePath(string value) => string.Join('/', value.Split('/').Select(EncodeSegment));
    private static string EncodeSegment(string value) => Uri.EscapeDataString(value);
    private static string HostHeader(Uri uri) => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    private static string CanonicalQuery(IEnumerable<KeyValuePair<string, string>> query) =>
        string.Join("&", query.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

    private static string StringToSign(string timestamp, string scope, string canonicalRequest) =>
        $"AWS4-HMAC-SHA256\n{timestamp}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

    private byte[] SigningKey(string date)
    {
        var dateKey = Hmac(Encoding.UTF8.GetBytes("AWS4" + _secretAccessKey), date);
        var regionKey = Hmac(dateKey, Region);
        var serviceKey = Hmac(regionKey, Service);
        return Hmac(serviceKey, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
