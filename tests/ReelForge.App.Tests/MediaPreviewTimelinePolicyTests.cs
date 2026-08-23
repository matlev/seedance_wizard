using ReelForge.App.Views.MediaPreview;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Tests;

#pragma warning disable CA1707 // Test names describe behavior with readable clauses.
public sealed class MediaPreviewTimelinePolicyTests
{
    [Fact]
    public void OrdinaryProjectMediaTick_DoesNotProjectTheCompositionTimeline() =>
        Assert.False(MediaPreviewTimelinePolicy.ShouldProjectMediaTick(isBakedCompositionPreview: false));

    [Fact]
    public void BakedCompositionTick_ProjectsTheCompositionTimeline() =>
        Assert.True(MediaPreviewTimelinePolicy.ShouldProjectMediaTick(isBakedCompositionPreview: true));

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    public void AuditionPosition_ProjectsOnlyWhenActiveUnquiescedAndNotSeeking(
        bool active,
        bool quiesced,
        bool timelineSeekActive,
        bool expected) =>
        Assert.Equal(
            expected,
            MediaPreviewTimelinePolicy.ShouldProjectAuditionPosition(active, quiesced, timelineSeekActive));

    [Fact]
    public void RetainedPreviewIdentity_MatchesTheExactProjectAndLocationInstances()
    {
        var project = new VideoProject();
        var location = new ProjectLocation("C:\\projects\\one", "C:\\projects\\one\\one.rfp");
        var compositionId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var identity = new SessionCompositionPreviewIdentity(project, location, compositionId, revisionId);

        Assert.True(identity.Matches(project, location, compositionId, revisionId));
        Assert.False(identity.Matches(
            new VideoProject { Id = project.Id },
            location,
            compositionId,
            revisionId));
        Assert.False(identity.Matches(
            project,
            new ProjectLocation(location.RootDirectory, location.ProjectFilePath),
            compositionId,
            revisionId));
    }

    [Fact]
    public void RetainedPreviewIdentity_DoesNotMatchAnotherCompositionOrRevision()
    {
        var project = new VideoProject();
        var location = new ProjectLocation("C:\\projects\\one", "C:\\projects\\one\\one.rfp");
        var identity = new SessionCompositionPreviewIdentity(project, location, Guid.NewGuid(), Guid.NewGuid());

        Assert.False(identity.Matches(project, location, Guid.NewGuid(), identity.RecipeRevisionId));
        Assert.False(identity.Matches(project, location, identity.CompositionAssetId, Guid.NewGuid()));
    }
}
