using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SeedanceWizard.Application;
using SeedanceWizard.Core;

namespace SeedanceWizard.Infrastructure;

public interface IProviderAssetReferenceResolver
{
    string? Resolve(string providerId, ProjectAsset asset);
}

public sealed class ProjectAssetReferenceResolver : IProviderAssetReferenceResolver
{
    public string? Resolve(string providerId, ProjectAsset asset) =>
        asset.ProviderReferences.TryGetValue(providerId, out var reference) && !string.IsNullOrWhiteSpace(reference.Value)
            ? reference.Value
            : null;
}

public sealed class AtlasCloudSeedance25Provider : IVideoGenerationProvider
{
    public const string ProviderId = "atlascloud";
    public const string CredentialKey = "atlascloud.api-key";
    public const string TextToVideoModel = "bytedance/seedance-2.5/text-to-video";
    public const string ImageToVideoModel = "bytedance/seedance-2.5/image-to-video";
    public const string ReferenceToVideoModel = "bytedance/seedance-2.5/reference-to-video";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedParameterNames = new(StringComparer.Ordinal)
    {
        "output_format", "generate_audio", "watermark", "return_last_frame"
    };

    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;
    private readonly IProviderAssetReferenceResolver _assetReferenceResolver;

    public AtlasCloudSeedance25Provider(
        HttpClient httpClient,
        ISecretStore secretStore,
        IProviderAssetReferenceResolver assetReferenceResolver)
    {
        _httpClient = httpClient;
        _secretStore = secretStore;
        _assetReferenceResolver = assetReferenceResolver;

        _httpClient.BaseAddress ??= new Uri("https://api.atlascloud.ai/");
        if (_httpClient.BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The AtlasCloud API base address must use HTTPS.", nameof(httpClient));
        }
    }

