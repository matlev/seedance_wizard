using ReelForge.App.Views.Editing;
using ReelForge.App.Views.Inspector;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class TimingWarningPresentationTests
{
    [Fact]
    public void ProjectMediaPrioritizesDerivedAndMissingWarningsOverCurrentTimingWarnings()
    {
        var estimated = PhysicalAsset(MediaType.Video, TimingReadiness.Estimated);
        var timingItem = new ProjectMediaListItem(estimated);

        Assert.True(timingItem.IsTimingDegraded);
        Assert.Equal("⚠", timingItem.Glyph);
        Assert.Contains("Timing is estimated", timingItem.GlyphToolTip);

        estimated.Physical!.Availability = PhysicalAssetAvailability.Missing;
        var missingItem = new ProjectMediaListItem(estimated);
        Assert.Contains("Right-click", missingItem.GlyphToolTip);

        var derived = new ProjectMediaListItem(estimated, isDegraded: true);
        Assert.Contains("Cleanup Project", derived.GlyphToolTip);
    }

    [Fact]
    public void ProjectMediaShowsUnusableCurrentTimingWithRepairGuidance()
    {
        var item = new ProjectMediaListItem(PhysicalAsset(MediaType.Audio, TimingReadiness.Unusable));

        Assert.True(item.IsTimingDegraded);
        Assert.Contains("cannot be placed", item.GlyphToolTip);
    }

    [Fact]
    public void InspectorShowsIndependentTimingAssessmentsAndHumanReadableIssues()
    {
        var asset = PhysicalAsset(MediaType.Video, TimingReadiness.Estimated);
        asset.TimingAssessments.Add(Assessment(MediaType.Audio, TimingReadiness.Exact));

        var text = InspectorTextFormatter.FormatAsset(asset);

        Assert.Contains("VIDEO TIMING", text);
        Assert.Contains("AUDIO TIMING", text);
        Assert.Contains("Video timing: Estimated", text);
        Assert.Contains("Audio timing: Exact", text);
        Assert.Contains("Selected stream: 0", text);
        Assert.Contains(StreamTimingAssessment.CurrentSchemaIdentity, text);
        Assert.Contains("source duration unavailable", text);
        Assert.Contains("Precise editing may require repair or replacement", text);
    }

    [Fact]
    public void TimelineItemsAndSummaryProjectEstimatedPinnedTiming()
    {
        var source = new ProjectAsset { DisplayName = "Source", FileName = "source.mp4" };
        var video = new CompositionVideoItem(
            Guid.NewGuid(),
            new AssetRevisionReference { AssetId = source.Id, RecipeRevisionId = Guid.NewGuid() },
            0,
            null,
            new StreamTimingAssessmentPin(Assessment(MediaType.Video, TimingReadiness.Estimated)),
            new ExactTime(0, 1));
        var audio = new CompositionAudioItem(
            Guid.NewGuid(),
            new AssetRevisionReference { AssetId = source.Id, RecipeRevisionId = Guid.NewGuid() },
            0,
            null,
            new StreamTimingAssessmentPin(Assessment(MediaType.Audio, TimingReadiness.Estimated)),
            new ExactTime(0, 1));

        var videoItem = new CompositionSegmentListItem(0, Guid.NewGuid(), video, source, false);
        var audioItem = new CompositionAudioClipListItem(Guid.NewGuid(), audio, source);
        var state = CompositionTimelineState.Empty with { Segments = [videoItem], AudioClips = [audioItem] };

        Assert.True(videoItem.IsTimingDegraded);
        Assert.True(audioItem.IsTimingDegraded);
        Assert.Contains("Timing is estimated", videoItem.TimingWarningToolTip);
        Assert.Contains("Timing is estimated", audioItem.TimingWarningToolTip);
        Assert.Equal(2, state.DegradedOccurrenceCount);
        Assert.Contains("2 occurrences", state.TimingWarningSummary);
    }

    [Fact]
    public void EditSelectionStateCarriesTimingWarningDetailWithoutAffectingExactState()
    {
        var degraded = new VideoSegmentEditState("Clip", "Source", "Timing", true, false,
            IsTimingDegraded: true,
            TimingWarningDetail: "Video timing: Estimated");
        var exact = new AudioClipEditState("Audio", "Timing", false, 0, 0, TimeSpan.Zero, TimeSpan.Zero, 0, true);

        Assert.True(degraded.IsTimingDegraded);
        Assert.Contains("Estimated", degraded.TimingWarningDetail);
        Assert.False(exact.IsTimingDegraded);
        Assert.Null(exact.TimingWarningDetail);
    }

    private static ProjectAsset PhysicalAsset(MediaType mediaType, TimingReadiness readiness)
    {
        var hash = new string('a', 64);
        var asset = new ProjectAsset
        {
            FileName = mediaType == MediaType.Audio ? "source.wav" : "source.mp4",
            DisplayName = "Source",
            MediaType = mediaType,
            StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage
            {
                Availability = PhysicalAssetAvailability.Available,
                ContentIdentity = new ContentIdentity { Status = ContentHashStatus.Verified, Sha256 = hash }
            }
        };
        asset.TimingAssessments.Add(Assessment(mediaType, readiness));
        return asset;
    }

    private static StreamTimingAssessment Assessment(MediaType mediaType, TimingReadiness readiness) => new(
        Guid.NewGuid(),
        new string('a', 64),
        mediaType,
        0,
        readiness,
        readiness != TimingReadiness.Unusable,
        readiness == TimingReadiness.Unusable ? null : new ExactTime(3, 1),
        readiness switch
        {
            TimingReadiness.Exact => [],
            TimingReadiness.Estimated => [TimingIssueClassification.NativeDurationUnavailable],
            _ => [TimingIssueClassification.NoUsableStream]
        },
        readiness == TimingReadiness.Exact ? new ExactTime(0, 1) : null);
}
