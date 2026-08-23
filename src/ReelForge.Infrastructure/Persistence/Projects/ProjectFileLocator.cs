namespace ReelForge.Infrastructure;

public static class ProjectFileLocator
{
    public static IReadOnlyList<string> FindInFolderAndChildren(string selectedFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedFolder);
        var root = Path.GetFullPath(selectedFolder);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"The selected folder does not exist: '{root}'.");

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddProjectFiles(root, candidates);
        foreach (var childDirectory in Directory.EnumerateDirectories(root))
            AddProjectFiles(childDirectory, candidates);

        return candidates.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsSupportedProjectFile(string path)
        => Path.GetExtension(path).Equals(PortableProjectStore.ProjectFileExtension, StringComparison.OrdinalIgnoreCase);

    private static void AddProjectFiles(string directory, HashSet<string> candidates)
    {
        foreach (var projectFile in Directory.EnumerateFiles(
                     directory,
                     $"*{PortableProjectStore.ProjectFileExtension}",
                     SearchOption.TopDirectoryOnly))
        {
            candidates.Add(Path.GetFullPath(projectFile));
        }
    }
}