    public GenerationProviderCapabilities Capabilities { get; } = new(
        ProviderId: ProviderId,
        DisplayName: "AtlasCloud Seedance 2.5",
        ModelVersion: "bytedance/seedance-2.5",
        Modes: [GenerationMode.TextToVideo, GenerationMode.ImageToVideo, GenerationMode.ReferenceToVideo],
        MinimumDurationSeconds: 4,
        MaximumDurationSeconds: 30,
        AspectRatios: ["16:9", "4:3", "1:1", "3:4", "9:16", "21:9", "adaptive"],
        Resolutions: ["480p", "720p"],
        MaximumImageReferences: 30,
        MaximumVideoReferences: 10,
        MaximumAudioReferences: 10,
        SupportedReferenceTypes: new HashSet<MediaType> { MediaType.Image, MediaType.Video, MediaType.Audio },
        ProviderParameters: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["output_format"] = ["mp4", "mov"],
            ["generate_audio"] = ["true", "false"],
            ["watermark"] = ["true", "false"],
            ["return_last_frame"] = ["true", "false"]
        });

    public async Task<GenerationSubmission> SubmitAsync(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(request, projectAssets);
        var apiKey = await _secretStore.GetAsync(CredentialKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new VideoGenerationProviderException(
                "An AtlasCloud API key is required. Store it in Windows Credential Manager before submitting.",
                providerCode: "missing_api_key");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/model/generateVideo")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var (providerCode, providerMessage) = ReadProviderError(responseBody);
            throw new VideoGenerationProviderException(
                providerMessage ?? $"AtlasCloud rejected the generation request with HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                providerCode,
                responseBody);
        }

        return ParseSubmission(responseBody);
    }

    public IReadOnlyDictionary<string, object?> BuildPayload(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projectAssets);

        var errors = Capabilities.Validate(request, projectAssets).ToList();
        foreach (var parameter in request.ProviderParameters.Keys.Where(key => !SupportedParameterNames.Contains(key)))
        {
            errors.Add($"AtlasCloud parameter '{parameter}' is not supported by this adapter.");
        }

        var references = request.ReferenceAssetIds
            .Select(id => projectAssets.FirstOrDefault(asset => asset.Id == id))
            .OfType<ProjectAsset>()
            .ToList();

        if (request.Mode == GenerationMode.ImageToVideo)
        {
            if (!request.AspectRatio.Equals("adaptive", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("AtlasCloud Seedance 2.5 image-to-video currently requires the adaptive ratio.");
            }

            if (references.Count is < 1 or > 2 || references.Any(asset => asset.MediaType != MediaType.Image))
            {
                errors.Add("AtlasCloud image-to-video requires one or two image references and no video or audio references.");
            }
        }

        var resolvedReferences = new Dictionary<Guid, string>();
        foreach (var asset in references)
        {
            var resolved = _assetReferenceResolver.Resolve(ProviderId, asset);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                errors.Add($"{asset.FileName} has no AtlasCloud reference URL, Base64 value, or uploaded asset reference.");
            }
            else
            {
                resolvedReferences[asset.Id] = resolved;
            }
        }

        if (errors.Count > 0)
        {
            throw new GenerationValidationException(errors.Distinct(StringComparer.Ordinal).ToList());
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = GetModelId(request.Mode),
            ["prompt"] = request.Prompt,
            ["duration"] = request.DurationSeconds,
            ["resolution"] = request.Resolution,
            ["ratio"] = request.AspectRatio,
            ["generate_audio"] = ReadBoolean(request, "generate_audio", defaultValue: true),
            ["watermark"] = ReadBoolean(request, "watermark", defaultValue: false),
            ["return_last_frame"] = ReadBoolean(request, "return_last_frame", defaultValue: false),
            ["output_format"] = ReadChoice(request, "output_format", ["mp4", "mov"], "mp4")
        };

        if (request.Mode == GenerationMode.ImageToVideo)
        {
            payload["image"] = resolvedReferences[references[0].Id];
            if (references.Count == 2)
            {
                payload["last_image"] = resolvedReferences[references[1].Id];
            }
        }
        else if (request.Mode == GenerationMode.ReferenceToVideo)
        {
            AddReferenceArray(payload, "reference_images", references, resolvedReferences, MediaType.Image);
            AddReferenceArray(payload, "reference_videos", references, resolvedReferences, MediaType.Video);
            AddReferenceArray(payload, "reference_audios", references, resolvedReferences, MediaType.Audio);
        }

        return payload;
    }

    private static void AddReferenceArray(
        IDictionary<string, object?> payload,
        string fieldName,
        IEnumerable<ProjectAsset> references,
        IReadOnlyDictionary<Guid, string> resolvedReferences,
        MediaType mediaType)
    {
        var values = references
            .Where(asset => asset.MediaType == mediaType)
            .Select(asset => resolvedReferences[asset.Id])
            .ToArray();
        if (values.Length > 0)
        {
            payload[fieldName] = values;
        }
    }

    private static string GetModelId(GenerationMode mode) => mode switch
    {
        GenerationMode.TextToVideo => TextToVideoModel,
        GenerationMode.ImageToVideo => ImageToVideoModel,
        GenerationMode.ReferenceToVideo => ReferenceToVideoModel,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static bool ReadBoolean(GenerationRequest request, string name, bool defaultValue)
    {
        if (!request.ProviderParameters.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw new GenerationValidationException([$"AtlasCloud parameter '{name}' must be true or false."]);
        }

        return parsed;
    }

    private static string ReadChoice(
        GenerationRequest request,
        string name,
        IReadOnlyCollection<string> choices,
        string defaultValue)
    {
        if (!request.ProviderParameters.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (!choices.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new GenerationValidationException(
                [$"AtlasCloud parameter '{name}' must be one of: {string.Join(", ", choices)}."]);
        }

        return value.ToLowerInvariant();
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
            {
                throw new JsonException("The response did not include data.id or id.");
            }

            var providerStatus = ReadScalar(data, "status") ?? "processing";
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["providerStatus"] = providerStatus
            };
            foreach (var name in new[] { "model", "created_at", "completion_tokens", "total_tokens" })
            {
                if (ReadScalar(data, name) is { } value)
                {
                    metadata[name] = value;
                }
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
                technicalDetails: responseBody,
                innerException: exception);
        }
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

    private static string? ReadScalar(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }
}
