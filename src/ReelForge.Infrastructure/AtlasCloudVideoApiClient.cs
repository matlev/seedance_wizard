using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class AtlasCloudVideoApiClient
{
    public const string CredentialKey = "atlascloud.api-key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;
    private readonly IApplicationDiagnosticLog? _diagnosticLog;

    public AtlasCloudVideoApiClient(
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

    public async Task<GenerationSubmission> SubmitAsync(
        IReadOnlyDictionary<string, object?> payload,
        GenerationSubmissionAuthorization? authorization,
        string authorizationProviderId,
        CancellationToken cancellationToken = default)
    {
        authorization?.Demand(authorizationProviderId, allowNetworkIsolatedTest: true);
        if (authorization is null)
        {
            throw new InvalidOperationException(
                "AtlasCloud submission requires a fresh confirmation created by an explicit user action.");
        }

        var apiKey = await GetApiKeyAsync(cancellationToken).ConfigureAwait(false);
        var requestBody = JsonSerializer.Serialize(payload, JsonOptions);
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/model/generateVideo")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var (providerCode, providerMessage) = ReadProviderError(responseBody);
            var technicalDetails = await WriteFailureDiagnosticsAsync(
                authorizationProviderId,
                "generation submission",
                message,
                (int)response.StatusCode,
                providerCode,
                requestBody,
                responseBody).ConfigureAwait(false);
            throw new VideoGenerationProviderException(
                providerMessage ?? $"AtlasCloud rejected the generation request with HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                providerCode,
                technicalDetails);
        }

        try
        {
            return ParseSubmission(responseBody);
        }
        catch (VideoGenerationProviderException exception)
        {
            var technicalDetails = await WriteFailureDiagnosticsAsync(
                authorizationProviderId,
                "generation response parsing",
                message,
                (int)response.StatusCode,
                exception.ProviderCode,
                requestBody,
                responseBody,
                exception.InnerException?.ToString()).ConfigureAwait(false);
            throw new VideoGenerationProviderException(
                exception.Message,
                exception.HttpStatus,
                exception.ProviderCode,
                technicalDetails,
                exception.InnerException);
        }
    }

    public async Task<ProviderGenerationJob> GetJobAsync(
        string providerId,
        string providerJobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerJobId);
        var apiKey = await GetApiKeyAsync(cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/v1/model/prediction/{Uri.EscapeDataString(providerJobId)}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var (providerCode, providerMessage) = ReadProviderError(responseBody);
            var technicalDetails = await WriteFailureDiagnosticsAsync(
                providerId,
                "prediction polling",
                message,
                (int)response.StatusCode,
                providerCode,
                null,
                responseBody).ConfigureAwait(false);
            throw new VideoGenerationProviderException(
                providerMessage ?? $"AtlasCloud prediction polling failed with HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                providerCode,
                technicalDetails);
        }

        try
        {
            return ParseJob(responseBody, providerJobId);
        }
        catch (VideoGenerationProviderException exception)
        {
            var technicalDetails = await WriteFailureDiagnosticsAsync(
                providerId,
                "prediction response parsing",
                message,
                (int)response.StatusCode,
                exception.ProviderCode,
                null,
                responseBody,
                exception.InnerException?.ToString()).ConfigureAwait(false);
            throw new VideoGenerationProviderException(
                exception.Message,
                exception.HttpStatus,
                exception.ProviderCode,
                technicalDetails,
                exception.InnerException);
        }
    }

    private static GenerationSubmission ParseSubmission(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var data = root.TryGetProperty("data", out var wrappedData) && wrappedData.ValueKind == JsonValueKind.Object
                ? wrappedData
                : root;
            var id = ReadScalar(data, "id");
            if (string.IsNullOrWhiteSpace(id))
                throw new JsonException("The response did not include data.id or id.");

            var providerStatus = ReadScalar(data, "status") ?? "processing";
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["providerStatus"] = providerStatus
            };
            foreach (var name in new[] { "model", "created_at", "completion_tokens", "total_tokens" })
            {
                if (ReadScalar(data, name) is { } value) metadata[name] = value;
            }

            return new GenerationSubmission
            {
                ProviderJobId = id,
                Status = providerStatus.ToLowerInvariant() switch
                {
                    "completed" => GenerationStatus.Succeeded,
                    "failed" or "timeout" => GenerationStatus.Failed,
                    "processing" => GenerationStatus.Running,
                    _ => GenerationStatus.Queued
                },
                ResponseMetadata = metadata
            };
        }
        catch (JsonException exception)
        {
            throw new VideoGenerationProviderException(
                "AtlasCloud returned an unreadable generation response.",
                providerCode: "invalid_response",
                innerException: exception);
        }
    }

    private static ProviderGenerationJob ParseJob(string responseBody, string expectedJobId)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var data = root.TryGetProperty("data", out var wrappedData) && wrappedData.ValueKind == JsonValueKind.Object
                ? wrappedData
                : root;
            var id = ReadScalar(data, "id") ?? expectedJobId;
            var providerStatus = ReadScalar(data, "status")
                ?? throw new JsonException("The prediction response did not include a status.");
            var status = providerStatus.ToLowerInvariant() switch
            {
                "completed" or "succeeded" => GenerationStatus.Succeeded,
                "failed" or "timeout" => GenerationStatus.Failed,
                "processing" or "running" => GenerationStatus.Running,
                "queued" or "pending" or "starting" => GenerationStatus.Queued,
                _ => GenerationStatus.Running
            };

            var outputs = new List<ProviderGenerationOutput>();
            if (data.TryGetProperty("outputs", out var outputArray) && outputArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in outputArray.EnumerateArray())
                {
                    var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                        throw new JsonException("AtlasCloud returned a non-HTTPS output URL.");
                    outputs.Add(new ProviderGenerationOutput(uri.AbsoluteUri));
                }
            }

            if (status == GenerationStatus.Succeeded && outputs.Count == 0)
                throw new JsonException("AtlasCloud reported completion without an output URL.");

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["providerStatus"] = providerStatus,
                ["outputCount"] = outputs.Count.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var name in new[] { "model", "created_at", "completed_at" })
            {
                if (ReadScalar(data, name) is { } value) metadata[name] = value;
            }

            var errorMessage = ReadScalar(data, "error");
            return new ProviderGenerationJob
            {
                ProviderJobId = id,
                Status = status,
                Outputs = outputs,
                Error = status == GenerationStatus.Failed
                    ? new GenerationError
                    {
                        ProviderCode = "provider_generation_failed",
                        Message = errorMessage ?? "AtlasCloud reported that generation failed."
                    }
                    : null,
                ResponseMetadata = metadata
            };
        }
        catch (JsonException exception)
        {
            throw new VideoGenerationProviderException(
                "AtlasCloud returned an unreadable prediction response.",
                providerCode: "invalid_prediction_response",
                innerException: exception);
        }
    }

    private async Task<string> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        var apiKey = await _secretStore.GetAsync(CredentialKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new VideoGenerationProviderException(
                "An AtlasCloud API key is required. Store it in Windows Credential Manager before submitting.",
                providerCode: "missing_api_key");
        }

        return apiKey;
    }

    private static (string? Code, string? Message) ReadProviderError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            return (ReadScalar(root, "code"), ReadScalar(root, "message") ?? ReadScalar(root, "error"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private async Task<string> WriteFailureDiagnosticsAsync(
        string providerId,
        string operation,
        HttpRequestMessage request,
        int httpStatus,
        string? providerCode,
        string? requestBody,
        string responseBody,
        string? exception = null)
    {
        if (_diagnosticLog is null)
            return "Verbose provider diagnostics are unavailable in this runtime.";

        var details = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["providerId"] = providerId,
            ["operation"] = operation,
            ["httpMethod"] = request.Method.Method,
            ["requestUri"] = SanitizeUri(request.RequestUri),
            ["httpStatus"] = httpStatus.ToString(CultureInfo.InvariantCulture),
            ["providerCode"] = providerCode,
            ["requestBody"] = requestBody is null ? null : ProviderDiagnosticSanitizer.SanitizeJsonOrText(requestBody),
            ["responseBody"] = ProviderDiagnosticSanitizer.SanitizeJsonOrText(responseBody),
            ["exception"] = exception
        };
        var reference = await _diagnosticLog.WriteErrorAsync(
            "provider.atlascloud",
            $"AtlasCloud {operation} failed.",
            details,
            CancellationToken.None).ConfigureAwait(false);

        return reference is null
            ? $"Verbose diagnostics could not be written to '{_diagnosticLog.LogDirectory}'."
            : $"Verbose diagnostics: {reference.FilePath} (event {reference.EventId}).";
    }

    private static string? SanitizeUri(Uri? uri)
    {
        if (uri is null) return null;
        return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
    }

    private static string? ReadScalar(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }
}
