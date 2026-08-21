namespace ReelForge.Application;

public sealed class RecentProjectTracker
{
    private const int MaximumRecentProjects = 12;
    private readonly IApplicationSettingsStore _settingsStore;

    public RecentProjectTracker(IApplicationSettingsStore settingsStore) => _settingsStore = settingsStore;

    public async Task RememberAsync(
        ApplicationSettings settings,
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        var fullPath = Path.GetFullPath(projectFilePath);
        settings.General.LastProjectFilePath = fullPath;
        settings.General.RecentProjectFilePaths.RemoveAll(path => PathsEqual(path, fullPath));
        settings.General.RecentProjectFilePaths.Insert(0, fullPath);
        if (settings.General.RecentProjectFilePaths.Count > MaximumRecentProjects)
        {
            settings.General.RecentProjectFilePaths.RemoveRange(
                MaximumRecentProjects,
                settings.General.RecentProjectFilePaths.Count - MaximumRecentProjects);
        }
        await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<string> GetExistingRecentProjectFiles(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.General.RecentProjectFilePaths
            .Prepend(settings.General.LastProjectFilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(TryGetFullPath)
            .Where(path => path is not null && File.Exists(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumRecentProjects)
            .ToArray();
    }

    public static string? GetExistingProjectFile(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var configuredPath = settings.General.LastProjectFilePath;
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;

        var fullPath = TryGetFullPath(configuredPath);
        return fullPath is not null && File.Exists(fullPath) ? fullPath : null;
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var normalizedLeft = TryGetFullPath(left);
        return normalizedLeft is not null && normalizedLeft.Equals(right, StringComparison.OrdinalIgnoreCase);
    }
}
