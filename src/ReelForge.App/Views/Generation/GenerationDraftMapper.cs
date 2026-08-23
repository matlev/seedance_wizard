using System.Collections.ObjectModel;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.Generation;

/// <summary>Maps the Generation panel's presentation state to durable draft state.</summary>
internal static class GenerationDraftMapper
{
    public static GenerationDraft Capture(
        GenerationPanel panel,
        IVideoGenerationProvider provider,
        GenerationDraft? currentDraft,
        IEnumerable<GenerationReferenceChoice> referenceChoices)
    {
        var state = panel.CaptureState();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (provider.Capabilities.ProviderParameters.ContainsKey("generate_audio"))
            parameters["generate_audio"] = state.GenerateAudio.ToString().ToLowerInvariant();
        else if (provider.Capabilities.ProviderParameters.ContainsKey("generateAudio"))
            parameters["generateAudio"] = state.GenerateAudio.ToString().ToLowerInvariant();
        if (provider.Capabilities.ProviderParameters.ContainsKey("watermark"))
            parameters["watermark"] = state.Watermark.ToString().ToLowerInvariant();
        if (provider.Capabilities.ProviderParameters.ContainsKey("output_format"))
            parameters["output_format"] = state.OutputFormat;

        return new GenerationDraft
        {
            ProviderId = provider.Capabilities.ProviderId,
            ModelVersion = provider.Capabilities.ModelVersion,
            Prompt = state.Prompt,
            Mode = state.Mode,
            DurationSeconds = state.DurationSeconds,
            AspectRatio = state.AspectRatio,
            Resolution = state.Resolution,
            References = GenerationReferenceEditor.Capture(state.Mode, referenceChoices),
            ProviderParameters = parameters,
            ParentGenerationId = currentDraft?.ParentGenerationId,
            RelationshipType = currentDraft?.RelationshipType,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }

    public static void Load(
        GenerationPanel panel,
        GenerationDraft draft,
        ObservableCollection<GenerationReferenceChoice> referenceChoices)
    {
        panel.LoadState(new GenerationPanelFormState(
            draft.Prompt, draft.Mode, draft.DurationSeconds, draft.AspectRatio, draft.Resolution,
            ReadBoolean(draft, "generate_audio", "generateAudio", true),
            ReadBoolean(draft, "watermark", null, false),
            draft.ProviderParameters.GetValueOrDefault("output_format", "mp4")));
        GenerationReferenceEditor.ApplyDraft(draft.References, referenceChoices);
        panel.RefreshReferences();
        panel.SetLineage(draft.ParentGenerationId is { } parent
            ? $"{draft.RelationshipType} • parent {parent}"
            : "New root generation");
    }

    private static bool ReadBoolean(GenerationDraft draft, string primaryName, string? fallbackName, bool defaultValue)
    {
        if (draft.ProviderParameters.TryGetValue(primaryName, out var value) && bool.TryParse(value, out var parsed))
            return parsed;
        if (fallbackName is not null && draft.ProviderParameters.TryGetValue(fallbackName, out value) && bool.TryParse(value, out parsed)) return parsed;
        return defaultValue;
    }
}
