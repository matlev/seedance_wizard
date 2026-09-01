using ReelForge.Core;

namespace ReelForge.App.Views.Generation;

internal sealed record GenerationModePresentationPolicy(
    bool ReferencesEnabled,
    string ReferenceHelpText,
    int DurationSeconds,
    bool DurationIsLocked,
    string AspectRatio,
    bool AspectRatioIsLocked)
{
    public static GenerationModePresentationPolicy Create(
        GenerationProviderCapabilities capabilities,
        GenerationMode mode,
        int flexibleDurationSeconds,
        string? flexibleAspectRatio)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var requirements = capabilities.GetModeRequirements(mode);
        var duration = requirements?.FixedDurationSeconds
            ?? Math.Clamp(
                flexibleDurationSeconds,
                capabilities.MinimumDurationSeconds,
                capabilities.MaximumDurationSeconds);
        var fallbackAspectRatio = capabilities.AspectRatios.Contains("16:9", StringComparer.OrdinalIgnoreCase)
            ? capabilities.AspectRatios.First(value => value.Equals("16:9", StringComparison.OrdinalIgnoreCase))
            : capabilities.AspectRatios[0];
        var flexibleRatio = capabilities.AspectRatios.FirstOrDefault(value =>
                value.Equals(flexibleAspectRatio, StringComparison.OrdinalIgnoreCase))
            ?? fallbackAspectRatio;
        if (mode == GenerationMode.TextToVideo &&
            flexibleRatio.Equals("adaptive", StringComparison.OrdinalIgnoreCase))
        {
            flexibleRatio = capabilities.AspectRatios.FirstOrDefault(value =>
                    !value.Equals("adaptive", StringComparison.OrdinalIgnoreCase))
                ?? fallbackAspectRatio;
        }
        var aspectRatio = requirements?.FixedAspectRatio
            ?? (mode == GenerationMode.ImageToVideo &&
                capabilities.AspectRatios.Contains("adaptive", StringComparer.OrdinalIgnoreCase)
                    ? capabilities.AspectRatios.First(value => value.Equals("adaptive", StringComparison.OrdinalIgnoreCase))
                    : flexibleRatio);

        return new GenerationModePresentationPolicy(
            ReferencesEnabled: mode != GenerationMode.TextToVideo,
            ReferenceHelpText: mode switch
            {
                GenerationMode.TextToVideo =>
                    "Text-to-video does not use reference assets. Choose another mode to select and describe references.",
                GenerationMode.VideoEdit =>
                    "Select exactly one 4–30 second source video. Edit mode follows that video's aspect ratio and duration and creates a new Project Media asset.",
                _ =>
                    "Select project assets to use as references. Role, order, label, and notes are frozen into history."
            },
            DurationSeconds: duration,
            DurationIsLocked: requirements?.FixedDurationSeconds is not null,
            AspectRatio: aspectRatio,
            AspectRatioIsLocked: requirements?.FixedAspectRatio is not null);
    }
}
