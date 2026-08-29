using System.Globalization;
using System.Text.Json.Nodes;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class MediaEncodingPersistenceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PortableProjectStoreRoundTripsResolvedStreamDescriptors()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Stream descriptors");
        var asset = new ProjectAsset
        {
            DisplayName = "source.mp4",
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                ContentIdentity = new ContentIdentity
                {
                    Sha256 = new string('a', 64),
                    Status = ContentHashStatus.Verified,
                    LengthBytes = 42
                }
            },
            Encoding = new MediaEncodingMetadata
            {
                Video = new VideoStreamMetadata
                {
                    StreamIndex = 4,
                    TimeBase = "1/90000",
                    TimeBaseNumerator = 1,
                    TimeBaseDenominator = 90000,
                    StartPresentationTimestamp = -180000,
                    DurationPresentationTimestamp = 405405
                },
                Audio = new AudioStreamMetadata
                {
                    StreamIndex = 7,
                    TimeBaseNumerator = 1,
                    TimeBaseDenominator = 48000,
                    StartPresentationTimestamp = 1024,
                    DurationPresentationTimestamp = 721920
                }
            }
        };
        project.AddAsset(asset);

        await store.SaveAsync(project, location);
        var (reopened, _) = await store.OpenAsync(location.ProjectFilePath);
        var encoding = Assert.Single(reopened.Assets).Encoding!;

        Assert.Equal(4, encoding.Video?.StreamIndex);
        Assert.Equal("1/90000", encoding.Video?.TimeBase);
        Assert.Equal(1, encoding.Video?.TimeBaseNumerator);
        Assert.Equal(90000, encoding.Video?.TimeBaseDenominator);
        Assert.Equal(-180000, encoding.Video?.StartPresentationTimestamp);
        Assert.Equal(405405, encoding.Video?.DurationPresentationTimestamp);
        Assert.Equal(7, encoding.Audio?.StreamIndex);
        Assert.Equal(1, encoding.Audio?.TimeBaseNumerator);
        Assert.Equal(48000, encoding.Audio?.TimeBaseDenominator);
        Assert.Equal(1024, encoding.Audio?.StartPresentationTimestamp);
        Assert.Equal(721920, encoding.Audio?.DurationPresentationTimestamp);
    }

    [Fact]
    public async Task PortableProjectStoreRoundTripsIndependentTimingAssessmentsAndEstimatedAcknowledgement()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Timing evidence");
        var asset = new ProjectAsset
        {
            DisplayName = "source.mp4",
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                ContentIdentity = new ContentIdentity { Sha256 = new string('a', 64), Status = ContentHashStatus.Verified }
            },
            Encoding = new MediaEncodingMetadata
            {
                Video = new VideoStreamMetadata { StreamIndex = 4 },
                Audio = new AudioStreamMetadata { StreamIndex = 7 }
            }
        };
        var video = new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Video, 4,
            TimingReadiness.Exact, true, new ExactTime(2, 1), [], new ExactTime(-1, 1));
        var audio = new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Audio, 7,
            TimingReadiness.Estimated, true, new ExactTime(96000, 48000),
            [TimingIssueClassification.UnresolvedAudioPrimingOrPadding], new ExactTime(0, 1));
        asset.SetTimingAssessment(video);
        asset.SetTimingAssessment(audio);
        project.AddAsset(asset);
        var acknowledgedAt = DateTimeOffset.Parse("2026-08-28T10:00:00Z", CultureInfo.InvariantCulture);
        project.AcknowledgeEstimatedTimingAssessment(audio.AssessmentId, acknowledgedAt);

        await store.SaveAsync(project, location);
        var (reopened, _) = await store.OpenAsync(location.ProjectFilePath);
        var reopenedAsset = Assert.Single(reopened.Assets);
        var reopenedVideo = reopenedAsset.TimingAssessments.Single(assessment => assessment.MediaType == MediaType.Video);
        var reopenedAudio = reopenedAsset.TimingAssessments.Single(assessment => assessment.MediaType == MediaType.Audio);

        Assert.Equal(video.AssessmentId, reopenedVideo.AssessmentId);
        Assert.Equal(new ExactTime(-1, 1), reopenedVideo.SourcePresentationStart);
        Assert.Equal(TimingReadiness.Exact, reopenedVideo.Readiness);
        Assert.Equal(audio.AssessmentId, reopenedAudio.AssessmentId);
        Assert.Equal(new ExactTime(2, 1), reopenedAudio.TimelineDuration);
        Assert.Equal([TimingIssueClassification.UnresolvedAudioPrimingOrPadding], reopenedAudio.IssueClassifications);
        var acknowledgement = Assert.Single(reopened.TimingAssessmentAcknowledgements);
        Assert.Equal(audio.AssessmentId, acknowledgement.AssessmentId);
        Assert.Equal(acknowledgedAt, acknowledgement.AcknowledgedAt);
    }

    [Fact]
    public async Task NullAssetTimingAssessmentsIsReportedAsProjectDataError()
    {
        var (store, location, document) = await TimingDocumentAsync();
        document["assets"]!.AsArray()[0]!["timingAssessments"] = null;
        await File.WriteAllTextAsync(location.ProjectFilePath, document.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(location.ProjectFilePath));
    }

    [Fact]
    public async Task NullTimingIssuesIsReportedAsProjectDataError()
    {
        var (store, location, document) = await TimingDocumentAsync();
        document["assets"]!.AsArray()[0]!["timingAssessments"]!.AsArray()[0]!["issueClassifications"] = null;
        await File.WriteAllTextAsync(location.ProjectFilePath, document.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(location.ProjectFilePath));
    }

    [Fact]
    public async Task InvalidTimingExactTimeIsReportedAsProjectDataError()
    {
        var (store, location, document) = await TimingDocumentAsync();
        document["assets"]!.AsArray()[0]!["timingAssessments"]!.AsArray()[0]!["timelineDuration"]!["denominator"] = 0;
        await File.WriteAllTextAsync(location.ProjectFilePath, document.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(location.ProjectFilePath));
    }

    [Fact]
    public async Task NullTimingAcknowledgementCollectionIsReportedAsProjectDataError()
    {
        var (store, location, document) = await TimingDocumentAsync();
        document["timingAssessmentAcknowledgements"] = null;
        await File.WriteAllTextAsync(location.ProjectFilePath, document.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(location.ProjectFilePath));
    }

    [Fact]
    public async Task NullTimingAcknowledgementEntryIsReportedAsProjectDataError()
    {
        var (store, location, document) = await TimingDocumentAsync(includeAcknowledgement: true);
        document["timingAssessmentAcknowledgements"]!.AsArray()[0] = null;
        await File.WriteAllTextAsync(location.ProjectFilePath, document.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(location.ProjectFilePath));
    }

    [Fact]
    public async Task PlaceableTimingPayloadWithoutMatchingDescriptorIsRejectedOnOpen()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Invalid descriptor");
        project.AddAsset(PhysicalVideoWithTiming());
        await store.SaveAsync(project, location);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(location.ProjectFilePath))!.AsObject();
        document["assets"]!.AsArray()[0]!["encoding"]!["video"]!["streamIndex"] = 8;
        await File.WriteAllTextAsync(location.ProjectFilePath, document.ToJsonString());

        await Assert.ThrowsAsync<ProjectValidationException>(() => store.OpenAsync(location.ProjectFilePath));
    }

    private static ProjectAsset PhysicalVideoWithTiming()
    {
        var asset = new ProjectAsset
        {
            MediaType = MediaType.Video,
            FileName = "source.mp4",
            Physical = new PhysicalAssetStorage { RelativePath = "assets/videos/source.mp4", ContentIdentity = new ContentIdentity { Sha256 = new string('a', 64), Status = ContentHashStatus.Verified } },
            Encoding = new MediaEncodingMetadata { Video = new VideoStreamMetadata { StreamIndex = 4 } }
        };
        asset.SetTimingAssessment(new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Video, 4,
            TimingReadiness.Exact, true, new ExactTime(1, 1), [], new ExactTime(0, 1)));
        return asset;
    }

    private async Task<(PortableProjectStore Store, ProjectLocation Location, JsonObject Document)> TimingDocumentAsync(bool includeAcknowledgement = false)
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Invalid timing");
        var asset = PhysicalVideoWithTiming();
        project.AddAsset(asset);
        if (includeAcknowledgement)
            project.TimingAssessmentAcknowledgements.Add(new TimingAssessmentAcknowledgement(asset.TimingAssessments.Single().AssessmentId, DateTimeOffset.UtcNow));
        await store.SaveAsync(project, location);
        return (store, location, JsonNode.Parse(await File.ReadAllTextAsync(location.ProjectFilePath))!.AsObject());
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
