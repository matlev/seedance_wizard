using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class BytePlusModelArkAssetPreparationService : IProviderAssetPreparationService
{
    private const long MaximumImageBytes = 30L * 1024 * 1024;
    private const long MaximumAudioBytes = 15L * 1024 * 1024;

    public async Task<PreparedProviderReference> PrepareAsync(
        string providerId,
        GenerationReferenceSnapshot logicalReference,
        MaterializedMediaLease media,
        GenerationSubmissionAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        if (!providerId.Equals(BytePlusModelArkSeedance25Provider.ProviderId, StringComparison.Ordinal))
            throw new NotSupportedException($"Provider '{providerId}' is not supported by this preparation service.");

        authorization.Demand(providerId, allowNetworkIsolatedTest: true);
        var extension = Path.GetExtension(media.Path).ToLowerInvariant();
        var (mimeType, maximumBytes) = extension switch
        {
            ".jpg" or ".jpeg" => ("image/jpeg", MaximumImageBytes),
            ".png" => ("image/png", MaximumImageBytes),
            ".webp" => ("image/webp", MaximumImageBytes),
            ".bmp" => ("image/bmp", MaximumImageBytes),
            ".tif" or ".tiff" => ("image/tiff", MaximumImageBytes),
            ".gif" => ("image/gif", MaximumImageBytes),
            ".heic" => ("image/heic", MaximumImageBytes),
            ".heif" => ("image/heif", MaximumImageBytes),
            ".wav" => ("audio/wav", MaximumAudioBytes),
            ".mp3" => ("audio/mp3", MaximumAudioBytes),
            ".mp4" or ".mov" => throw new GenerationValidationException(
                ["BytePlus reference videos require a public HTTPS URL or asset:// reference. " +
                 "The official Seedance video contract does not accept inline Base64 video or generic Files API IDs."]),
            _ => throw new GenerationValidationException(
                [$"BytePlus cannot prepare the local reference format '{extension}'."])
        };

        var fileInfo = new FileInfo(media.Path);
        if (fileInfo.Length >= maximumBytes)
        {
            var limit = maximumBytes / (1024 * 1024);
            throw new GenerationValidationException(
                [$"'{fileInfo.Name}' exceeds BytePlus's {limit} MB inline {mimeType.Split('/')[0]} limit."]);
        }

        var bytes = await File.ReadAllBytesAsync(media.Path, cancellationToken).ConfigureAwait(false);
        var representation = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        Array.Clear(bytes);
        return new PreparedProviderReference(
            logicalReference,
            representation,
            new MaterializationReceipt
            {
                SourceContentHash = logicalReference.ContentHash,
                ProducedContentHash = media.ContentIdentity.Sha256,
                Encoding = media.Encoding,
                ProviderScope = "inline-base64"
            });
    }
}
