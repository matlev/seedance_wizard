using System.Net.Http.Headers;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class AtlasCloudAssetPreparationService : IProviderAssetPreparationService
{
    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;
    private readonly IApplicationDiagnosticLog? _diagnosticLog;

    public AtlasCloudAssetPreparationService(
        HttpClient httpClient,
        ISecretStore secretStore,
        IApplicationDiagnosticLog? diagnosticLog = null)
    {
        _httpClient = httpClient;
        _secretStore = secretStore;
        _diagnosticLog = diagnosticLog;
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
        if (!providerId.Equals(AtlasCloudSeedance25Provider.ProviderId, StringComparison.Ordinal) &&
            !providerId.Equals(AtlasCloudMiniMaxH3Provider.ProviderId, StringComparison.Ordinal))
            throw new NotSupportedException($"Provider '{providerId}' is not supported by this preparation service.");

        authorization.Demand(providerId, allowNetworkIsolatedTest: true);
        var apiKey = await _secretStore.GetAsync(AtlasCloudVideoApiClient.CredentialKey, cancellationToken)
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
            var technicalDetails = await WriteFailureDiagnosticsAsync(
                providerId,
                message,
                Path.GetFileName(media.Path),
                (int)response.StatusCode,
                "media_upload_failed",
                body).ConfigureAwait(false);
            throw new VideoGenerationProviderException(
                $"AtlasCloud media upload failed with HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                "media_upload_failed",
                technicalDetails);
        }

        string? url;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            url = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                  data.TryGetProperty("download_url", out var downloadUrl) && downloadUrl.ValueKind == JsonValueKind.String
                ? downloadUrl.GetString()
                : root.TryGetProperty("url", out var rootUrl) && rootUrl.ValueKind == JsonValueKind.String
                    ? rootUrl.GetString()
                    : null;
        }
        catch (JsonException exception)
        {
            var technicalDetails = await WriteFailureDiagnosticsAsync(
                providerId,
                message,
                Path.GetFileName(media.Path),
                (int)response.StatusCode,
                "invalid_upload_response",
                body,
                exception.ToString()).ConfigureAwait(false);
            throw InvalidResponse(technicalDetails, exception);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            var technicalDetails = await WriteFailureDiagnosticsAsync(
                providerId,
                message,
                Path.GetFileName(media.Path),
                (int)response.StatusCode,
                "invalid_upload_response",
                body).ConfigureAwait(false);
            throw InvalidResponse(technicalDetails);
        }

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

    private static VideoGenerationProviderException InvalidResponse(string technicalDetails, Exception? inner = null) =>
        new(
            "AtlasCloud returned an unreadable media-upload response.",
            providerCode: "invalid_upload_response",
            technicalDetails: technicalDetails,
            innerException: inner);

    private async Task<string> WriteFailureDiagnosticsAsync(
        string providerId,
        HttpRequestMessage request,
        string mediaFileName,
        int httpStatus,
        string providerCode,
        string responseBody,
        string? exception = null)
    {
        if (_diagnosticLog is null)
            return "Verbose provider diagnostics are unavailable in this runtime.";

        var details = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["providerId"] = providerId,
            ["operation"] = "media upload",
            ["httpMethod"] = request.Method.Method,
            ["requestUri"] = request.RequestUri is null
                ? null
                : new UriBuilder(request.RequestUri) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri,
            ["httpStatus"] = httpStatus.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["providerCode"] = providerCode,
            ["mediaFileName"] = mediaFileName,
            ["responseBody"] = ProviderDiagnosticSanitizer.SanitizeJsonOrText(responseBody),
            ["exception"] = exception
        };
        var reference = await _diagnosticLog.WriteErrorAsync(
            "provider.atlascloud",
            "AtlasCloud media upload failed.",
            details,
            CancellationToken.None).ConfigureAwait(false);

        return reference is null
            ? $"Verbose diagnostics could not be written to '{_diagnosticLog.LogDirectory}'."
            : $"Verbose diagnostics: {reference.FilePath} (event {reference.EventId}).";
    }

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
