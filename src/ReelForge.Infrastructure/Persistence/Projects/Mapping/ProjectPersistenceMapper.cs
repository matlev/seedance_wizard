using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal static partial class ProjectPersistenceMapper
{
    public static ProjectFileDto ToDto(VideoProject source) => new()
    {
        FormatVersion = ProjectFileDto.CurrentFormatVersion,
        Id = source.Id,
        Name = source.Name,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
        Assets = source.Assets.Select(ToDto).ToList(),
        RecipeRevisions = source.RecipeRevisions.Select(ToDto).ToList(),
        RecipeDrafts = source.RecipeDrafts.Select(ToDto).ToList(),
        Anchors = source.Anchors.Select(ToDto).ToList(),
        AnchorRevisions = source.AnchorRevisions.Select(ToDto).ToList(),
        WorkingCompositionAssetId = source.WorkingCompositionAssetId,
        CurrentGenerationDraft = ToDto(source.CurrentGenerationDraft),
        Generations = source.Generations.Select(ToDto).ToList()
    };

    public static VideoProject FromDto(ProjectFileDto source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
        Assets = source.Assets.Select(FromDto).ToList(),
        RecipeRevisions = source.RecipeRevisions.Select(FromDto).ToList(),
        RecipeDrafts = source.RecipeDrafts.Select(FromDto).ToList(),
        Anchors = source.Anchors.Select(FromDto).ToList(),
        AnchorRevisions = source.AnchorRevisions.Select(FromDto).ToList(),
        WorkingCompositionAssetId = source.WorkingCompositionAssetId,
        CurrentGenerationDraft = FromDto(source.CurrentGenerationDraft),
        Generations = source.Generations.Select(FromDto).ToList()
    };

    private static Dictionary<string, string> Copy(IEnumerable<KeyValuePair<string, string>> source) =>
        source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static ReadOnlyDictionary<string, string> ReadOnly(IEnumerable<KeyValuePair<string, string>> source) =>
        new(Copy(source));
}
