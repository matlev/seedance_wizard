using ReelForge.Application;

namespace ReelForge.Platform.Windows;

public sealed class WindowsApplicationPathProvider : IApplicationPathProvider
{
    public ApplicationPaths GetPaths()
    {
        var localApplicationData = RequireFolder(
            Environment.SpecialFolder.LocalApplicationData,
            "local application data");
        var applicationDataDirectory = Path.Combine(localApplicationData, "ReelForge");

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            var userProfile = RequireFolder(Environment.SpecialFolder.UserProfile, "user profile");
            documents = Path.Combine(userProfile, "Documents");
        }

        return new ApplicationPaths(
            Path.Combine(applicationDataDirectory, "appsettings.local.json"),
            Path.Combine(applicationDataDirectory, "active-jobs.json"),
            Path.Combine(applicationDataDirectory, "Cache"),
            Path.Combine(applicationDataDirectory, "Logs"),
            Path.Combine(documents, "ReelForge", "Projects"));
    }

    private static string RequireFolder(Environment.SpecialFolder folder, string displayName)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(path)
            ? throw new InvalidOperationException($"Windows did not provide a {displayName} folder.")
            : Path.GetFullPath(path);
    }
}
