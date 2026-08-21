using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

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
}
