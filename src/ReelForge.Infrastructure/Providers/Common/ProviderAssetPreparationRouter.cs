using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class ProviderAssetPreparationRouter : IProviderAssetPreparationService
{
    private readonly IReadOnlyDictionary<string, IProviderAssetPreparationService> _services;

    public ProviderAssetPreparationRouter(
        IReadOnlyDictionary<string, IProviderAssetPreparationService> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public Task<PreparedProviderReference> PrepareAsync(
        string providerId,
        GenerationReferenceSnapshot logicalReference,
        MaterializedMediaLease media,
        GenerationSubmissionAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        if (!_services.TryGetValue(providerId, out var service))
            throw new NotSupportedException($"Provider '{providerId}' has no configured asset-preparation service.");
        return service.PrepareAsync(providerId, logicalReference, media, authorization, cancellationToken);
    }
}
