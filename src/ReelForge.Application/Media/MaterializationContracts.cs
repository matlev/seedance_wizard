using ReelForge.Core;

namespace ReelForge.Application;

public enum MaterializationPurpose
{
    Preview,
    ProviderUpload,
    FinalExport,
    FrameExtraction,
    Thumbnail,
    Waveform
}

public enum MaterializationRetentionPreference
{
    Unspecified,
    Ephemeral,
    NormalCache,
    PreferRetained,
    Persistent
}

public abstract record MaterializationTarget;

public sealed record AssetMaterializationTarget(
    Guid AssetId,
    Guid? RecipeRevisionId = null) : MaterializationTarget;

public sealed record AnchorMaterializationTarget(
    Guid AnchorId,
    Guid AnchorRevisionId) : MaterializationTarget;

public sealed record MaterializationRequest(
    MaterializationTarget Target,
    MaterializationPurpose Purpose,
    MaterializationRetentionPreference RetentionPreference = MaterializationRetentionPreference.Unspecified,
    string? Profile = null);

public sealed class MaterializedMediaLease : IAsyncDisposable
{
    private Func<ValueTask>? _release;

    public MaterializedMediaLease(
        string path,
        ContentIdentity contentIdentity,
        MediaEncodingMetadata? encoding,
        bool isDurableSource,
        Func<ValueTask>? release = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        ContentIdentity = contentIdentity;
        Encoding = encoding;
        IsDurableSource = isDurableSource;
        _release = release;
    }

    public string Path { get; }
    public ContentIdentity ContentIdentity { get; }
    public MediaEncodingMetadata? Encoding { get; }
    public bool IsDurableSource { get; }

    public ValueTask DisposeAsync()
    {
        var release = Interlocked.Exchange(ref _release, null);
        return release?.Invoke() ?? ValueTask.CompletedTask;
    }
}

public interface IMediaMaterializer
{
    Task<MaterializedMediaLease> MaterializeAsync(
        VideoProject project,
        ProjectLocation location,
        MaterializationRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICompositionSegmentMaterializer
{
    Task<MaterializedMediaLease> MaterializeSegmentAsync(
        VideoProject project,
        ProjectLocation location,
        Guid compositionAssetId,
        Guid recipeRevisionId,
        Guid segmentId,
        MaterializationPurpose purpose,
        CancellationToken cancellationToken = default);
}

public sealed record VideoPresentationFrame(
    int VideoStreamIndex,
    long PresentationTimestamp,
    int TimeBaseNumerator,
    int TimeBaseDenominator,
    long? FrameNumber = null)
{
    public double TimestampSeconds =>
        PresentationTimestamp * (double)TimeBaseNumerator / TimeBaseDenominator;
}

public interface IExactVideoFrameService
{
    Task<IReadOnlyList<VideoPresentationFrame>> IndexAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VideoPresentationFrame>> IndexWindowAsync(
        string mediaPath,
        double centerSeconds,
        double radiusSeconds = 2,
        CancellationToken cancellationToken = default);

    Task<MaterializedMediaLease> ExtractAsync(
        string mediaPath,
        string sourceContentHash,
        FrameAnchorRevision revision,
        MaterializationPurpose purpose,
        string? profile = null,
        CancellationToken cancellationToken = default);
}

public interface IMaterializationRetentionPolicy
{
    MaterializationRetentionPreference Resolve(
        MaterializationPurpose purpose,
        MaterializationTarget target,
        MaterializationRetentionPreference requestedPreference);
}
