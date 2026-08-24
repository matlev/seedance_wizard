using ReelForge.Application;

namespace ReelForge.Application.Tests;

public sealed class MediaRuntimeContractTests
{
    [Fact]
    public void AssessmentReportsOnlyOperationalProfileMatching()
    {
        var assessment = new MediaRuntimeProfileAssessment("P2.Test", []);

        Assert.True(assessment.MatchesProfile);
        Assert.Empty(assessment.Issues);
    }

    [Fact]
    public void ComponentRequirementsRemainConcreteRuntimeEvidence()
    {
        var requirement = new MediaRuntimeComponentRequirement(
            MediaRuntimeComponentKind.Encoder,
            ["libvpx-vp9"]);

        Assert.Equal(MediaRuntimeComponentKind.Encoder, requirement.Kind);
        Assert.Equal("libvpx-vp9", Assert.Single(requirement.Names));
    }
}
