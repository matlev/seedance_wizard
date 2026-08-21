using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class HttpGeneratedOutputIngestionService : IGeneratedOutputIngestionService
{
    private const long MaximumOutputBytes = 20L * 1024 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly IContentHashService _contentHashService;
    private readonly IMediaInspectionService _mediaInspector;

    public HttpGeneratedOutputIngestionService(
        HttpClient httpClient,
        IMediaInspectionService mediaInspector,
        IContentHashService? contentHashService = null)
    {
        _httpClient = httpClient;
        _mediaInspector = mediaInspector;
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
    }

    public async Task<IReadOnlyList<ProjectAsset>> IngestAsync(
        ProjectLocation location,
        Guid generationId,
        IReadOnlyList<ProviderGenerationOutput> outputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(outputs);
        if (outputs.Count == 0) throw new InvalidDataException("The provider returned no generation outputs.");

        var generatedDirectory = Path.GetFullPath(Path.Combine(location.RootDirectory, "generated"));
        Directory.CreateDirectory(generatedDirectory);
        var createdPaths = new List<string>();
        var assets = new List<ProjectAsset>();
        try
        {
            for (var index = 0; index < outputs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceUri = ValidateOutputUri(outputs[index].DownloadUrl);
                var extension = GetExtension(sourceUri);
                var temporaryPath = Path.Combine(generatedDirectory, $".download-{Guid.NewGuid():N}.partial");
                try
                {
                    using var response = await _httpClient
                        .GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                        throw new InvalidDataException("The generated output redirected to a non-HTTPS address.");
                    if (response.Content.Headers.ContentLength is > MaximumOutputBytes)
                        throw new InvalidDataException("The generated output exceeds the 20 GB safety limit.");

                    await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    await using (var destination = new FileStream(
                                     temporaryPath,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.None,
                                     81920,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await CopyWithLimitAsync(source, destination, MaximumOutputBytes, cancellationToken).ConfigureAwait(false);
                    }

                    var identity = await _contentHashService.ComputeAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                    var encoding = await _mediaInspector.InspectAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                    if (encoding.Video is null)
                        throw new InvalidDataException("The downloaded generation output is not an inspectable video.");

                    var finalPath = GetAvailablePath(generatedDirectory, $"generation-{generationId:N}-{index + 1}{extension}");
                    File.Move(temporaryPath, finalPath);
                    createdPaths.Add(finalPath);
                    assets.Add(CreateAsset(location, finalPath, generationId, identity, encoding));
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }

            return assets;
        }
        catch
        {
            foreach (var path in createdPaths)
                if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    private static ProjectAsset CreateAsset(
        ProjectLocation location,
        string path,
        Guid generationId,
        ContentIdentity identity,
        MediaEncodingMetadata encoding) => new()
    {
        DisplayName = Path.GetFileName(path),
        FileName = Path.GetFileName(path),
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Origin = AssetOrigin.Generated,
        DurationSeconds = encoding.DurationSeconds,
        Width = encoding.Video?.Width,
        Height = encoding.Video?.Height,
        Encoding = encoding,
        Provenance = new AssetProvenance
        {
            Operation = "generation-output",
            GenerationId = generationId
        },
        Physical = new PhysicalAssetStorage
        {
            RelativePath = Path.GetRelativePath(location.RootDirectory, path).Replace(Path.DirectorySeparatorChar, '/'),
            Durability = PhysicalAssetDurability.Generated,
            ContentIdentity = identity,
            Availability = PhysicalAssetAvailability.Available
        },
        Virtual = null
    };

    private static Uri ValidateOutputUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Provider output URLs must use HTTPS.");
        return uri;
    }

    private static string GetExtension(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return extension is ".mp4" or ".mov" or ".webm" ? extension : ".mp4";
    }

    private static string GetAvailablePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}-{suffix}{extension}");
            suffix++;
        }
        return candidate;
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidDataException("The generated output exceeds the 20 GB safety limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
