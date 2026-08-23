using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class AssetImportService : IAssetImportService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".heic", ".heif", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".webm", ".wmv"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".flac", ".m4a", ".mp3", ".ogg", ".wav", ".wma"
    };

    private readonly IMediaInspectionService _mediaInspector;
    private readonly IContentHashService _contentHashService;

    public AssetImportService(IMediaInspectionService mediaInspector, IContentHashService? contentHashService = null)
    {
        _mediaInspector = mediaInspector;
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
    }

    public async Task<IReadOnlyList<ProjectAsset>> ImportAsync(
        ProjectLocation location,
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var imported = new List<ProjectAsset>();
        foreach (var sourcePath in sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullSourcePath))
            {
                throw new FileNotFoundException("Asset file was not found.", fullSourcePath);
            }

            var mediaType = DetermineMediaType(fullSourcePath);
            var folderName = mediaType switch
            {
                MediaType.Image => "images",
                MediaType.Video => "videos",
                MediaType.Audio => "audio",
                _ => throw new InvalidOperationException($"Unsupported media type '{mediaType}'.")
            };

            var targetDirectory = Path.Combine(location.RootDirectory, "assets", folderName);
            Directory.CreateDirectory(targetDirectory);
            var destinationPath = CollisionFreeDestinationPolicy.GetAvailablePath(
                targetDirectory,
                Path.GetFileName(fullSourcePath));

            using var fileCommit = AtomicFileCommit.Create(
                destinationPath,
                "import",
                Path.GetExtension(destinationPath));
            await CopyFileAsync(fullSourcePath, fileCommit.TemporaryPath, cancellationToken).ConfigureAwait(false);

            var contentIdentity = await _contentHashService
                .ComputeAsync(fileCommit.TemporaryPath, cancellationToken)
                .ConfigureAwait(false);

            var asset = new ProjectAsset
            {
                FileName = Path.GetFileName(destinationPath),
                DisplayName = Path.GetFileName(destinationPath),
                MediaType = mediaType,
                StorageKind = AssetStorageKind.Physical,
                Origin = AssetOrigin.Imported,
                CreatedAt = DateTimeOffset.UtcNow,
                Physical = new PhysicalAssetStorage
                {
                    RelativePath = ProjectPathPolicy.GetRelativePath(location, destinationPath),
                    Durability = PhysicalAssetDurability.Source,
                    ContentIdentity = contentIdentity,
                    Availability = PhysicalAssetAvailability.Available
                },
                Virtual = null
            };

            if (mediaType is MediaType.Video or MediaType.Audio)
            {
                try
                {
                    asset.Encoding = await _mediaInspector
                        .InspectAsync(fileCommit.TemporaryPath, cancellationToken)
                        .ConfigureAwait(false);
                    asset.DurationSeconds = asset.Encoding.DurationSeconds;
                    asset.Width = asset.Encoding.Video?.Width;
                    asset.Height = asset.Encoding.Video?.Height;
                }
                catch (MediaToolUnavailableException)
                {
                    // Import is still useful when FFmpeg is not installed. The UI reports this state.
                }
            }

            fileCommit.Commit();
            imported.Add(asset);
        }

        return imported;
    }

    public static MediaType DetermineMediaType(string path)
    {
        var extension = Path.GetExtension(path);
        if (ImageExtensions.Contains(extension))
        {
            return MediaType.Image;
        }

        if (VideoExtensions.Contains(extension))
        {
            return MediaType.Video;
        }

        if (AudioExtensions.Contains(extension))
        {
            return MediaType.Audio;
        }

        throw new NotSupportedException($"'{extension}' is not a supported image, video, or audio extension.");
    }

    public static bool IsSupportedMediaFile(string path)
    {
        var extension = Path.GetExtension(path);
        return ImageExtensions.Contains(extension) ||
               VideoExtensions.Contains(extension) ||
               AudioExtensions.Contains(extension);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

}
