using System.Globalization;
using ReelForge.Core;

namespace ReelForge.Core.Tests;

public sealed class ProjectTimingAssessmentStateTests
{
    [Fact]
    public void PhysicalVideoAssetRetainsIndependentCurrentVideoAndAudioAssessments()
    {
        var asset = VideoAsset();
        var video = Assessment(MediaType.Video, 4, TimingReadiness.Exact);
        var audio = Assessment(MediaType.Audio, 7, TimingReadiness.Estimated);

        asset.SetTimingAssessment(video);
        asset.SetTimingAssessment(audio);

        Assert.Equal(2, asset.TimingAssessments.Count);
        Assert.Contains(video, asset.TimingAssessments);
        Assert.Contains(audio, asset.TimingAssessments);
    }

    [Fact]
    public void ReplacingCurrentMediaTypeAssessmentRequiresNewAcknowledgement()
    {
        var asset = VideoAsset();
        var old = Assessment(MediaType.Video, 4, TimingReadiness.Estimated);
        var replacement = Assessment(MediaType.Video, 4, TimingReadiness.Estimated);
        asset.SetTimingAssessment(old);
        var project = new VideoProject();
        project.AddAsset(asset);
        var acknowledgedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z", CultureInfo.InvariantCulture);

        project.AcknowledgeEstimatedTimingAssessment(old.AssessmentId, acknowledgedAt);
        var modified = project.ModifiedAt;
        project.AcknowledgeEstimatedTimingAssessment(old.AssessmentId, acknowledgedAt.AddMinutes(1));
        Assert.Equal(modified, project.ModifiedAt);

        asset.SetTimingAssessment(replacement);
        Assert.Throws<InvalidOperationException>(() => project.AcknowledgeEstimatedTimingAssessment(old.AssessmentId, acknowledgedAt));
        project.AcknowledgeEstimatedTimingAssessment(replacement.AssessmentId, acknowledgedAt.AddMinutes(2));
        Assert.Equal(2, project.TimingAssessmentAcknowledgements.Count);
    }

    [Fact]
    public void AssessmentMustMatchVerifiedIdentityAllowedTypeAndSelectedDescriptor()
    {
        var asset = VideoAsset();
        Assert.Throws<InvalidOperationException>(() => asset.SetTimingAssessment(Assessment(MediaType.Video, 5, TimingReadiness.Exact)));
        Assert.Throws<InvalidOperationException>(() => asset.SetTimingAssessment(Assessment(MediaType.Video, 4, TimingReadiness.Exact, new string('b', 64))));

        var audioAsset = new ProjectAsset
        {
            MediaType = MediaType.Audio,
            Physical = new PhysicalAssetStorage { RelativePath = "assets/audio/a.m4a", ContentIdentity = VerifiedIdentity() },
            Encoding = new MediaEncodingMetadata { Audio = new AudioStreamMetadata { StreamIndex = 7 } }
        };
        Assert.Throws<InvalidOperationException>(() => audioAsset.SetTimingAssessment(Assessment(MediaType.Video, 4, TimingReadiness.Exact)));

        asset.Encoding!.Video = null;
        Assert.Throws<InvalidOperationException>(() => asset.SetTimingAssessment(Assessment(MediaType.Video, 4, TimingReadiness.Exact)));

        var unusable = new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Video, null,
            TimingReadiness.Unusable, false, null, [TimingIssueClassification.NoUsableStream]);
        asset.SetTimingAssessment(unusable);
    }

    [Fact]
    public void ValidatorRejectsDuplicateAndInvalidCurrentAssessmentStateButPermitsHistoricalAcknowledgement()
    {
        var asset = VideoAsset();
        var first = Assessment(MediaType.Video, 4, TimingReadiness.Estimated);
        var duplicateType = Assessment(MediaType.Video, 4, TimingReadiness.Estimated);
        asset.TimingAssessments.Add(first);
        asset.TimingAssessments.Add(duplicateType);
        var project = new VideoProject { Assets = [asset], TimingAssessmentAcknowledgements = [new TimingAssessmentAcknowledgement(first.AssessmentId, DateTimeOffset.UtcNow), new TimingAssessmentAcknowledgement(first.AssessmentId, DateTimeOffset.UtcNow)] };

        var errors = ProjectInvariantValidator.Validate(project);

        Assert.Contains(errors, error => error.Contains("duplicate current Video", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Duplicate timing assessment acknowledgement", StringComparison.Ordinal));

        project.TimingAssessmentAcknowledgements = [new TimingAssessmentAcknowledgement(first.AssessmentId, DateTimeOffset.UtcNow)];
        asset.TimingAssessments = [duplicateType];
        Assert.Empty(ProjectInvariantValidator.Validate(project));
    }

    private static ProjectAsset VideoAsset() => new()
    {
        MediaType = MediaType.Video,
        Physical = new PhysicalAssetStorage { RelativePath = "assets/videos/v.mp4", ContentIdentity = VerifiedIdentity() },
        Encoding = new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata { StreamIndex = 4 },
            Audio = new AudioStreamMetadata { StreamIndex = 7 }
        }
    };

    private static ContentIdentity VerifiedIdentity() => new() { Status = ContentHashStatus.Verified, Sha256 = new string('a', 64) };
    private static StreamTimingAssessment Assessment(MediaType type, int index, TimingReadiness readiness, string? hash = null) => new(
        Guid.NewGuid(), hash ?? new string('a', 64), type, index, readiness, true, new ExactTime(1, 1),
        readiness == TimingReadiness.Exact ? [] : [TimingIssueClassification.TerminalBoundaryUnavailable], new ExactTime(0, 1));
}
