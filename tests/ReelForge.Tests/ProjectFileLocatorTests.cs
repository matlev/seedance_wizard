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
    public void IncludesLegacyProjectJsonForBackwardCompatibility()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var legacyProject = Path.Combine(_temporaryRoot, PortableProjectStore.LegacyProjectFileName);
        File.WriteAllText(legacyProject, "{}");

        var result = Assert.Single(ProjectFileLocator.FindInFolderAndChildren(_temporaryRoot));

        Assert.Equal(Path.GetFullPath(legacyProject), result);
        Assert.True(ProjectFileLocator.IsSupportedProjectFile(result));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }
}
