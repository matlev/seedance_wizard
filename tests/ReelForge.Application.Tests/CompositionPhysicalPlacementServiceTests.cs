using ReelForge.Application;
using ReelForge.Application.Editing.Composition;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class CompositionPhysicalPlacementServiceTests
{
    [Fact]
    public async Task ExactLinkedVideoAndAudioPlaceAsOneRevisionWithSharedLinkGroup()
    {
        var source = VideoSource(withAudio: true);
        var (workspace, store, videoTrack, audioTrack) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(workspace,
            new Timing(ExactVideo(), ExactAudio()), new MatchingHash(), new Decisions());

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(3, 1), videoTrack, audioTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        Assert.Equal(2, store.SaveCount); // assessment, then composition + acknowledgement (none needed here)
        var state = Current(workspace);
        var video = Assert.Single(state.VideoTracks.Single().Items);
        var audio = Assert.Single(state.AudioTracks.Single().Items);
        Assert.Equal(result.LinkGroupId, video.LinkGroupId);
        Assert.Equal(video.LinkGroupId, audio.LinkGroupId);
        Assert.Equal(new ExactTime(3, 1), video.CompositionStart);
        Assert.Equal(new ExactTime(3, 1), audio.CompositionStart);
        Assert.NotNull(video.SourceRange);
        Assert.NotNull(audio.SourceRange);
        Assert.Null(video.Source.RecipeRevisionId);
    }

    [Fact]
    public async Task VideoAppendUsesExactTargetEndsInsteadOfTheRawPointerTime()
    {
        var source = VideoSource(withAudio: true);
        var (workspace, _, videoTrack, audioTrack) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(workspace,
            new Timing(ExactVideo(), ExactAudio()), new MatchingHash(), new Decisions());

        await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack, audioTrack));
        var result = await service.PlaceAsync(new(
            source.Id,
            new ExactTime(9055, 10000),
            videoTrack,
            audioTrack,
            CompositionPhysicalPlacementMode.AppendToVideoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        var state = Current(workspace);
        Assert.Equal(
            [new ExactTime(0, 1), new ExactTime(1, 1)],
            state.VideoTracks.Single().Items.Select(item => item.CompositionStart));
        Assert.Equal(
            [new ExactTime(0, 1), new ExactTime(1, 1)],
            state.AudioTracks.Single().Items.Select(item => item.CompositionStart));
        _ = CompositionAuditionPlan.Create(workspace.Project!,
            (CompositionRecipe)workspace.Project!.RecipeRevisions.Single(revision =>
                revision.Id == workspace.Project.Assets.Single(asset => asset.Id == workspace.Project.WorkingCompositionAssetId)
                    .Virtual!.CurrentRecipeRevisionId).Recipe);
    }

    [Fact]
    public async Task VideoAppendPreservesThePinnedLinkedAudioOffset()
    {
        var source = VideoSource(withAudio: true);
        var (workspace, _, videoTrack, audioTrack) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(workspace,
            new Timing(ExactVideo(), ExactAudioAt(48000)), new MatchingHash(), new Decisions());

        await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack, audioTrack));
        await service.PlaceAsync(new(
            source.Id,
            new ExactTime(1, 2),
            videoTrack,
            audioTrack,
            CompositionPhysicalPlacementMode.AppendToVideoTrack));

        var state = Current(workspace);
        Assert.Equal(
            [new ExactTime(0, 1), new ExactTime(1, 1)],
            state.VideoTracks.Single().Items.Select(item => item.CompositionStart));
        Assert.Equal(
            [new ExactTime(1, 1), new ExactTime(2, 1)],
            state.AudioTracks.Single().Items.Select(item => item.CompositionStart));
    }

    [Fact]
    public async Task EstimatedPlacementAcknowledgesOnceAndDoesNotPromptAgainForUnchangedAssessment()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, _, videoTrack, _) = await OpenAsync(source);
        var decisions = new Decisions(new CompositionPlacementDecision(CompositionPlacementAction.Place, AcknowledgeEstimatedTiming: true));
        var assessment = EstimatedVideo();
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(assessment), new MatchingHash(), decisions);

        var first = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));
        var second = await service.PlaceAsync(new(source.Id, new ExactTime(2, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, first.Status);
        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, second.Status);
        Assert.Equal(1, decisions.Calls);
        Assert.Single(workspace.Project!.TimingAssessmentAcknowledgements);
        Assert.Equal(2, Current(workspace).VideoTracks.Single().Items.Count);
    }

    [Fact]
    public async Task EstimatedCancellationPersistsAssessmentButCreatesNoOccurrenceOrAcknowledgement()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, _, videoTrack, _) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(EstimatedVideo()), new MatchingHash(),
            new Decisions(new CompositionPlacementDecision(CompositionPlacementAction.Cancel)));

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Cancelled, result.Status);
        Assert.Single(source.TimingAssessments);
        Assert.Empty(workspace.Project!.TimingAssessmentAcknowledgements);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
    }

    [Fact]
    public async Task UnusableVideoBlocksWithoutSavingAnOccurrence()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, _, videoTrack, _) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(UnusableVideo()), new MatchingHash(), new Decisions());

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Blocked, result.Status);
        Assert.Single(source.TimingAssessments);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
    }

    [Fact]
    public async Task UnusableAudioRequiresExplicitVideoOnlyConsent()
    {
        var source = VideoSource(withAudio: true);
        var (workspace, _, videoTrack, _) = await OpenAsync(source);
        var denied = new CompositionPhysicalPlacementService(workspace, new Timing(ExactVideo(), UnusableAudio()), new MatchingHash(),
            new Decisions(new CompositionPlacementDecision(CompositionPlacementAction.Place)));

        var blocked = await denied.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));
        Assert.Equal(CompositionPhysicalPlacementStatus.Blocked, blocked.Status);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);

        var accepted = new CompositionPhysicalPlacementService(workspace, new Timing(ExactVideo(), UnusableAudio()), new MatchingHash(),
            new Decisions(new CompositionPlacementDecision(CompositionPlacementAction.Place, ApproveVideoOnlyWithoutUsableAudio: true)));
        var placed = await accepted.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));
        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, placed.Status);
        Assert.Single(Current(workspace).VideoTracks.Single().Items);
        Assert.Empty(Current(workspace).AudioTracks.Single().Items);
    }

    [Fact]
    public async Task UnusableAudioCanPlaceVideoOnlyWhenNoAudioTrackExists()
    {
        var source = VideoSource(withAudio: true);
        var (workspace, _, videoTrack, audioTrack) = await OpenAsync(source);
        await new WorkingCompositionService(workspace).DeleteEmptyTrackAsync(CompositionTrackKind.Audio, audioTrack);
        var service = new CompositionPhysicalPlacementService(
            workspace,
            new Timing(ExactVideo(), UnusableAudio()),
            new MatchingHash(),
            new Decisions(new CompositionPlacementDecision(
                CompositionPlacementAction.Place,
                ApproveVideoOnlyWithoutUsableAudio: true)));

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        Assert.Single(Current(workspace).VideoTracks.Single().Items);
        Assert.Empty(Current(workspace).AudioTracks);
    }

    [Fact]
    public async Task HashMismatchBlocksBeforeAssessmentPersistenceOrCompositionMutation()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, _, videoTrack, _) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(ExactVideo()), new MismatchedHash(), new Decisions());

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Blocked, result.Status);
        Assert.Empty(source.TimingAssessments);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
    }

    [Fact]
    public async Task StandaloneExactAudioPlacesOnItsSelectedAudioTrackWithoutPrompt()
    {
        var source = AudioSource();
        var (workspace, _, _, audioTrack) = await OpenAsync(source);
        var decisions = new Decisions();
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(ExactAudio()), new MatchingHash(), decisions);

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(5, 1), audioTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        Assert.Equal(0, decisions.Calls);
        var item = Assert.Single(Current(workspace).AudioTracks.Single().Items);
        Assert.Equal(new ExactTime(5, 1), item.CompositionStart);
        Assert.Null(item.LinkGroupId);
    }

    [Fact]
    public async Task AudioRejectsVideoAppendPlacementIntentBeforeAssessment()
    {
        var source = AudioSource();
        var (workspace, store, _, audioTrack) = await OpenAsync(source);
        var timing = new Timing(ExactAudio());
        var service = new CompositionPhysicalPlacementService(workspace, timing, new MatchingHash(), new Decisions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceAsync(new(
            source.Id,
            new ExactTime(0, 1),
            audioTrack,
            Mode: CompositionPhysicalPlacementMode.AppendToVideoTrack)));

        Assert.Equal(0, timing.Calls);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CompositionSaveFailureRollsBackAcknowledgementAndOccurrenceWhileRetainingSavedAssessment()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, store, videoTrack, _) = await OpenAsync(source);
        store.FailSaveNumber = 2;
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(EstimatedVideo()), new MatchingHash(),
            new Decisions(new CompositionPlacementDecision(CompositionPlacementAction.Place, AcknowledgeEstimatedTiming: true)));

        await Assert.ThrowsAsync<IOException>(() => service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack)));

        Assert.Single(source.TimingAssessments);
        Assert.Empty(workspace.Project!.TimingAssessmentAcknowledgements);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
    }

    [Fact]
    public async Task MixedExactVideoAndEstimatedAudioCreatesLinkedItemsAfterOneAcknowledgement()
    {
        var source = VideoSource(withAudio: true);
        var (workspace, _, videoTrack, audioTrack) = await OpenAsync(source);
        var decisions = new Decisions(new CompositionPlacementDecision(CompositionPlacementAction.Place, AcknowledgeEstimatedTiming: true));
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(ExactVideo(), EstimatedAudio()), new MatchingHash(), decisions);

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack, audioTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        Assert.Equal(1, decisions.Calls);
        Assert.Single(workspace.Project!.TimingAssessmentAcknowledgements);
        var video = Assert.Single(Current(workspace).VideoTracks.Single().Items);
        var audio = Assert.Single(Current(workspace).AudioTracks.Single().Items);
        Assert.Equal(video.LinkGroupId, audio.LinkGroupId);
        Assert.NotNull(video.SourceRange);
        Assert.Null(audio.SourceRange);
    }

    [Fact]
    public async Task VideoWithoutSelectedAudioPlacesVideoOnlyWithoutPrompt()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, _, videoTrack, _) = await OpenAsync(source);
        var decisions = new Decisions();
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(ExactVideo()), new MatchingHash(), decisions);

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        Assert.Equal(0, decisions.Calls);
        Assert.Empty(Current(workspace).AudioTracks.Single().Items);
    }

    [Fact]
    public async Task StandaloneEstimatedAudioRequiresAndRetainsAcknowledgement()
    {
        var source = AudioSource();
        var (workspace, _, _, audioTrack) = await OpenAsync(source);
        var decisions = new Decisions(new CompositionPlacementDecision(CompositionPlacementAction.Place, AcknowledgeEstimatedTiming: true));
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(EstimatedAudio()), new MatchingHash(), decisions);

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), audioTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        Assert.Single(workspace.Project!.TimingAssessmentAcknowledgements);
        Assert.Equal(TimingReadiness.Estimated, Assert.Single(Current(workspace).AudioTracks.Single().Items).TimingAssessment.Readiness);
    }

    [Fact]
    public async Task LinkedStartsPreserveSourceOffsetFromEarliestRepresentableStart()
    {
        var source = VideoSource(withAudio: true);
        var (workspace, _, videoTrack, audioTrack) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(workspace, new Timing(ExactVideoAt(2000), ExactAudioAt(48000)), new MatchingHash(), new Decisions());

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(5, 1), videoTrack, audioTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        Assert.Equal(new ExactTime(6, 1), Assert.Single(Current(workspace).VideoTracks.Single().Items).CompositionStart);
        Assert.Equal(new ExactTime(5, 1), Assert.Single(Current(workspace).AudioTracks.Single().Items).CompositionStart);
    }

    [Fact]
    public async Task EstimatedStreamWithoutKnownPresentationStartAnchorsAtRequestedPlacement()
    {
        var source = VideoSource(withAudio: true);
        var (workspace, _, videoTrack, audioTrack) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(
            workspace,
            new Timing(EstimatedVideo(), ExactAudioAt(48000)),
            new MatchingHash(),
            new Decisions(new CompositionPlacementDecision(
                CompositionPlacementAction.Place,
                AcknowledgeEstimatedTiming: true)));

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(5, 1), videoTrack, audioTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Placed, result.Status);
        Assert.Equal(new ExactTime(5, 1), Assert.Single(Current(workspace).VideoTracks.Single().Items).CompositionStart);
        Assert.Equal(new ExactTime(6, 1), Assert.Single(Current(workspace).AudioTracks.Single().Items).CompositionStart);
    }

    [Fact]
    public async Task InvalidTargetTracksRejectBeforeAssessmentOrSave()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, store, videoTrack, audioTrack) = await OpenAsync(source);
        var timing = new Timing(ExactVideo());
        var service = new CompositionPhysicalPlacementService(workspace, timing, new MatchingHash(), new Decisions());

        await new WorkingCompositionService(workspace).SetTrackLockAsync(videoTrack, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceAsync(new(source.Id, new ExactTime(0, 1), audioTrack)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceAsync(new(source.Id, new ExactTime(0, 1), Guid.NewGuid())));

        Assert.Equal(0, timing.Calls);
        Assert.Equal(1, store.SaveCount); // only the explicit lock command
    }

    [Fact]
    public async Task AssessmentCancellationPersistsNothing()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, store, videoTrack, _) = await OpenAsync(source);
        using var cancellation = new CancellationTokenSource();
        var service = new CompositionPhysicalPlacementService(workspace, new CancellingTiming(cancellation), new MatchingHash(), new Decisions());

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack), cancellation.Token);

        Assert.Equal(CompositionPhysicalPlacementStatus.Cancelled, result.Status);
        Assert.Empty(source.TimingAssessments);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task SessionChangeDuringAssessmentReturnsStaleWithoutOccurrence()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, store, videoTrack, _) = await OpenAsync(source);
        var timing = new Timing(ExactVideo(), onAssess: () => workspace.OpenAsync("C:\\other\\Other.rfp"));
        var service = new CompositionPhysicalPlacementService(workspace, timing, new MatchingHash(), new Decisions());

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Stale, result.Status);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task UnknownDecisionAndIrrelevantStandaloneAudioTargetAreRefused()
    {
        var video = VideoSource(withAudio: false);
        var (videoWorkspace, _, videoTrack, _) = await OpenAsync(video);
        var unknown = new CompositionPhysicalPlacementService(videoWorkspace, new Timing(EstimatedVideo()), new MatchingHash(),
            new Decisions(new CompositionPlacementDecision((CompositionPlacementAction)999, AcknowledgeEstimatedTiming: true)));

        var decision = await unknown.PlaceAsync(new(video.Id, new ExactTime(0, 1), videoTrack));
        Assert.Equal(CompositionPhysicalPlacementStatus.Blocked, decision.Status);
        Assert.Empty(Current(videoWorkspace).VideoTracks.Single().Items);

        var audio = AudioSource();
        var (audioWorkspace, _, _, audioTrack) = await OpenAsync(audio);
        var audioTiming = new Timing(ExactAudio());
        var standalone = new CompositionPhysicalPlacementService(audioWorkspace, audioTiming, new MatchingHash(), new Decisions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => standalone.PlaceAsync(new(audio.Id, new ExactTime(0, 1), audioTrack, audioTrack)));
        Assert.Equal(0, audioTiming.Calls);
    }

    [Fact]
    public async Task SourceChangeDuringInteractiveConfirmationBlocksBeforeAcknowledgementOrOccurrence()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, _, videoTrack, _) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(
            workspace,
            new Timing(EstimatedVideo()),
            new ChangingHash(),
            new Decisions(new CompositionPlacementDecision(
                CompositionPlacementAction.Place,
                AcknowledgeEstimatedTiming: true)));

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Blocked, result.Status);
        Assert.Contains("changed", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Single(source.TimingAssessments);
        Assert.Empty(workspace.Project!.TimingAssessmentAcknowledgements);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
    }

    [Fact]
    public async Task ExactSourceChangeBeforeCommitBlocksWithoutOccurrence()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, _, videoTrack, _) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(
            workspace,
            new Timing(ExactVideo()),
            new ChangingHash(),
            new Decisions());

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Blocked, result.Status);
        Assert.Contains("changed", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Single(source.TimingAssessments);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
    }

    [Fact]
    public async Task ProjectSwitchAtFinalPrecommitVerificationReturnsStaleWithoutSavingEitherProject()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, store, videoTrack, _) = await OpenAsync(source);
        var originalProject = workspace.Project!;
        var replacementProject = new VideoProject { Name = "Other" };
        store.OtherProject = replacementProject;
        var service = new CompositionPhysicalPlacementService(
            workspace,
            new Timing(ExactVideo()),
            new SwitchingHash(workspace),
            new Decisions());

        var result = await service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack));

        Assert.Equal(CompositionPhysicalPlacementStatus.Stale, result.Status);
        Assert.Empty(Current(originalProject).VideoTracks.Single().Items);
        Assert.Empty(replacementProject.RecipeRevisions);
        Assert.Same(replacementProject, workspace.Project);
    }

    [Fact]
    public async Task SameProjectTrackLockBeforePlacementCommitIsRetainedAndRejectsOccurrence()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, store, videoTrack, _) = await OpenAsync(source);
        var service = new CompositionPhysicalPlacementService(
            workspace,
            new Timing(ExactVideo()),
            new SecondVerificationActionHash(() =>
                new WorkingCompositionService(workspace).SetTrackLockAsync(videoTrack, true)),
            new Decisions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PlaceAsync(new(source.Id, new ExactTime(0, 1), videoTrack)));

        Assert.Contains("Unlock", exception.Message, StringComparison.Ordinal);
        var track = Current(workspace).VideoTracks.Single();
        Assert.True(track.IsLocked);
        Assert.Empty(track.Items);
        Assert.Equal(2, store.SaveCount); // timing assessment, then the concurrent lock
    }

    [Fact]
    public async Task AssessmentSaveCancellationRollsBackEvidenceAndCreatesNoOccurrence()
    {
        var source = VideoSource(withAudio: false);
        var (workspace, store, videoTrack, _) = await OpenAsync(source);
        using var cancellation = new CancellationTokenSource();
        store.CancelSaveNumber = 1;
        store.SaveCancellation = cancellation;
        var service = new CompositionPhysicalPlacementService(
            workspace,
            new Timing(ExactVideo()),
            new MatchingHash(),
            new Decisions());

        var result = await service.PlaceAsync(
            new(source.Id, new ExactTime(0, 1), videoTrack),
            cancellation.Token);

        Assert.Equal(CompositionPhysicalPlacementStatus.Cancelled, result.Status);
        Assert.Empty(source.TimingAssessments);
        Assert.Empty(Current(workspace).VideoTracks.Single().Items);
    }

    private static WorkingCompositionState Current(ProjectWorkspace workspace) => Current(workspace.Project!);
    private static WorkingCompositionState Current(VideoProject project) => ((CompositionRecipe)project
        .RecipeRevisions.Single(revision => revision.Id == project.Assets.Single(asset => asset.Id == project.WorkingCompositionAssetId).Virtual!.CurrentRecipeRevisionId).Recipe).Composition;

    private static ProjectAsset VideoSource(bool withAudio) => new()
    {
        DisplayName = "Source", FileName = "Source.mp4", MediaType = MediaType.Video, StorageKind = AssetStorageKind.Physical,
        Encoding = new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata { StreamIndex = 0, TimeBaseNumerator = 1, TimeBaseDenominator = 1000 },
            Audio = withAudio ? new AudioStreamMetadata { StreamIndex = 1, SampleRate = 48000, TimeBaseNumerator = 1, TimeBaseDenominator = 48000 } : null
        },
        Physical = new PhysicalAssetStorage { RelativePath = "Media/Source.mp4", ContentIdentity = Identity(), Availability = PhysicalAssetAvailability.Available }
    };

    private static ProjectAsset AudioSource() => new()
    {
        DisplayName = "Audio", FileName = "Audio.m4a", MediaType = MediaType.Audio, StorageKind = AssetStorageKind.Physical,
        Encoding = new MediaEncodingMetadata { Audio = new AudioStreamMetadata { StreamIndex = 1, SampleRate = 48000, TimeBaseNumerator = 1, TimeBaseDenominator = 48000 } },
        Physical = new PhysicalAssetStorage { RelativePath = "Media/Audio.m4a", ContentIdentity = Identity(), Availability = PhysicalAssetAvailability.Available }
    };

    private static ContentIdentity Identity() => new() { Status = ContentHashStatus.Verified, Sha256 = new string('a', 64) };
    private static StreamTimingAssessmentResult ExactVideo() => new(Assessment(MediaType.Video, TimingReadiness.Exact, []), new VideoSourceRange(new VideoPresentationTime(0, 1, 1000), new VideoPresentationTime(1000, 1, 1000)));
    private static StreamTimingAssessmentResult ExactAudio() => new(Assessment(MediaType.Audio, TimingReadiness.Exact, []), audioFullRange: new AudioSourceRange(new AudioSampleTime(0, 48000), new AudioSampleTime(48000, 48000)));
    private static StreamTimingAssessmentResult EstimatedVideo() => new(Assessment(MediaType.Video, TimingReadiness.Estimated, [TimingIssueClassification.NativeDurationUnavailable], null));
    private static StreamTimingAssessmentResult EstimatedAudio() => new(Assessment(MediaType.Audio, TimingReadiness.Estimated, [TimingIssueClassification.NativeDurationUnavailable], null));
    private static StreamTimingAssessmentResult ExactVideoAt(long start) => new(Assessment(MediaType.Video, TimingReadiness.Exact, [], new ExactTime(start, 1000)), new VideoSourceRange(new VideoPresentationTime(start, 1, 1000), new VideoPresentationTime(start + 1000, 1, 1000)));
    private static StreamTimingAssessmentResult ExactAudioAt(long start) => new(Assessment(MediaType.Audio, TimingReadiness.Exact, [], new ExactTime(start, 48000)), audioFullRange: new AudioSourceRange(new AudioSampleTime(start, 48000), new AudioSampleTime(start + 48000, 48000)));
    private static StreamTimingAssessmentResult UnusableVideo() => new(Assessment(MediaType.Video, TimingReadiness.Unusable, [TimingIssueClassification.FiniteSpanUnavailable], null, false));
    private static StreamTimingAssessmentResult UnusableAudio() => new(Assessment(MediaType.Audio, TimingReadiness.Unusable, [TimingIssueClassification.FiniteSpanUnavailable], null, false));
    private static StreamTimingAssessment Assessment(MediaType type, TimingReadiness readiness, IEnumerable<TimingIssueClassification> issues, ExactTime? start = null, bool sequential = true) => new(
        Guid.NewGuid(), new string('a', 64), type, type == MediaType.Video ? 0 : 1, readiness, sequential,
        readiness == TimingReadiness.Unusable ? null : new ExactTime(1, 1), issues, start ?? (readiness == TimingReadiness.Exact ? new ExactTime(0, 1) : null));

    private static async Task<(ProjectWorkspace Workspace, Store Store, Guid VideoTrack, Guid AudioTrack)> OpenAsync(ProjectAsset source)
    {
        var videoTrack = Guid.NewGuid(); var audioTrack = Guid.NewGuid();
        var composition = new ProjectAsset { DisplayName = "Working Composition", FileName = "Working Composition", MediaType = MediaType.Video, StorageKind = AssetStorageKind.Virtual, Physical = null, Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition } };
        var project = new VideoProject { Assets = [source, composition], WorkingCompositionAssetId = composition.Id };
        project.CommitRecipe(composition.Id, new CompositionRecipe { Composition = new WorkingCompositionState([new CompositionVideoTrack(videoTrack, false, true, [])], [new CompositionAudioTrack(audioTrack, false, false, [])]) });
        var store = new Store(project, new ProjectLocation("C:\\placement", "C:\\placement\\Placement.rfp"));
        var workspace = new ProjectWorkspace(store, new Importer());
        await workspace.OpenAsync("C:\\placement\\Placement.rfp");
        return (workspace, store, videoTrack, audioTrack);
    }

    private sealed class Timing : IStreamTimingAssessmentService
    {
        private readonly Dictionary<MediaType, StreamTimingAssessmentResult> _results;
        private readonly Func<Task>? _onAssess;
        public Timing(StreamTimingAssessmentResult first, StreamTimingAssessmentResult? second = null, Func<Task>? onAssess = null)
        {
            _results = new[] { first, second }.Where(result => result is not null).Cast<StreamTimingAssessmentResult>().ToDictionary(result => result.Assessment.MediaType);
            _onAssess = onAssess;
        }
        public int Calls { get; private set; }
        public async Task<StreamTimingAssessmentResult> AssessAsync(StreamTimingAssessmentRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (_onAssess is not null) await _onAssess();
            return _results[request.MediaType];
        }
    }
    private sealed class CancellingTiming(CancellationTokenSource cancellation) : IStreamTimingAssessmentService
    {
        public Task<StreamTimingAssessmentResult> AssessAsync(StreamTimingAssessmentRequest request, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }
    private sealed class MatchingHash : IContentHashService
    {
        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Identity());
        public Task<ContentVerificationResult> VerifyAsync(string path, ContentIdentity expected, CancellationToken cancellationToken = default) => Task.FromResult(new ContentVerificationResult(true, expected));
    }
    private sealed class MismatchedHash : IContentHashService
    {
        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Identity());
        public Task<ContentVerificationResult> VerifyAsync(string path, ContentIdentity expected, CancellationToken cancellationToken = default) => Task.FromResult(new ContentVerificationResult(false, Identity()));
    }
    private sealed class ChangingHash : IContentHashService
    {
        private int _verificationCount;
        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Identity());
        public Task<ContentVerificationResult> VerifyAsync(string path, ContentIdentity expected, CancellationToken cancellationToken = default)
        {
            var matches = ++_verificationCount == 1;
            return Task.FromResult(new ContentVerificationResult(matches, matches ? expected : Identity()));
        }
    }
    private sealed class SwitchingHash(ProjectWorkspace workspace) : IContentHashService
    {
        private int _verificationCount;
        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Identity());
        public async Task<ContentVerificationResult> VerifyAsync(string path, ContentIdentity expected, CancellationToken cancellationToken = default)
        {
            if (++_verificationCount == 2)
                await workspace.OpenAsync("C:\\other\\Other.rfp", cancellationToken);
            return new ContentVerificationResult(true, expected);
        }
    }
    private sealed class SecondVerificationActionHash(Func<Task> action) : IContentHashService
    {
        private int _verificationCount;
        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Identity());
        public async Task<ContentVerificationResult> VerifyAsync(string path, ContentIdentity expected, CancellationToken cancellationToken = default)
        {
            if (++_verificationCount == 2)
                await action();
            return new ContentVerificationResult(true, expected);
        }
    }
    private sealed class Decisions(params CompositionPlacementDecision[] decisions) : ICompositionPlacementDecisionProvider
    {
        private readonly Queue<CompositionPlacementDecision> _decisions = new(decisions);
        public int Calls { get; private set; }
        public Task<CompositionPlacementDecision> DecideAsync(CompositionPlacementDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_decisions.Count == 0 ? new CompositionPlacementDecision(CompositionPlacementAction.Cancel) : _decisions.Dequeue());
        }
    }
    private sealed class Store(VideoProject project, ProjectLocation location) : IProjectStore, IProjectCommitGuardedStore
    {
        public int SaveCount { get; private set; }
        public int? FailSaveNumber { get; set; }
        public int? CancelSaveNumber { get; set; }
        public CancellationTokenSource? SaveCancellation { get; set; }
        public VideoProject? OtherProject { get; set; }
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) => Task.FromResult(projectFilePath.Contains("Other", StringComparison.Ordinal)
            ? (OtherProject ?? project, new ProjectLocation("C:\\other", "C:\\other\\Other.rfp"))
            : (project, location));
        public Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default)
            => SaveCoreAsync(project, location, _ => true, cancellationToken);
        public Task<bool> SaveIfAsync(VideoProject project, ProjectLocation location, Func<Action, bool> tryCommit, CancellationToken cancellationToken = default)
            => SaveCoreAsync(project, location, tryCommit, cancellationToken);
        private Task<bool> SaveCoreAsync(VideoProject savedProject, ProjectLocation savedLocation, Func<Action, bool> tryCommit, CancellationToken cancellationToken)
        {
            SaveCount++;
            if (SaveCount == CancelSaveNumber)
            {
                SaveCancellation?.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
            if (SaveCount == FailSaveNumber) throw new IOException("Injected project save failure.");
            return Task.FromResult(tryCommit(() => { }));
        }
    }
    private sealed class Importer : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, IEnumerable<string> reservedRelativePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
