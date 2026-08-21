using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Tests;

public sealed class CompositionAuditionPlanTests
{
    [Fact]
    public void CreateProjectsPinnedSegmentsOntoOneContiguousTimeline()
    {
        var first = Video("first.mp4", 10);
        var second = Video("second.mp4", 8);
        var project = Project(first, second);
        var recipe = new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment
                {
                    Source = new AssetRevisionReference { AssetId = first.Id },
                    Start = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 2 },
                    End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 7 },
                    AudioEnabled = false
                },
                new CompositionSegment
                {
                    Source = new AssetRevisionReference { AssetId = second.Id },
                    Start = RecipeBoundary.SourceStart,
                    End = RecipeBoundary.SourceEnd
                }
            ]
        };

        var plan = CompositionAuditionPlan.Create(project, recipe);

        Assert.Equal(13, plan.DurationSeconds);
        Assert.Collection(
            plan.Segments,
            segment =>
            {
                Assert.Equal(0, segment.TimelineStartSeconds);
                Assert.Equal(2, segment.SourceStartSeconds);
                Assert.Equal(5, segment.DurationSeconds);
                Assert.False(segment.AudioEnabled);
            },
            segment =>
            {
                Assert.Equal(5, segment.TimelineStartSeconds);
                Assert.Equal(0, segment.SourceStartSeconds);
                Assert.Equal(8, segment.DurationSeconds);
                Assert.True(segment.AudioEnabled);
            });
    }

    [Fact]
    public void PositionMappingSelectsTheTrailingSegmentAtAnExactCut()
    {
        var first = Video("first.mp4", 4);
        var second = Video("second.mp4", 6);
        var plan = CompositionAuditionPlan.Create(Project(first, second), new CompositionRecipe
        {
            Segments =
            [
                Segment(first),
                Segment(second, startSeconds: 1, endSeconds: 5)
            ]
        });

        Assert.Equal(0, plan.FindSegmentIndex(3.999));
        Assert.Equal(1, plan.FindSegmentIndex(4));
        Assert.Equal(1, plan.FindSegmentIndex(50));
        Assert.Equal(2.5, plan.GetSourcePosition(1, 5.5));
        Assert.Equal(5.5, plan.GetGlobalPosition(1, 2.5));
    }

    [Fact]
    public void AnchorBoundaryIsResolvedFromThePinnedRevision()
    {
        var source = Video("source.mp4", 10);
        var project = Project(source);
        var anchor = new FrameAnchor();
        project.Anchors.Add(anchor);
        var revision = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            source.Id,
            new string('a', 64),
            0,
            72,
            1,
            24,
            72));
        var segment = new CompositionSegment
        {
            Source = new AssetRevisionReference { AssetId = source.Id },
            Start = new RecipeBoundary
            {
                Kind = RecipeBoundaryKind.Anchor,
                Anchor = new AnchorRevisionReference
                {
                    AnchorId = anchor.Id,
                    AnchorRevisionId = revision.Id
                }
            },
            End = RecipeBoundary.SourceEnd
        };

        var plan = CompositionAuditionPlan.Create(project, new CompositionRecipe { Segments = [segment] });

        Assert.Equal(3, plan.Segments[0].SourceStartSeconds);
        Assert.Equal(7, plan.Segments[0].DurationSeconds);
    }

    [Fact]
    public void CreateRejectsEmptyMissingAndUnknownDurationCompositions()
    {
        var source = Video("source.mp4", null);
        var project = Project(source);

        Assert.Throws<InvalidDataException>(() =>
            CompositionAuditionPlan.Create(project, new CompositionRecipe()));
        Assert.Throws<InvalidDataException>(() =>
            CompositionAuditionPlan.Create(project, new CompositionRecipe
            {
                Segments = [Segment(new ProjectAsset { Id = Guid.NewGuid() })]
            }));
        Assert.Throws<InvalidDataException>(() =>
            CompositionAuditionPlan.Create(project, new CompositionRecipe { Segments = [Segment(source)] }));
    }

    [Fact]
    public void NavigationAndMappingClampToPlanAndSegmentBounds()
    {
        var source = Video("source.mp4", 5);
        var plan = CompositionAuditionPlan.Create(Project(source), new CompositionRecipe
        {
            Segments = [Segment(source, startSeconds: 1, endSeconds: 4)]
        });

        Assert.Equal(0, plan.ClampGlobalPosition(double.NaN));
        Assert.Equal(3, plan.ClampGlobalPosition(20));
        Assert.Equal(1, plan.GetSourcePosition(0, -2));
        Assert.Equal(4, plan.GetSourcePosition(0, 50));
        Assert.Equal(0, plan.GetGlobalPosition(0, -1));
        Assert.Equal(3, plan.GetGlobalPosition(0, 10));
        Assert.False(plan.TryGetNextSegmentIndex(0, out var nextIndex));
        Assert.Equal(1, nextIndex);
    }

    private static VideoProject Project(params ProjectAsset[] assets)
    {
        var project = new VideoProject();
        project.Assets.AddRange(assets);
        return project;
    }

    private static ProjectAsset Video(string name, double? durationSeconds) => new()
    {
        FileName = name,
        DisplayName = name,
        MediaType = MediaType.Video,
        DurationSeconds = durationSeconds,
        Physical = new PhysicalAssetStorage
        {
            ContentIdentity = new ContentIdentity
            {
                Sha256 = new string('a', 64),
                Status = ContentHashStatus.Verified
            }
        }
    };

    private static CompositionSegment Segment(
        ProjectAsset source,
        double? startSeconds = null,
        double? endSeconds = null) => new()
    {
        Source = new AssetRevisionReference { AssetId = source.Id },
        Start = startSeconds is { } start
            ? new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = start }
            : RecipeBoundary.SourceStart,
        End = endSeconds is { } end
            ? new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = end }
            : RecipeBoundary.SourceEnd
    };
}
