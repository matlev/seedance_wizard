using ReelForge.Application;

namespace ReelForge.Tests;

public sealed class MediaFilePolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReelForge media file policies",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void RequiredExtensionPreservesTrimmedFileNameAndRejectsTypeChange()
    {
        Assert.Equal(
            "clip.mp4",
            MediaFileNamePolicy.ValidateRequiredExtension(
                " clip.mp4 ", ".mp4", "Rendered composition assets", "value"));

        var exception = Assert.Throws<ArgumentException>(() =>
            MediaFileNamePolicy.ValidateRequiredExtension(
                "clip.mov", ".mp4", "Rendered composition assets", "value"));
        Assert.Contains("must keep the .mp4 file type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsFolderPathInsteadOfDiscardingIt()
    {
        Assert.Throws<ArgumentException>(() =>
            MediaFileNamePolicy.ValidateLeafFileName("folder/clip.mp4", "value"));
    }

    [Theory]
    [InlineData(FileNameCollisionStyle.Parenthesized, "clip (3).mp4")]
    [InlineData(FileNameCollisionStyle.Hyphenated, "clip-3.mp4")]
    public void AllocatesExistingCollisionStylesWithoutChangingVisibleNames(
        FileNameCollisionStyle style,
        string expected)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "clip.mp4"), string.Empty);
        File.WriteAllText(
            Path.Combine(_root, style == FileNameCollisionStyle.Parenthesized ? "clip (2).mp4" : "clip-2.mp4"),
            string.Empty);

        var path = CollisionFreeDestinationPolicy.GetAvailablePath(_root, "clip.mp4", style);

        Assert.Equal(expected, Path.GetFileName(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
