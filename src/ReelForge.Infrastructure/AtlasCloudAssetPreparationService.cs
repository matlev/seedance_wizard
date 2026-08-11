using System.Net.Http.Headers;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class AtlasCloudAssetPreparationService : IProviderAssetPreparationService
{
    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;

    public AtlasCloudAssetPreparationService(HttpClient httpClient, ISecretStore secretStore)
    {
        _httpClient = httpClient;
        _secretStore = secretStore;
        _httpClient.BaseAddress ??= new Uri("https://api.atlascloud.ai/");
        if (_httpClient.BaseAddress.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The AtlasCloud API base address must use HTTPS.", nameof(httpClient));
    }

    public async Task<PreparedProviderReference> PrepareAsync(
        string providerId,
        GenerationReferenceSnapshot logicalReference,
        MaterializedMediaLease media,
        GenerationSubmissionAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        if (!providerId.Equals(AtlasCloudSeedance25Provider.ProviderId, StringComparison.Ordinal))
            throw new NotSupportedException($"Provider '{providerId}' is not supported by this preparation service.");

        authorization.Demand(providerId, allowNetworkIsolatedTest: true);
        var apiKey = await _secretStore.GetAsync(AtlasCloudSeedance25Provider.CredentialKey, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new VideoGenerationProviderException("An AtlasCloud API key is required.", providerCode: "missing_api_key");

        await using var stream = new FileStream(
            media.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetMediaType(media.Path));
        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "file", Path.GetFileName(media.Path));
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/model/uploadMedia") { Content = form };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new VideoGenerationProviderException(
                $"AtlasCloud media upload failed with HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                "media_upload_failed",
                "AtlasCloud response body omitted from durable diagnostics.");
        }

        string? url;
        try
        {
            using var document = JsonDocument.Parse(body);
            url = document.RootElement.TryGetProperty("url", out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException exception)
        {
            throw InvalidResponse(exception);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw InvalidResponse();

        return new PreparedProviderReference(
            logicalReference,
            uri.AbsoluteUri,
            new MaterializationReceipt
            {
                SourceContentHash = logicalReference.ContentHash,
                ProducedContentHash = media.ContentIdentity.Sha256,
                Encoding = media.Encoding,
                ProviderScope = "temporary-upload"
            });
    }

    private static VideoGenerationProviderException InvalidResponse(Exception? inner = null) =>
        new(
            "AtlasCloud returned an unreadable media-upload response.",
            providerCode: "invalid_upload_response",
            technicalDetails: "AtlasCloud response body omitted from durable diagnostics.",
            innerException: inner);

    private static string GetMediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        _ => "application/octet-stream"
    };
}
