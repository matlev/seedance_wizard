using System.Globalization;
using System.Text;
using ReelForge.Core;
using ReelForge.App.Views.MediaPreparation;
using ReelForge.App.Views.Editing;

namespace ReelForge.App.Views.Inspector;

internal static class InspectorTextFormatter
{
    public static string FormatSavedFrame(SavedFrameListItem item)
    {
        var builder = new StringBuilder();
        builder.AppendLine(item.DisplayLabel);
        builder.AppendLine($"Saved Frame: {item.Anchor.Id}");
        builder.AppendLine($"Revision: {item.Revision.RevisionNumber} ({item.Revision.Id})");
        builder.AppendLine($"Position: {FormatFrameTimestamp(item.Revision.TimestampSeconds)}");
        builder.AppendLine($"Stream: {item.Revision.VideoStreamIndex}");
        builder.AppendLine($"Presentation timestamp: {item.Revision.PresentationTimestamp}");
        builder.AppendLine($"Time base: {item.Revision.TimeBaseNumerator}/{item.Revision.TimeBaseDenominator}");
        builder.AppendLine($"Source SHA-256: {item.Revision.SourceContentHash}");
        if (!string.IsNullOrWhiteSpace(item.Anchor.Notes)) builder.AppendLine($"Notes: {item.Anchor.Notes}");
        if (!string.IsNullOrWhiteSpace(item.Error)) builder.AppendLine($"Preview unavailable: {item.Error}");
        return builder.ToString();
    }

