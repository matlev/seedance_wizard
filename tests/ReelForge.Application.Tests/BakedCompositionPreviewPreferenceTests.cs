using ReelForge.Application;

namespace ReelForge.Application.Tests;

public sealed class BakedCompositionPreviewPreferenceTests
{
    [Fact]
    public void MatchesRequiresExactPathCompositionAndRevision()
    {
        var preference = new BakedCompositionPreviewPreference
        {
            ProjectFilePath = "C:\\Projects\\One\\one.rfp",
            CompositionAssetId = Guid.NewGuid(),
            RecipeRevisionId = Guid.NewGuid()
        };

        Assert.True(preference.Matches(
            "c:\\projects\\one\\ONE.rfp", preference.CompositionAssetId, preference.RecipeRevisionId));
        Assert.False(preference.Matches(
            "C:\\Projects\\Two\\one.rfp", preference.CompositionAssetId, preference.RecipeRevisionId));
        Assert.False(preference.Matches(
            preference.ProjectFilePath, Guid.NewGuid(), preference.RecipeRevisionId));
        Assert.False(preference.Matches(
            preference.ProjectFilePath, preference.CompositionAssetId, Guid.NewGuid()));
    }
}
