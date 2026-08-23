using ReelForge.Core;

namespace ReelForge.Application;

public sealed record TemporaryAssetHostRequest(
    GenerationReferenceSnapshot LogicalReference,
    MaterializedMediaLease Media,
    string ContentType,
    TimeSpan ReadUrlLifetime);

public sealed record HostedAssetReference(
    string HostingProvider,
    string ObjectKey,
    string ContentSha256,
    Uri ReadUrl,
    DateTimeOffset ReadUrlExpiresAt,
    bool Uploaded);

public interface ITemporaryAssetHost
{
    string ProviderId { get; }
    Task<HostedAssetReference> EnsureHostedAsync(
        TemporaryAssetHostRequest request,
        CancellationToken cancellationToken = default);
    Task RemoveAsync(string objectKey, CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public enum ConnectionFailureKind
{
    None,
    MissingConfiguration,
    MissingCredential,
    AuthenticationRejected,
    InsufficientPermissions,
    NetworkFailure,
    EndpointUnavailable,
    Unknown
}

public sealed record ConnectionTestResult(
    bool Succeeded,
    string Message,
    ConnectionFailureKind FailureKind = ConnectionFailureKind.None);
