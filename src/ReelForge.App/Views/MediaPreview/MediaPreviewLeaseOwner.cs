using ReelForge.Application;

namespace ReelForge.App.Views.MediaPreview;

internal sealed class MediaPreviewLeaseOwner : IDisposable
{
    private MaterializedMediaLease? _video;
    private MaterializedMediaLease? _auditionAudio;

    public bool HasAuditionAudio => _auditionAudio is not null;

    public string AdoptVideo(MaterializedMediaLease lease) => Adopt(ref _video, lease);

    public string AdoptAuditionAudio(MaterializedMediaLease lease) => Adopt(ref _auditionAudio, lease);

    public void ReleaseVideo() => Release(ref _video);

    public void ReleaseAuditionAudio() => Release(ref _auditionAudio);

    public void ReleaseAll()
    {
        try
        {
            ReleaseVideo();
        }
        finally
        {
            ReleaseAuditionAudio();
        }
    }

    public void Dispose() => ReleaseAll();

    private static string Adopt(ref MaterializedMediaLease? destination, MaterializedMediaLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var prior = Interlocked.Exchange(ref destination, lease);
        DisposeLease(prior);
        return lease.Path;
    }

    private static void Release(ref MaterializedMediaLease? destination)
    {
        var lease = Interlocked.Exchange(ref destination, null);
        DisposeLease(lease);
    }

    private static void DisposeLease(MaterializedMediaLease? lease)
    {
        if (lease is not null) lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
