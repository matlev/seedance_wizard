using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class ProjectFileLocatorTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge project locator tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void FindsRfpFilesAtRootAndOneLevelBelowButNotDeeper()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var child = Directory.CreateDirectory(Path.Combine(_temporaryRoot, "Child")).FullName;
        var grandchild = Directory.CreateDirectory(Path.Combine(child, "Grandchild")).FullName;
        var rootProject = Path.Combine(_temporaryRoot, "Root.rfp");
        var childProject = Path.Combine(child, "Child.rfp");
        var tooDeepProject = Path.Combine(grandchild, "Too deep.rfp");
        File.WriteAllText(rootProject, "{}");
        File.WriteAllText(childProject, "{}");
        File.WriteAllText(tooDeepProject, "{}");

        var results = ProjectFileLocator.FindInFolderAndChildren(_temporaryRoot);

        Assert.Equal(2, results.Count);
        Assert.Contains(Path.GetFullPath(rootProject), results);
        Assert.Contains(Path.GetFullPath(childProject), results);
        Assert.DoesNotContain(Path.GetFullPath(tooDeepProject), results);
    }

    [Fact]
    public void IgnoresNonRfpFiles()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var jsonFile = Path.Combine(_temporaryRoot, "project.json");
        File.WriteAllText(jsonFile, "{}");

        var result = ProjectFileLocator.FindInFolderAndChildren(_temporaryRoot);

        Assert.Empty(result);
        Assert.False(ProjectFileLocator.IsSupportedProjectFile(jsonFile));
    }

    [Fact]
    public void IgnoresUnpublishedCloneStagingProjects()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var staging = Directory.CreateDirectory(Path.Combine(
            _temporaryRoot,
            ".Interrupted copy.clone-0123456789abcdef0123456789abcdef")).FullName;
        File.WriteAllText(Path.Combine(staging, "Interrupted copy.rfp"), "{}");

        var result = ProjectFileLocator.FindInFolderAndChildren(_temporaryRoot);

        Assert.Empty(result);
        Assert.False(ProjectFileLocator.IsSupportedProjectFile(Path.Combine(staging, "Interrupted copy.rfp")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }
}
