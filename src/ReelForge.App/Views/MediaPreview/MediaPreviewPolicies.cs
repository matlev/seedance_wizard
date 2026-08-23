using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.MediaPreview;

/// <summary>
/// Pure playback-projection decisions for the shared media viewer. Keeping these
/// rules independent from WPF makes the three viewer modes deterministic to test.
/// </summary>
internal static class MediaPreviewTimelinePolicy
{
    /// <summary>
    /// Only a rendered composition is ordinary MediaElement playback that also
    /// represents the composition timeline. Project Media videos must not move it.
    /// </summary>
    public static bool ShouldProjectMediaTick(bool isBakedCompositionPreview) =>
        isBakedCompositionPreview;

    /// <summary>
    /// Fast audition owns timeline position until it is quiesced for a render or a
    /// user-driven timeline seek has temporarily taken ownership of the playhead.
    /// </summary>
    public static bool ShouldProjectAuditionPosition(
        bool isAuditionActive,
        bool isAuditionQuiesced,
        bool isTimelineSeekActive) =>
        isAuditionActive && !isAuditionQuiesced && !isTimelineSeekActive;
}

/// <summary>
/// Application-session identity for an explicit rendered-composition preview.
/// Logical IDs alone are deliberately insufficient: a copied or reopened project
/// can retain the same IDs while resolving media from a different project location.
/// </summary>
internal sealed record SessionCompositionPreviewIdentity(
    VideoProject Project,
    ProjectLocation Location,
    Guid CompositionAssetId,
    Guid RecipeRevisionId)
{
    public bool Matches(
        VideoProject? project,
        ProjectLocation? location,
        Guid compositionAssetId,
        Guid recipeRevisionId) =>
        ReferenceEquals(Project, project) &&
        ReferenceEquals(Location, location) &&
        CompositionAssetId == compositionAssetId &&
        RecipeRevisionId == recipeRevisionId;
}
