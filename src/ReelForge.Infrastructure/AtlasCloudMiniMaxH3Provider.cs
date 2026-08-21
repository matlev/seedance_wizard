using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class AtlasCloudMiniMaxH3Provider : IAsyncVideoGenerationProvider, IApiKeyVideoGenerationProvider
{
    public const string ProviderId = "atlascloud.minimax-h3";
    public const string CredentialKey = AtlasCloudVideoApiClient.CredentialKey;
    public const string TextToVideoModel = "minimax/h3/text-to-video";
    public const string ImageToVideoModel = "minimax/h3/image-to-video";
    public const string ReferenceToVideoModel = "minimax/h3/reference-to-video";

    private static readonly HashSet<string> SupportedParameterNames = new(StringComparer.Ordinal);
    private readonly AtlasCloudVideoApiClient _apiClient;
    private readonly IProviderAssetReferenceResolver _assetReferenceResolver;

    public AtlasCloudMiniMaxH3Provider(
        HttpClient httpClient,
        ISecretStore secretStore,
        IProviderAssetReferenceResolver assetReferenceResolver,
        IApplicationDiagnosticLog? diagnosticLog = null)
    {
        _apiClient = new AtlasCloudVideoApiClient(httpClient, secretStore, diagnosticLog);
        _assetReferenceResolver = assetReferenceResolver;
    }

    public GenerationProviderCapabilities Capabilities { get; } = new(
        ProviderId: ProviderId,
        DisplayName: "AtlasCloud MiniMax H3",
        ModelVersion: "minimax/h3",
        Modes: [GenerationMode.TextToVideo, GenerationMode.ImageToVideo, GenerationMode.ReferenceToVideo],
        MinimumDurationSeconds: 4,
        MaximumDurationSeconds: 15,
        AspectRatios: ["adaptive", "21:9", "16:9", "4:3", "1:1", "3:4", "9:16"],
        Resolutions: ["768P", "2K"],
        // AtlasCloud's current H3 schema does not publish per-type or combined maximums.
        MaximumImageReferences: int.MaxValue,
        MaximumVideoReferences: int.MaxValue,
        MaximumAudioReferences: int.MaxValue,
        SupportedReferenceTypes: new HashSet<MediaType> { MediaType.Image, MediaType.Video, MediaType.Audio },
        ProviderParameters: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    public GenerationProviderCostBehavior CostBehavior => GenerationProviderCostBehavior.PotentiallyBillable;
    public string ApiKeyCredentialKey => CredentialKey;

    public Task<GenerationSubmission> SubmitAsync(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets,
        GenerationSubmissionAuthorization? authorization = null,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(request, projectAssets);
        return _apiClient.SubmitAsync(payload, authorization, ProviderId, cancellationToken);
    }

    public Task<ProviderGenerationJob> GetJobAsync(
        string providerJobId,
        CancellationToken cancellationToken = default) =>
        _apiClient.GetJobAsync(ProviderId, providerJobId, cancellationToken);

    public IReadOnlyDictionary<string, object?> BuildPayload(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projectAssets);

        var errors = Capabilities.Validate(request, projectAssets).ToList();
        foreach (var parameter in request.ProviderParameters.Keys.Where(key => !SupportedParameterNames.Contains(key)))
        {
            errors.Add($"AtlasCloud MiniMax H3 parameter '{parameter}' is not supported by this adapter.");
        }

        var references = GenerationRequestReferenceResolver.Resolve(request, projectAssets)
            .OrderBy(reference => reference.Order)
            .ToArray();

        switch (request.Mode)
        {
            case GenerationMode.TextToVideo when request.AspectRatio.Equals("adaptive", StringComparison.OrdinalIgnoreCase):
                errors.Add("AtlasCloud MiniMax H3 text-to-video requires a concrete aspect ratio.");
                break;
            case GenerationMode.ImageToVideo:
                if (!request.AspectRatio.Equals("adaptive", StringComparison.OrdinalIgnoreCase))
                    errors.Add("AtlasCloud MiniMax H3 image-to-video requires the adaptive aspect ratio.");
                if (references.Length is < 1 or > 2 || references.Any(asset => asset.MediaType != MediaType.Image))
                    errors.Add("AtlasCloud MiniMax H3 image-to-video requires one or two image references.");
                if (references.Any(reference => reference.Role == GenerationReferenceRole.EndFrame) &&
                    references.All(reference => reference.Role == GenerationReferenceRole.EndFrame))
                    errors.Add("AtlasCloud MiniMax H3 requires a start-frame image when an end-frame image is supplied.");
                break;
            case GenerationMode.ReferenceToVideo:
                if (!references.Any(asset => asset.MediaType is MediaType.Image or MediaType.Video))
                    errors.Add("AtlasCloud MiniMax H3 reference-to-video requires at least one image or video reference; audio alone is not allowed.");
                break;
        }

        var resolvedReferences = new List<string>();
        for (var index = 0; index < references.Length; index++)
        {
            var asset = references[index];
            var resolved = asset.PreparedRepresentation
                ?? (asset.Asset is null ? null : _assetReferenceResolver.Resolve(ProviderId, asset.Asset))
                ?? (asset.Asset is null ? null : _assetReferenceResolver.Resolve(AtlasCloudSeedance25Provider.ProviderId, asset.Asset));
            if (string.IsNullOrWhiteSpace(resolved))
            {
                errors.Add($"{asset.DisplayName} has no prepared AtlasCloud reference.");
                continue;
            }

            var valid = request.Mode == GenerationMode.ImageToVideo
                ? IsHttpsUrl(resolved) || IsSupportedImageDataUrl(resolved)
                : IsHttpsUrl(resolved);
            if (!valid)
            {
                errors.Add(request.Mode == GenerationMode.ImageToVideo
                    ? $"{asset.DisplayName} must use an HTTPS URL or supported image Base64 data URL for MiniMax H3 image-to-video."
                    : $"{asset.DisplayName} must use an HTTPS URL for MiniMax H3 reference-to-video.");
                continue;
            }

            resolvedReferences.Add(resolved);
        }

        if (errors.Count > 0)
            throw new GenerationValidationException(errors.Distinct(StringComparer.Ordinal).ToList());

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = GetModelId(request.Mode),
            ["prompt"] = request.Prompt,
            ["resolution"] = CanonicalChoice(request.Resolution, Capabilities.Resolutions),
            ["duration"] = request.DurationSeconds,
            ["ratio"] = CanonicalChoice(request.AspectRatio, Capabilities.AspectRatios)
        };

        if (request.Mode == GenerationMode.ImageToVideo)
        {
            var firstIndex = Array.FindIndex(references, reference => reference.Role != GenerationReferenceRole.EndFrame);
            payload["image"] = resolvedReferences[firstIndex];
            var endIndex = Array.FindIndex(references, reference => reference.Role == GenerationReferenceRole.EndFrame);
            if (endIndex < 0 && references.Length == 2) endIndex = firstIndex == 0 ? 1 : 0;
            if (endIndex >= 0) payload["end_image"] = resolvedReferences[endIndex];
        }
        else if (request.Mode == GenerationMode.ReferenceToVideo)
        {
            payload["refers"] = references.Select((asset, index) => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = resolvedReferences[index],
                ["type"] = asset.MediaType.ToString().ToLowerInvariant()
            }).ToArray();
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

    private static string GetModelId(GenerationMode mode) => mode switch
    {
        GenerationMode.TextToVideo => TextToVideoModel,
        GenerationMode.ImageToVideo => ImageToVideoModel,
        GenerationMode.ReferenceToVideo => ReferenceToVideoModel,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static string CanonicalChoice(string value, IReadOnlyList<string> choices) =>
        choices.First(choice => choice.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsSupportedImageDataUrl(string value) =>
        value.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("data:image/jpg;base64,", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("data:image/webp;base64,", StringComparison.OrdinalIgnoreCase);
}