    public static string FormatAsset(
        ProjectAsset asset,
        MediaEncodingMetadata? realizedEncoding = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(asset.FileName);
        builder.AppendLine($"ID: {asset.Id}");
        builder.AppendLine($"Type: {asset.MediaType}");
        builder.AppendLine($"Storage: {asset.StorageKind}");
        builder.AppendLine($"Created from: {asset.Origin}");
        builder.AppendLine($"Path: {asset.Physical?.RelativePath ?? "materialized on demand"}");
        if (asset.Physical is { } physical)
        {
            builder.AppendLine($"Availability: {physical.Availability}");
        }
        if (asset.Physical?.ContentIdentity is { } identity)
        {
            builder.AppendLine($"SHA-256: {identity.Sha256 ?? identity.Status.ToString()}");
        }
        builder.AppendLine($"Created: {asset.CreatedAt.LocalDateTime:g}");

        if (asset.DurationSeconds is not null)
        {
            builder.AppendLine($"Duration: {asset.DurationSeconds:0.###} seconds");
        }

        AppendTimingAssessments(builder, asset.TimingAssessments);

        var encoding = realizedEncoding ?? asset.Encoding;
        if (encoding is null)
        {
            builder.AppendLine();
            builder.AppendLine("Encoding metadata unavailable. Install/configure ffprobe, then reselect the asset.");
            return builder.ToString();
        }

        builder.AppendLine();
        builder.AppendLine("CONTAINER");
        builder.AppendLine($"Format: {encoding.ContainerFormat ?? "—"}");
        builder.AppendLine($"Size: {FormatBytes(encoding.SizeBytes)}");
        builder.AppendLine($"Bit rate: {encoding.BitRate?.ToString("N0", CultureInfo.InvariantCulture) ?? "—"} bps");

        if (encoding.Video is { } video)
        {
            builder.AppendLine();
            builder.AppendLine("VIDEO");
            builder.AppendLine($"Codec: {video.Codec ?? "—"} / {video.CodecProfile ?? "—"}");
            builder.AppendLine($"Dimensions: {video.Width?.ToString(CultureInfo.InvariantCulture) ?? "—"} × {video.Height?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            builder.AppendLine($"Pixel format: {video.PixelFormat ?? "—"}");
            builder.AppendLine($"Frame rate: {video.FrameRate ?? "—"}");
            builder.AppendLine($"Time base: {video.TimeBase ?? "—"}");
            builder.AppendLine($"Codec level: {video.CodecLevel?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
        }

        if (encoding.Audio is { } audio)
        {
            builder.AppendLine();
            builder.AppendLine("AUDIO");
            builder.AppendLine($"Codec: {audio.Codec ?? "—"}");
            builder.AppendLine($"Sample rate: {audio.SampleRate?.ToString(CultureInfo.InvariantCulture) ?? "—"} Hz");
            builder.AppendLine($"Channels: {audio.Channels?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            builder.AppendLine($"Layout: {audio.ChannelLayout ?? "—"}");
        }

        return builder.ToString();
    }

    private static void AppendTimingAssessments(StringBuilder builder, IEnumerable<StreamTimingAssessment> assessments)
    {
        foreach (var assessment in assessments.OrderBy(item => item.MediaType))
        {
            builder.AppendLine();
            builder.AppendLine(assessment.MediaType == MediaType.Video ? "VIDEO TIMING" : "AUDIO TIMING");
            builder.AppendLine(TimingWarningPresentation.FormatAssessmentDetail(assessment));
        }
    }

    public static string FormatGeneration(GenerationRecord generation)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Generation {generation.Id}");
        builder.AppendLine($"Status: {generation.Status}");
        builder.AppendLine($"Output ingestion: {generation.IngestionStatus}");
        builder.AppendLine($"Provider: {generation.RequestSnapshot.ProviderId}");
        builder.AppendLine($"Model: {generation.RequestSnapshot.ModelVersion}");
        builder.AppendLine($"Provider job: {generation.ProviderJobId ?? "—"}");
        builder.AppendLine($"Requested: {generation.RequestedAt.LocalDateTime:g}");
        builder.AppendLine($"Completed: {generation.CompletedAt?.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) ?? "—"}");
        builder.AppendLine();
        builder.AppendLine("PROMPT");
        builder.AppendLine(generation.RequestSnapshot.Prompt);
        builder.AppendLine();
        builder.AppendLine("SETTINGS");
        builder.AppendLine($"Mode: {generation.RequestSnapshot.Mode}");
        builder.AppendLine($"Duration: {generation.RequestSnapshot.DurationSeconds}s");
        builder.AppendLine($"Aspect ratio: {generation.RequestSnapshot.AspectRatio}");
        builder.AppendLine($"Resolution: {generation.RequestSnapshot.Resolution}");
        builder.AppendLine($"References: {generation.RequestSnapshot.References.Count}");
        builder.AppendLine($"Lineage: {generation.RelationshipType?.ToString() ?? "root"}");
        builder.AppendLine($"Parent: {generation.ParentGenerationId?.ToString() ?? "—"}");
        builder.AppendLine($"Output assets: {generation.OutputAssetIds.Count}");

        foreach (var reference in generation.RequestSnapshot.References.OrderBy(item => item.Order))
        {
            builder.AppendLine(
                $"  [{reference.Order}] {reference.ObjectKind} {reference.LogicalObjectId} • {reference.Role?.ToString() ?? "general"}" +
                (string.IsNullOrWhiteSpace(reference.Label) ? string.Empty : $" • {reference.Label}"));
            if (generation.ReferenceMaterializations.TryGetValue(reference.ReferenceId, out var receipt))
            {
                builder.AppendLine($"      prepared bytes: {receipt.ProducedContentHash ?? "—"}");
                builder.AppendLine($"      preparation: {receipt.ProviderScope ?? "local"}");
            }
        }

        foreach (var pair in generation.ResponseMetadata)
        {
            builder.AppendLine($"{pair.Key}: {pair.Value}");
        }

        if (generation.Error is not null)
        {
            builder.AppendLine();
            builder.AppendLine("ERROR");
            builder.AppendLine(generation.Error.Message);
            builder.AppendLine(generation.Error.TechnicalDetails);
        }

        return builder.ToString();
    }

    private static string FormatFrameTimestamp(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
