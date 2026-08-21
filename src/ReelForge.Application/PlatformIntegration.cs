namespace ReelForge.Application;

public sealed record ApplicationPaths(
    string LocalSettingsFilePath,
    string ActiveGenerationJobsFilePath,
    string MediaCacheDirectory,
    string DefaultLogDirectory,
    string DefaultProjectsDirectory);

public interface IApplicationPathProvider
{
    ApplicationPaths GetPaths();
}

public static class ApplicationPathResolver
{
    public static string ResolveDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}

public static class ApplicationSettingsPlatformDefaults
{
    public static void Apply(ApplicationSettings settings, ApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);

        settings.General.LogDirectory = ApplicationPathResolver.ResolveDirectory(
            string.IsNullOrWhiteSpace(settings.General.LogDirectory)
                ? paths.DefaultLogDirectory
                : settings.General.LogDirectory);
        settings.General.ProjectsRoot = ApplicationPathResolver.ResolveDirectory(
            string.IsNullOrWhiteSpace(settings.General.ProjectsRoot)
                ? paths.DefaultProjectsDirectory
                : settings.General.ProjectsRoot);
    }
}
