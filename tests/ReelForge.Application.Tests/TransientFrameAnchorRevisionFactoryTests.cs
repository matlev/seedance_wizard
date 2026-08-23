using ReelForge.Application;

namespace ReelForge.Tests;

public sealed class TransientFrameAnchorRevisionFactoryTests
{
    private static readonly Guid SourceAssetId = Guid.Parse("2673fdba-e746-4d12-b938-9c4b3904a1e8");

    [Fact]
    public void SameInputsProduceSameIdentity()
    {
        var frame = new VideoPresentationFrame(2, 1_001, 1, 24_000, 42);

        var first = TransientFrameAnchorRevisionFactory.Create(SourceAssetId, "sha256:content", frame);
        var second = TransientFrameAnchorRevisionFactory.Create(SourceAssetId, "sha256:content", frame);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(Guid.Parse("3235ffef-cd52-ca73-5755-7d37d53a3f20"), first.Id);
    }

    [Theory]
    [InlineData("sha256:other", 2, 1_001, 1, 24_000)]
    [InlineData("sha256:content", 3, 1_001, 1, 24_000)]
    [InlineData("sha256:content", 2, 1_002, 1, 24_000)]
    [InlineData("sha256:content", 2, 1_001, 2, 24_000)]
    [InlineData("sha256:content", 2, 1_001, 1, 48_000)]
    public void ExactPositionIdentityChangesProduceDifferentIdentity(
        string contentHash,
        int streamIndex,
        long presentationTimestamp,
        int timeBaseNumerator,
        int timeBaseDenominator)
    {
        var baseline = TransientFrameAnchorRevisionFactory.Create(
            SourceAssetId,
            "sha256:content",
            new VideoPresentationFrame(2, 1_001, 1, 24_000, 42));

        var changed = TransientFrameAnchorRevisionFactory.Create(
            SourceAssetId,
            contentHash,
            new VideoPresentationFrame(
                streamIndex,
                presentationTimestamp,
                timeBaseNumerator,
                timeBaseDenominator,
                42));

        Assert.NotEqual(baseline.Id, changed.Id);
    }

    [Fact]
    public void RetainsTransientSourceAndExactFrameFields()
    {
        var frame = new VideoPresentationFrame(4, 90_091, 1001, 60_000, 1_501);

        var revision = TransientFrameAnchorRevisionFactory.Create(SourceAssetId, "sha256:exact", frame);

        Assert.Equal(Guid.Empty, revision.AnchorId);
        Assert.Equal(0, revision.RevisionNumber);
        Assert.Null(revision.PreviousRevisionId);
        Assert.Equal(SourceAssetId, revision.SourceAssetId);
        Assert.Null(revision.SourceRecipeRevisionId);
        Assert.Equal("sha256:exact", revision.SourceContentHash);
        Assert.Equal(4, revision.VideoStreamIndex);
        Assert.Equal(90_091, revision.PresentationTimestamp);
        Assert.Equal(1001, revision.TimeBaseNumerator);
        Assert.Equal(60_000, revision.TimeBaseDenominator);
        Assert.Equal(1_501, revision.FrameNumber);
    }
}
