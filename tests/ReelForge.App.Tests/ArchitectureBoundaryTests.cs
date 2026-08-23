using ReelForge.App;

namespace ReelForge.App.Tests;

public sealed class ArchitectureBoundaryTests
{
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
}
