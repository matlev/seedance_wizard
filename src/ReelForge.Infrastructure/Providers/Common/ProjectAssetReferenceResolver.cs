using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public interface IProviderAssetReferenceResolver
{
    string? Resolve(string providerId, ProjectAsset asset);
}

public sealed class ProjectAssetReferenceResolver : IProviderAssetReferenceResolver
{
    public string? Resolve(string providerId, ProjectAsset asset) =>
        asset.ProviderReferences.TryGetValue(providerId, out var reference) &&
        !string.IsNullOrWhiteSpace(reference.Value)
            ? reference.Value
            : null;
}
