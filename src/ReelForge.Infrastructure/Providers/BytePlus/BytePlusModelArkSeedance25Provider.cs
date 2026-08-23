using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class BytePlusModelArkSeedance25Provider :
    IAsyncVideoGenerationProvider,
    IApiKeyVideoGenerationProvider
{
    public const string ProviderId = "byteplus.modelark";
    public const string CredentialKey = "byteplus.modelark.api-key";
    public const string ModelId = "dreamina-seedance-2-5-260628";

    private const int MaximumRequestBodyBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedParameterNames = new(StringComparer.Ordinal)
    {
        "output_format", "generate_audio", "watermark", "return_last_frame"
    };

    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;
    private readonly IProviderAssetReferenceResolver _assetReferenceResolver;

    public BytePlusModelArkSeedance25Provider(
        HttpClient httpClient,
        ISecretStore secretStore,
        IProviderAssetReferenceResolver assetReferenceResolver)
    {
        _httpClient = httpClient;
        _secretStore = secretStore;
        _assetReferenceResolver = assetReferenceResolver;
        _httpClient.BaseAddress ??= new Uri("https://ark.ap-southeast.bytepluses.com/api/v3/");
        if (_httpClient.BaseAddress.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The BytePlus ModelArk API base address must use HTTPS.", nameof(httpClient));
    }

    public GenerationProviderCapabilities Capabilities { get; } = new(
        ProviderId,
        "BytePlus ModelArk Seedance 2.5",
        ModelId,
        [GenerationMode.TextToVideo, GenerationMode.ImageToVideo, GenerationMode.ReferenceToVideo],
        4,
        30,
        ["16:9", "4:3", "1:1", "3:4", "9:16", "21:9", "adaptive"],
        ["480p", "720p"],
        30,
        10,
        10,
        new HashSet<MediaType> { MediaType.Image, MediaType.Video, MediaType.Audio },
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["output_format"] = ["mp4", "mov"],
            ["generate_audio"] = ["true", "false"],
            ["watermark"] = ["true", "false"],
            ["return_last_frame"] = ["true", "false"]
        });

    public GenerationProviderCostBehavior CostBehavior => GenerationProviderCostBehavior.PotentiallyBillable;
    public string ApiKeyCredentialKey => CredentialKey;

    public async Task<GenerationSubmission> SubmitAsync(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets,
        GenerationSubmissionAuthorization? authorization = null,
        CancellationToken cancellationToken = default)
    {
        authorization?.Demand(ProviderId, allowNetworkIsolatedTest: true);
        if (authorization is null)
            throw new InvalidOperationException(
                "BytePlus submission requires a fresh confirmation created by an explicit user action.");

        var payload = BuildPayload(request, projectAssets);
        var apiKey = await GetApiKeyAsync(cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(HttpMethod.Post, "contents/generations/tasks")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateHttpException(responseBody, response.StatusCode, "BytePlus rejected the generation request");
        return ParseSubmission(responseBody);
    }

    public async Task<ProviderGenerationJob> GetJobAsync(
        string providerJobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerJobId);
        var apiKey = await GetApiKeyAsync(cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"contents/generations/tasks/{Uri.EscapeDataString(providerJobId)}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateHttpException(responseBody, response.StatusCode, "BytePlus task polling failed");
        return ParseJob(responseBody, providerJobId);
    }

    public IReadOnlyDictionary<string, object?> BuildPayload(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projectAssets);
        var errors = Capabilities.Validate(request, projectAssets).ToList();
        foreach (var parameter in request.ProviderParameters.Keys.Where(key => !SupportedParameterNames.Contains(key)))
            errors.Add($"BytePlus parameter '{parameter}' is not supported by this adapter.");

        var references = GenerationRequestReferenceResolver.Resolve(request, projectAssets)
            .OrderBy(reference => reference.Order)
            .ToArray();
        if (request.Mode == GenerationMode.ImageToVideo)
        {
            if (!request.AspectRatio.Equals("adaptive", StringComparison.OrdinalIgnoreCase))
                errors.Add("BytePlus Seedance 2.5 first-frame video generation requires the adaptive ratio.");
            if (references.Length is < 1 or > 2 || references.Any(asset => asset.MediaType != MediaType.Image))
                errors.Add("BytePlus image-to-video requires one or two image references and no video or audio references.");
            if (references.Any(reference => reference.Role == GenerationReferenceRole.EndFrame) &&
                references.All(reference => reference.Role == GenerationReferenceRole.EndFrame))
                errors.Add("BytePlus first/last-frame generation requires a first-frame image when a last-frame image is supplied.");
        }

        var resolvedReferences = new List<string>();
        for (var index = 0; index < references.Length; index++)
        {
            var asset = references[index];
            var resolved = asset.PreparedRepresentation
                ?? (asset.Asset is null ? null : _assetReferenceResolver.Resolve(ProviderId, asset.Asset));
            if (string.IsNullOrWhiteSpace(resolved))
            {
                errors.Add($"{asset.DisplayName} has no prepared BytePlus reference.");
                continue;
            }
            if (!IsSupportedRepresentation(asset.MediaType, resolved))
            {
                errors.Add($"{asset.DisplayName} has an unsupported BytePlus reference representation.");
                continue;
            }
            resolvedReferences.Add(resolved);
        }

        if (errors.Count > 0)
            throw new GenerationValidationException(errors.Distinct(StringComparer.Ordinal).ToList());

        var content = new List<object>
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "text",
                ["text"] = request.Prompt
            }
        };
        for (var index = 0; index < references.Length; index++)
        {
            var asset = references[index];
            var typeName = asset.MediaType switch
            {
                MediaType.Image => "image_url",
                MediaType.Video => "video_url",
                MediaType.Audio => "audio_url",
                _ => throw new InvalidOperationException($"Unsupported reference media type '{asset.MediaType}'.")
            };
            var role = asset.Role switch
            {
                GenerationReferenceRole.StartFrame => "first_frame",
                GenerationReferenceRole.EndFrame => "last_frame",
                _ when request.Mode == GenerationMode.ImageToVideo => index == 0 ? "first_frame" : "last_frame",
                _ => asset.MediaType switch
                {
                    MediaType.Image => "reference_image",
                    MediaType.Video => "reference_video",
                    MediaType.Audio => "reference_audio",
                    _ => throw new InvalidOperationException($"Unsupported reference media type '{asset.MediaType}'.")
                }
            };
            content.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = typeName,
                [typeName] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["url"] = resolvedReferences[index]
                },
                ["role"] = role
            });
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = ModelId,
            ["content"] = content,
            ["duration"] = request.DurationSeconds,
            ["resolution"] = request.Resolution,
            ["ratio"] = request.AspectRatio,
            ["generate_audio"] = ReadBoolean(request, "generate_audio", true),
            ["watermark"] = ReadBoolean(request, "watermark", false),
            ["return_last_frame"] = ReadBoolean(request, "return_last_frame", false),
            ["output_format"] = ReadChoice(request, "output_format", ["mp4", "mov"], "mp4")
        };
        if (resolvedReferences.Any(value => value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) &&
            JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions).Length >= MaximumRequestBodyBytes)
        {
            throw new GenerationValidationException(
                ["The inline BytePlus request would exceed the documented 64 MB request-body limit."]);
        }
        return payload;
    }

    private static string? GetPreparedRepresentation(GenerationRequest request, int index, Guid logicalObjectId)
    {
        var ordered = request.PreparedReferences.OrderBy(reference => reference.Order).ToArray();
        if (index < ordered.Length && ordered[index].LogicalObjectId == logicalObjectId)
            return ordered[index].ProviderRepresentation;
        return ordered.FirstOrDefault(reference => reference.LogicalObjectId == logicalObjectId)?.ProviderRepresentation;
    }

    private static bool IsSupportedRepresentation(MediaType mediaType, string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return true;
        if (value.StartsWith("asset://", StringComparison.OrdinalIgnoreCase)) return true;
        return mediaType switch
        {
            MediaType.Image => value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase),
            MediaType.Audio => value.StartsWith("data:audio/", StringComparison.OrdinalIgnoreCase),
            MediaType.Video => false,
            _ => false
        };
    }

    private static bool ReadBoolean(GenerationRequest request, string name, bool defaultValue)
    {
        if (!request.ProviderParameters.TryGetValue(name, out var value)) return defaultValue;
        if (!bool.TryParse(value, out var parsed))
            throw new GenerationValidationException([$"BytePlus parameter '{name}' must be true or false."]);
        return parsed;
    }

    private static string ReadChoice(
        GenerationRequest request,
        string name,
        IReadOnlyCollection<string> choices,
        string defaultValue)
    {
        if (!request.ProviderParameters.TryGetValue(name, out var value)) return defaultValue;
        if (!choices.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new GenerationValidationException(
                [$"BytePlus parameter '{name}' must be one of: {string.Join(", ", choices)}."]);
        return value.ToLowerInvariant();
    }

    private static GenerationSubmission ParseSubmission(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var id = ReadScalar(document.RootElement, "id")
                ?? throw new JsonException("The response did not include an id.");
            return new GenerationSubmission
            {
                ProviderJobId = id,
                Status = GenerationStatus.Queued,
                ResponseMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["providerStatus"] = "queued"
                }
            };
        }
        catch (JsonException exception)
        {
            throw InvalidResponse("generation", exception);
        }
    }

    private static ProviderGenerationJob ParseJob(string responseBody, string expectedJobId)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var id = ReadScalar(root, "id") ?? expectedJobId;
            var providerStatus = ReadScalar(root, "status")
                ?? throw new JsonException("The task response did not include a status.");
            var status = providerStatus.ToLowerInvariant() switch
            {
                "queued" => GenerationStatus.Queued,
                "running" => GenerationStatus.Running,
                "succeeded" => GenerationStatus.Succeeded,
                "cancelled" => GenerationStatus.Cancelled,
                "failed" or "expired" => GenerationStatus.Failed,
                _ => throw new JsonException($"Unknown BytePlus task status '{providerStatus}'.")
            };

            var outputs = new List<ProviderGenerationOutput>();
            if (root.TryGetProperty("content", out var outputContent) &&
                outputContent.ValueKind == JsonValueKind.Object &&
                ReadScalar(outputContent, "video_url") is { } videoUrl)
            {
                if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new JsonException("BytePlus returned a non-HTTPS output URL.");
                outputs.Add(new ProviderGenerationOutput(uri.AbsoluteUri));
            }
            if (status == GenerationStatus.Succeeded && outputs.Count == 0)
                throw new JsonException("BytePlus reported success without a video URL.");

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["providerStatus"] = providerStatus,
                ["outputCount"] = outputs.Count.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var name in new[]
                     {
                         "model", "created_at", "updated_at", "duration", "resolution", "ratio",
                         "output_format", "generate_audio", "framespersecond"
                     })
            {
                if (ReadScalar(root, name) is { } value) metadata[name] = value;
            }
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "completion_tokens", "total_tokens" })
                    if (ReadScalar(usage, name) is { } value) metadata[$"usage.{name}"] = value;
            }
            if (root.TryGetProperty("content", out outputContent) && outputContent.ValueKind == JsonValueKind.Object &&
                ReadScalar(outputContent, "last_frame_url") is { } lastFrameUrl)
                metadata["lastFrameUrl"] = lastFrameUrl;

            var (errorCode, errorMessage) = ReadError(root);
            return new ProviderGenerationJob
            {
                ProviderJobId = id,
                Status = status,
                Outputs = outputs,
                Error = status == GenerationStatus.Failed
                    ? new GenerationError
                    {
                        ProviderCode = errorCode ?? providerStatus,
                        Message = errorMessage ?? $"BytePlus reported that the task {providerStatus}."
                    }
                    : null,
                ResponseMetadata = metadata
            };
        }
        catch (JsonException exception)
        {
            throw InvalidResponse("task", exception);
        }
    }

    private async Task<string> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        var apiKey = await _secretStore.GetAsync(CredentialKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new VideoGenerationProviderException(
                $"A BytePlus ModelArk API key is required. Store it in {_secretStore.DisplayName} before submitting.",
                providerCode: "missing_api_key");
        return apiKey;
    }

    private static VideoGenerationProviderException CreateHttpException(
        string responseBody,
        System.Net.HttpStatusCode statusCode,
        string fallbackMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var (code, message) = ReadError(document.RootElement);
            return new VideoGenerationProviderException(
                message ?? $"{fallbackMessage} with HTTP {(int)statusCode}.",
                (int)statusCode,
                code,
                "BytePlus response body omitted from durable diagnostics.");
        }
        catch (JsonException)
        {
            return new VideoGenerationProviderException(
                $"{fallbackMessage} with HTTP {(int)statusCode}.",
                (int)statusCode,
                technicalDetails: "BytePlus response body omitted from durable diagnostics.");
        }
    }

    private static (string? Code, string? Message) ReadError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            return (ReadScalar(error, "code"), ReadScalar(error, "message"));
        return (ReadScalar(root, "code"), ReadScalar(root, "message"));
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

    private static VideoGenerationProviderException InvalidResponse(string operation, Exception exception) =>
        new(
            $"BytePlus returned an unreadable {operation} response.",
            providerCode: "invalid_response",
            technicalDetails: "BytePlus response body omitted from durable diagnostics.",
            innerException: exception);
}
