using ReelForge.Platform.Windows;

namespace ReelForge.Platform.Windows.Tests;

public sealed class WindowsPlatformIntegrationTests
{
    [Fact]
    public void ApplicationPathsUseWindowsKnownFoldersAndOneApplicationRoot()
    {
        var paths = new WindowsApplicationPathProvider().GetPaths();
        var localApplicationData = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var applicationRoot = Path.Combine(localApplicationData, "ReelForge");

        Assert.Equal(Path.Combine(applicationRoot, "appsettings.local.json"), paths.LocalSettingsFilePath);
        Assert.Equal(Path.Combine(applicationRoot, "active-jobs.json"), paths.ActiveGenerationJobsFilePath);
        Assert.Equal(Path.Combine(applicationRoot, "Cache"), paths.MediaCacheDirectory);
        Assert.Equal(Path.Combine(applicationRoot, "Logs"), paths.DefaultLogDirectory);
        Assert.Equal(Path.Combine("ReelForge", "Projects"),
            Path.GetRelativePath(Path.GetDirectoryName(Path.GetDirectoryName(paths.DefaultProjectsDirectory))!,
                paths.DefaultProjectsDirectory));
    }

    [Fact]
    public void CredentialStoreExposesPlatformMetadataWithoutReadingASecret()
    {
        var store = new WindowsCredentialStore("ReelForge-Test");

        Assert.Equal("Windows Credential Manager", store.DisplayName);
        Assert.Equal("ReelForge-Test:provider.api-key", store.GetDisplayKey("provider.api-key"));
    }

    [Fact]
    public void PlatformProjectOnlyReferencesApplicationAmongReelForgeAssemblies()
    {
        var references = typeof(WindowsApplicationPathProvider).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("ReelForge.", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ReelForge.Application"], references);
    }
}
