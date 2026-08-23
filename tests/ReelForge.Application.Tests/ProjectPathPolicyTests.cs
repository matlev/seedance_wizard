using ReelForge.Application;

namespace ReelForge.Tests;

public sealed class ProjectPathPolicyTests
{
    [Fact]
    public void ResolvesProjectRelativePathAndStoresPortableSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge path policy", Guid.NewGuid().ToString("N"));
        var location = new ProjectLocation(root, Path.Combine(root, "project.rfp"));
        var resolved = ProjectPathPolicy.ResolveContainedPath(location, "assets/videos/clip.mp4");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "assets", "videos", "clip.mp4")),
            resolved);
        Assert.Equal("assets/videos/clip.mp4", ProjectPathPolicy.GetRelativePath(location, resolved));
    }

    [Fact]
    public void RejectsParentPathThatTargetsSiblingWithSharedNamePrefix()
    {
        var parent = Path.Combine(Path.GetTempPath(), "ReelForge path policy", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "Project");
        var location = new ProjectLocation(root, Path.Combine(root, "project.rfp"));

        var exception = Assert.Throws<InvalidDataException>(() =>
            ProjectPathPolicy.ResolveContainedPath(location, "../Project Other/clip.mp4"));

        Assert.Contains("escapes", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsRootedPathAndTryResolveReturnsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge path policy", Guid.NewGuid().ToString("N"));
        var location = new ProjectLocation(root, Path.Combine(root, "project.rfp"));
        var rooted = Path.Combine(Path.GetPathRoot(root)!, "outside.mp4");

        Assert.Throws<InvalidDataException>(() =>
            ProjectPathPolicy.ResolveContainedPath(location, rooted));
        Assert.False(ProjectPathPolicy.TryResolveContainedPath(location, rooted, out var resolved));
        Assert.Empty(resolved);
    }
}
