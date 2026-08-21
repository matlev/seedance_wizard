using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal static partial class ProjectPersistenceMapper
{
    private static ProjectAssetDto ToDto(ProjectAsset source) => new()
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        FileName = source.FileName,
        MediaType = source.MediaType,
        StorageKind = source.StorageKind,
        Origin = source.Origin,
        CreatedAt = source.CreatedAt,
        DurationSeconds = source.DurationSeconds,
        Width = source.Width,
        Height = source.Height,
        Encoding = source.Encoding,
        Provenance = ToDto(source.Provenance),
        Physical = ToDto(source.Physical),
        Virtual = source.Virtual is null ? null : new VirtualAssetStateDto
        {
            Kind = source.Virtual.Kind,
            CurrentRecipeRevisionId = source.Virtual.CurrentRecipeRevisionId,
            ExpectedMediaProperties = source.Virtual.ExpectedMediaProperties
        },
        ProviderReferences = source.ProviderReferences.ToDictionary(pair => pair.Key, pair => ToDto(pair.Value), StringComparer.Ordinal)
    };

    private static ProjectAsset FromDto(ProjectAssetDto source) => new()
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        FileName = source.FileName,
        MediaType = source.MediaType,
        StorageKind = source.StorageKind,
        Origin = source.Origin,
        CreatedAt = source.CreatedAt,
        DurationSeconds = source.DurationSeconds,
        Width = source.Width,
        Height = source.Height,
        Encoding = source.Encoding,
        Provenance = FromDto(source.Provenance),
        Physical = FromDto(source.Physical),
        Virtual = source.Virtual is null ? null : new VirtualAssetState
        {
            Kind = source.Virtual.Kind,
            CurrentRecipeRevisionId = source.Virtual.CurrentRecipeRevisionId,
            ExpectedMediaProperties = source.Virtual.ExpectedMediaProperties
        },
        ProviderReferences = source.ProviderReferences.ToDictionary(pair => pair.Key, pair => FromDto(pair.Value), StringComparer.Ordinal)
    };

    private static PhysicalAssetStorageDto? ToDto(PhysicalAssetStorage? source) => source is null ? null : new()
    {
        RelativePath = source.RelativePath,
        Durability = source.Durability,
        ContentIdentity = new ContentIdentityDto
        {
            Algorithm = source.ContentIdentity.Algorithm,
            Sha256 = source.ContentIdentity.Sha256,
            Status = source.ContentIdentity.Status,
            LengthBytes = source.ContentIdentity.LengthBytes,
            ObservedLastWriteTimeUtc = source.ContentIdentity.ObservedLastWriteTimeUtc
        }
    };

    private static PhysicalAssetStorage? FromDto(PhysicalAssetStorageDto? source) => source is null ? null : new()
    {
        RelativePath = source.RelativePath,
        Durability = source.Durability,
        ContentIdentity = new ContentIdentity
        {
            Algorithm = source.ContentIdentity.Algorithm,
            Sha256 = source.ContentIdentity.Sha256,
            Status = source.ContentIdentity.Status,
            LengthBytes = source.ContentIdentity.LengthBytes,
            ObservedLastWriteTimeUtc = source.ContentIdentity.ObservedLastWriteTimeUtc
        }
    };

    private static ProviderAssetReferenceDto ToDto(ProviderAssetReference source) => new()
    {
        Value = source.Value,
        SourceContentHash = source.SourceContentHash,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Scope = source.Scope,
        ExpiresAt = source.ExpiresAt
    };

    private static ProviderAssetReference FromDto(ProviderAssetReferenceDto source) => new()
    {
        Value = source.Value,
        SourceContentHash = source.SourceContentHash,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Scope = source.Scope,
        ExpiresAt = source.ExpiresAt
    };

    private static AssetProvenanceDto? ToDto(AssetProvenance? source) => source is null ? null : new()
    {
        Operation = source.Operation,
        SourceAssetIds = [.. source.SourceAssetIds],
        GenerationId = source.GenerationId,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Parameters = Copy(source.Parameters)
    };

    private static AssetProvenance? FromDto(AssetProvenanceDto? source) => source is null ? null : new()
    {
        Operation = source.Operation,
        SourceAssetIds = [.. source.SourceAssetIds],
        GenerationId = source.GenerationId,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Parameters = Copy(source.Parameters)
    };
}
