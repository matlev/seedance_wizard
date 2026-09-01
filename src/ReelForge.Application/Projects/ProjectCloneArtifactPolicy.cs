using System.Text.RegularExpressions;

namespace ReelForge.Application;

/// <summary>
/// Identifies unpublished clone artifacts so every project-selection path can keep them
/// outside the user-visible project namespace after an interrupted process.
/// </summary>
public static partial class ProjectCloneArtifactPolicy
{
    public static string CreateStagingDirectoryName(string cloneName) =>
        $".{cloneName}.clone-{Guid.NewGuid():N}";

    public static bool IsStagingDirectoryName(string directoryName) =>
        StagingDirectoryNamePattern().IsMatch(directoryName);

    public static bool IsStagingProjectFile(string projectFilePath)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(projectFilePath));
        return parent is not null && IsStagingDirectoryName(Path.GetFileName(parent));
    }

    [GeneratedRegex(@"^\..+\.clone-[0-9a-f]{32}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StagingDirectoryNamePattern();
}
