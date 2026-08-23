namespace ReelForge.Application;

/// <summary>
/// Resolves paths owned by a project without allowing rooted or parent-relative paths
/// to escape the project directory.
/// </summary>
public static class ProjectPathPolicy
{
    public static string ResolveContainedPath(ProjectLocation location, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(location);
        return ResolveContainedPath(location.RootDirectory, relativePath);
    }

    public static string ResolveContainedPath(string rootDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("A project-relative path cannot be rooted.");

        var root = Path.GetFullPath(rootDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        EnsureContained(root, candidate);
        return candidate;
    }

    public static bool TryResolveContainedPath(
        ProjectLocation location,
        string relativePath,
        out string resolvedPath)
    {
        ArgumentNullException.ThrowIfNull(location);

        try
        {
            resolvedPath = ResolveContainedPath(location, relativePath);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or NotSupportedException)
        {
            resolvedPath = string.Empty;
            return false;
        }
    }

    public static string GetRelativePath(ProjectLocation location, string containedPath)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(containedPath);

        var root = Path.GetFullPath(location.RootDirectory);
        var candidate = Path.GetFullPath(containedPath);
        EnsureContained(root, candidate);
        return Path.GetRelativePath(root, candidate)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void EnsureContained(string rootDirectory, string candidatePath)
    {
        var relative = Path.GetRelativePath(rootDirectory, candidatePath);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The project path escapes the project directory.");
        }
    }
}
