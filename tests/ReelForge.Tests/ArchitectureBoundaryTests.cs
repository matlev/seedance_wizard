using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;
using System.Text.RegularExpressions;

namespace ReelForge.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void CoreDoesNotReferenceAnotherReelForgeLayer()
    {
        AssertReelForgeReferences(
            typeof(VideoProject).Assembly,
            []);
    }

    [Fact]
    public void ApplicationOnlyReferencesCore()
    {
        AssertReelForgeReferences(
            typeof(ProjectWorkspace).Assembly,
            ["ReelForge.Core"]);
        AssertDoesNotReferenceDesktopPresentation(typeof(ProjectWorkspace).Assembly);
    }

    [Fact]
    public void InfrastructureOnlyReferencesApplicationAndCore()
    {
        AssertReelForgeReferences(
            typeof(PortableProjectStore).Assembly,
            ["ReelForge.Application", "ReelForge.Core"]);
        AssertDoesNotReferenceDesktopPresentation(typeof(PortableProjectStore).Assembly);
    }

    [Fact]
    public void PortableProductionSourcesDoNotUseDesktopOrWindowsSpecificApis()
    {
        var forbiddenPatterns = new Dictionary<string, Regex>
        {
            ["System.Windows desktop namespace imports"] = UsingDirectivePattern("System\\.Windows"),
            ["Microsoft.Win32 API imports"] = UsingDirectivePattern("Microsoft\\.Win32"),
            ["DllImport"] = NativeImportAttributePattern("DllImport"),
            ["LibraryImport"] = NativeImportAttributePattern("LibraryImport"),
            ["ReelForge.App imports"] = UsingDirectivePattern("ReelForge\\.App"),
            ["ReelForge.Platform.Windows imports"] = UsingDirectivePattern("ReelForge\\.Platform\\.Windows")
        };

        foreach (var sourceFile in EnumeratePortableProductionSourceFiles())
        {
            var source = File.ReadAllText(sourceFile);
            foreach (var (description, pattern) in forbiddenPatterns)
            {
                Assert.False(
                    pattern.IsMatch(source),
                    $"Portable source '{GetRepositoryRelativePath(sourceFile)}' must not use {description}.");
            }
        }
    }

    private static void AssertReelForgeReferences(
        System.Reflection.Assembly assembly,
        IReadOnlyCollection<string> expected)
    {
        var actual = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("ReelForge.", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    private static void AssertDoesNotReferenceDesktopPresentation(System.Reflection.Assembly assembly)
    {
        var references = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("PresentationCore", references);
        Assert.DoesNotContain("PresentationFramework", references);
        Assert.DoesNotContain("System.Xaml", references);
        Assert.DoesNotContain("WindowsBase", references);
    }

    private static Regex UsingDirectivePattern(string namespacePattern) => new(
        $@"(?m)^\s*(?:global\s+)?using\s+(?:static\s+)?(?:[A-Za-z_]\w*\s*=\s*)?{namespacePattern}(?:\.|\s*;)",
        RegexOptions.CultureInvariant);

    private static Regex NativeImportAttributePattern(string attributeName) => new(
        $@"(?m)^\s*\[\s*(?:global::)?(?:System\.Runtime\.InteropServices\.)?{attributeName}(?:Attribute)?\s*\(",
        RegexOptions.CultureInvariant);

    private static IEnumerable<string> EnumeratePortableProductionSourceFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var portableProjects = new[]
        {
            "ReelForge.Core",
            "ReelForge.Application",
            "ReelForge.Infrastructure"
        };

        return portableProjects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src", project),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(IsProductionSourceFile);
    }

    private static string GetRepositoryRelativePath(string path) =>
        Path.GetRelativePath(FindRepositoryRoot(), path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsProductionSourceFile(string path) =>
        !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("generated", StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ReelForge.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the ReelForge repository root from the test output directory.");
    }
}
