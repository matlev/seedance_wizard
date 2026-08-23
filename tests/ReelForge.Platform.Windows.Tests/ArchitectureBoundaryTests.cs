using ReelForge.Platform.Windows;

namespace ReelForge.Platform.Windows.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void PlatformWindowsDirectlyReferencesOnlyApplication()
    {
        var references = typeof(WindowsApplicationPathProvider).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("ReelForge.", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ReelForge.Application"], references);
    }
}
