using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class CompositionAuditionPlanTests
{
    [Fact]
    public void CreateProjectsTheSingleVisibleTrackFromPinnedTimingEvidence()
    {
        var first = Video("first.mp4", durationSeconds: null);
        var second = Video("second.mp4", durationSeconds: 99);
        var recipe = Recipe(
            VideoItem(first, compositionStart: 0, sourceStart: 10, duration: 4),
            VideoItem(second, compositionStart: 4, sourceStart: 20, duration: 6));

        var plan = CompositionAuditionPlan.Create(Project(first, second), recipe);

        Assert.Equal(10, plan.DurationSeconds);
        Assert.Collection(
            plan.Segments,
            segment =>
            {
                Assert.Equal(0, segment.TimelineStartSeconds);
                Assert.Equal(10, segment.SourceStartSeconds);
                Assert.Equal(4, segment.DurationSeconds);
                Assert.False(segment.AudioEnabled);
            },
            segment =>
            {
                Assert.Equal(4, segment.TimelineStartSeconds);
                Assert.Equal(20, segment.SourceStartSeconds);
                Assert.Equal(6, segment.DurationSeconds);
                Assert.False(segment.AudioEnabled);
            });
    }

    [Fact]
    public void CreateOrdersItemsByExactCompositionStart()
    {
        var first = Video("first.mp4");
        var second = Video("second.mp4");
        var firstItem = VideoItem(first, compositionStart: 0, sourceStart: 2, duration: 3);
        var secondItem = VideoItem(second, compositionStart: 3, sourceStart: 8, duration: 2);

        var plan = CompositionAuditionPlan.Create(Project(first, second), Recipe(secondItem, firstItem));

        Assert.Collection(
            plan.Segments,
            segment => Assert.Equal(firstItem.Id, segment.SegmentId),
            segment => Assert.Equal(secondItem.Id, segment.SegmentId));
    }

    [Fact]
    public void EstimatedItemUsesItsFrozenPinInsteadOfAssetDuration()
    {
        var source = Video("source.mp4", durationSeconds: 999);
        var item = VideoItem(source, compositionStart: 0, sourceStart: 7, duration: 3,
            readiness: TimingReadiness.Estimated);

        var plan = CompositionAuditionPlan.Create(Project(source), Recipe(item));

        var segment = Assert.Single(plan.Segments);
        Assert.Equal(7, segment.SourceStartSeconds);
        Assert.Equal(3, segment.DurationSeconds);
        Assert.Equal(3, plan.DurationSeconds);
    }

    [Fact]
    public void CreateRejectsShapesTheSequentialAuditionSessionCannotRepresent()
    {
        var first = Video("first.mp4");
        var second = Video("second.mp4");

        var multipleVisible = new CompositionRecipe
        {
            Composition = new WorkingCompositionState(
                [new CompositionVideoTrack(Guid.NewGuid(), false, true, [VideoItem(first, 0, 0, 1)]),
                 new CompositionVideoTrack(Guid.NewGuid(), false, true, [VideoItem(second, 0, 0, 1)])],
                [])
        };
        var gap = Recipe(VideoItem(first, 0, 0, 1), VideoItem(second, 2, 0, 1));
        var overlap = Recipe(VideoItem(first, 0, 0, 2), VideoItem(second, 1, 0, 1));
        var empty = new CompositionRecipe { Composition = new WorkingCompositionState([], []) };

        Assert.Contains("exactly one visible video track", Assert.Throws<InvalidDataException>(() =>
            CompositionAuditionPlan.Create(Project(first, second), multipleVisible)).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gap", Assert.Throws<InvalidDataException>(() =>
            CompositionAuditionPlan.Create(Project(first, second), gap)).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overlapping", Assert.Throws<InvalidDataException>(() =>
            CompositionAuditionPlan.Create(Project(first, second), overlap)).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one visible video track", Assert.Throws<InvalidDataException>(() =>
            CompositionAuditionPlan.Create(Project(first, second), empty)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PositionMappingSelectsTheTrailingItemAtAnExactCut()
    {
        var first = Video("first.mp4");
        var second = Video("second.mp4");
        var plan = CompositionAuditionPlan.Create(Project(first, second), Recipe(
            VideoItem(first, 0, 10, 4),
            VideoItem(second, 4, 20, 6)));

        Assert.Equal(0, plan.FindSegmentIndex(3.999));
        Assert.Equal(1, plan.FindSegmentIndex(4));
        Assert.Equal(1, plan.FindSegmentIndex(50));
        Assert.Equal(21.5, plan.GetSourcePosition(1, 5.5));
        Assert.Equal(5.5, plan.GetGlobalPosition(1, 21.5));
    }

    [Fact]
    public void NavigationAndSessionProgressionClampToPinnedItemBounds()
    {
        var first = Video("first.mp4");
        var second = Video("second.mp4");
        var plan = CompositionAuditionPlan.Create(Project(first, second), Recipe(
            VideoItem(first, 0, 1, 2),
            VideoItem(second, 2, 7, 3)));
        var session = new CompositionAuditionSession(Guid.NewGuid(), plan, 1.5);

        Assert.Equal(0, plan.ClampGlobalPosition(double.NaN));
        Assert.Equal(5, plan.ClampGlobalPosition(20));
        Assert.Equal(1, plan.GetSourcePosition(0, -2));
        Assert.Equal(3, plan.GetSourcePosition(0, 50));
        Assert.Equal(0, plan.GetGlobalPosition(0, -1));
        Assert.Equal(2, plan.GetGlobalPosition(0, 10));
        Assert.True(session.TryAdvance(out var advanced));
        Assert.Equal(1, advanced.SegmentIndex);
        Assert.Equal(2, advanced.GlobalSeconds);
        Assert.Equal(7, advanced.SourceSeconds);
        Assert.Equal(5, session.Complete().GlobalSeconds);
    }

    private static CompositionRecipe Recipe(params CompositionVideoItem[] items) => new()
    {
        Composition = new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true, items)], [])
    };

    private static VideoProject Project(params ProjectAsset[] assets) => new() { Assets = [.. assets] };

    private static ProjectAsset Video(string name, double? durationSeconds = null) => new()
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

    private static CompositionVideoItem VideoItem(
        ProjectAsset source,
        long compositionStart,
        long sourceStart,
        long duration,
        TimingReadiness readiness = TimingReadiness.Exact)
    {
        var start = new VideoPresentationTime(sourceStart, 1, 1);
        var end = new VideoPresentationTime(sourceStart + duration, 1, 1);
        var assessment = new StreamTimingAssessment(
            Guid.NewGuid(),
            new string('a', 64),
            MediaType.Video,
            0,
            readiness,
            hasUsableSequentialDecodePath: true,
            new ExactTime(duration, 1),
            readiness == TimingReadiness.Exact ? [] : [TimingIssueClassification.NativeDurationUnavailable],
            new ExactTime(sourceStart, 1));
        return new CompositionVideoItem(
            Guid.NewGuid(),
            new AssetRevisionReference { AssetId = source.Id },
            0,
            readiness == TimingReadiness.Exact ? new VideoSourceRange(start, end) : null,
            assessment.CreatePlacementPin(),
            new ExactTime(compositionStart, 1));
    }
}
