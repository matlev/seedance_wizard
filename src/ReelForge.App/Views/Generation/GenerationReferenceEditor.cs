using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.App.Views.Generation;

internal static class GenerationReferenceEditor
{
    public static List<GenerationReferenceDraft> Capture(
        GenerationMode mode,
        IEnumerable<GenerationReferenceChoice> choices)
    {
        if (mode == GenerationMode.TextToVideo) return [];
        return choices
            .Where(choice => choice.IsSelected)
            .OrderBy(choice => choice.Order)
            .Select(choice => new GenerationReferenceDraft
            {
                ReferenceId = choice.ReferenceId,
                ObjectKind = choice.ObjectKind,
                LogicalObjectId = choice.LogicalObjectId,
                AnchorRevisionId = choice.AnchorRevisionId,
                Role = choice.Role,
                Order = choice.Order,
                Label = NullIfWhiteSpace(choice.Label),
                Notes = NullIfWhiteSpace(choice.Notes)
            })
            .ToList();
    }

    public static void ApplyDraft(
        IReadOnlyList<GenerationReferenceDraft> references,
        ObservableCollection<GenerationReferenceChoice> choices)
    {
        EnsureOccurrences(references, choices);
        foreach (var choice in choices)
        {
            choice.IsSelected = false;
            choice.Role = null;
            choice.Label = null;
            choice.Notes = null;
        }

        foreach (var reference in references.OrderBy(item => item.Order))
        {
            var choice = choices.FirstOrDefault(item => item.ReferenceId == reference.ReferenceId) ??
                         choices.FirstOrDefault(item =>
                             !item.IsSelected &&
                             item.ObjectKind == reference.ObjectKind &&
                             item.LogicalObjectId == reference.LogicalObjectId);
            if (choice is null) continue;
            choice.IsSelected = true;
            choice.ReferenceId = reference.ReferenceId;
            choice.AnchorRevisionId = reference.AnchorRevisionId ?? choice.AnchorRevisionId;
            choice.Role = reference.Role;
            choice.Order = reference.Order ?? choice.Order;
            choice.Label = reference.Label;
            choice.Notes = reference.Notes;
        }
    }

    private static void EnsureOccurrences(
        IReadOnlyList<GenerationReferenceDraft> references,
        ObservableCollection<GenerationReferenceChoice> choices)
    {
        foreach (var group in references.GroupBy(reference =>
                     (reference.ObjectKind, reference.LogicalObjectId)))
        {
            var matching = choices.Where(choice =>
                choice.ObjectKind == group.Key.ObjectKind &&
                choice.LogicalObjectId == group.Key.LogicalObjectId).ToList();
            if (matching.Count == 0) continue;
            while (matching.Count < group.Count())
            {
                var duplicate = matching[0].Duplicate(choices.Count);
                duplicate.IsSelected = false;
                choices.Add(duplicate);
                matching.Add(duplicate);
            }
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
