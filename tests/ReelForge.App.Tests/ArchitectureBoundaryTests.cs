using ReelForge.App;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ReelForge.App.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] InfrastructureAndPlatformProjects =
    [
        "ReelForge.Infrastructure",
        "ReelForge.Platform.Windows"
    ];

    [Fact]
    public void AppDirectlyReferencesOnlyApprovedReelForgeLayers()
    {
        var references = typeof(MainWindow).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("ReelForge.", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "ReelForge.Application",
                "ReelForge.Core",
                "ReelForge.Infrastructure",
                "ReelForge.Platform.Windows"
            ],
            references);
    }

    [Fact]
    public void InfrastructureReferencesOutsideBootstrapMatchArchDebt001Allowlist()
    {
        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/ReelForge.App/MainWindow.xaml.cs",
            "src/ReelForge.App/Views/Dialogs/AssetNameDialog.xaml.cs",
            "src/ReelForge.App/Views/Dialogs/OpenProjectDialog.xaml.cs",
            "src/ReelForge.App/Views/Editing/CompositionAuditionController.cs",
            "src/ReelForge.App/Views/Editing/CompositionRenderCoordinator.cs",
            "src/ReelForge.App/Views/Editing/CompositionWorkspaceCoordinator.cs",
            "src/ReelForge.App/Views/Generation/GenerationContinuationCoordinator.cs",
            "src/ReelForge.App/Views/Generation/GenerationWorkspaceCoordinator.cs",
            "src/ReelForge.App/Views/MediaPreparation/FramePreparationCoordinator.cs",
            "src/ReelForge.App/Views/MediaPreview/MediaPreviewCoordinator.cs",
            "src/ReelForge.App/Views/ProjectMedia/MediaImportCoordinator.cs",
            "src/ReelForge.App/Views/ProjectMedia/ProjectMediaOperationsCoordinator.cs",
            "src/ReelForge.App/Views/Settings/SettingsWindow.xaml.cs"
        };

        var actualFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceFile in EnumerateAppSourceFiles())
        {
            var relativePath = GetRepositoryRelativePath(sourceFile);
            if (relativePath == "src/ReelForge.App/Bootstrap/ApplicationRuntime.cs") continue;

            var source = File.ReadAllText(sourceFile);
            if (!NamespaceReferencePattern("ReelForge\\.Infrastructure").IsMatch(source)) continue;
            actualFiles.Add(relativePath);
        }

        Assert.Equal(allowedFiles.Order(StringComparer.Ordinal), actualFiles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PlatformWindowsReferencesRemainInBootstrap()
    {
        foreach (var sourceFile in EnumerateAppSourceFiles())
        {
            var relativePath = GetRepositoryRelativePath(sourceFile);
            if (relativePath == "src/ReelForge.App/Bootstrap/ApplicationRuntime.cs") continue;

            Assert.False(
                NamespaceReferencePattern("ReelForge\\.Platform\\.Windows").IsMatch(File.ReadAllText(sourceFile)),
                $"Platform.Windows reference in '{relativePath}' bypasses Bootstrap/ApplicationRuntime.");
        }
    }

    [Fact]
    public void PresentationDoesNotUseCompositionRootBypasses()
    {
        var bypassPattern = new Regex(
            @"(?m)^\s*(?:global\s+)?using\s+[A-Za-z_]\w*\s*=\s*(?:global::)?ReelForge\.(?:Infrastructure|Platform\.Windows)\."
            + @"|\b(?:Activator|Assembly)\s*\.\s*CreateInstance\s*\(|\.\s*Get(?:Required)?Service\s*[<(]",
            RegexOptions.CultureInvariant);

        foreach (var sourceFile in EnumerateAppSourceFiles())
        {
            var relativePath = GetRepositoryRelativePath(sourceFile);
            if (relativePath == "src/ReelForge.App/Bootstrap/ApplicationRuntime.cs") continue;

            Assert.False(
                bypassPattern.IsMatch(File.ReadAllText(sourceFile)),
                $"A concrete-type alias, dynamic construction, or service location in '{relativePath}' bypasses Bootstrap/ApplicationRuntime.");
        }
    }

    [Fact]
    public void ConcreteInfrastructureAndPlatformConstructionOutsideBootstrapMatchesArchDebt002Allowlist()
    {
        var allowedConstructions = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/ReelForge.App/MainWindow.xaml.cs :: PhysicalAssetSelectionPreparationService",
            "src/ReelForge.App/Views/Editing/CompositionWorkspaceCoordinator.cs :: Sha256ContentHashService",
            "src/ReelForge.App/Views/Generation/GenerationWorkspaceCoordinator.cs :: FakeVideoGenerationProvider"
        };

        var actualConstructions = new HashSet<string>(StringComparer.Ordinal);
        var concreteTypeNames = GetConcreteInfrastructureAndPlatformTypeNames();
        foreach (var sourceFile in EnumerateAppSourceFiles())
        {
            var relativePath = GetRepositoryRelativePath(sourceFile);
            if (relativePath == "src/ReelForge.App/Bootstrap/ApplicationRuntime.cs") continue;

            var source = File.ReadAllText(sourceFile);
            foreach (var typeName in concreteTypeNames)
            {
                var construction = new Regex(
                    $@"\bnew\s+(?:(?:global::)?ReelForge\.(?:Infrastructure|Platform\.Windows)\.)?{Regex.Escape(typeName)}\s*(?:<|\(|\{{)",
                    RegexOptions.CultureInvariant);
                if (!construction.IsMatch(source)) continue;
                actualConstructions.Add($"{relativePath} :: {typeName}");
            }
        }

        Assert.Equal(
            allowedConstructions.Order(StringComparer.Ordinal),
            actualConstructions.Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> EnumerateAppSourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src", "ReelForge.App"), "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionSourceFile);

    private static HashSet<string> GetConcreteInfrastructureAndPlatformTypeNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var declaration = new Regex(
            @"(?m)^\s*(?:public|internal)\s+(?:(?:sealed|abstract|static|partial)\s+)*(?:class|struct|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_]\w*)",
            RegexOptions.CultureInvariant);

        return InfrastructureAndPlatformProjects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src", project),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(IsProductionSourceFile))
            .SelectMany(path => declaration.Matches(File.ReadAllText(path)).Select(match => match.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Regex NamespaceReferencePattern(string namespacePattern) => new(
        $@"(?m)(?:^\s*(?:global\s+)?using\s+(?:static\s+)?(?:[A-Za-z_]\w*\s*=\s*)?|\b(?:global::)?){namespacePattern}(?:\.|\s*;)",
        RegexOptions.CultureInvariant);

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
