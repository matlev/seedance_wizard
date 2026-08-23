using ReelForge.App.Views.ProjectMedia;

namespace ReelForge.App.Tests;

public sealed class MediaImportInputTests
{
    [Fact]
    public void FromDialogSelectionPreservesEverySelectedPathWithoutFiltering()
    {
        var selected = new[] { "C:\\media\\clip.mp4", "C:\\media\\notes.txt" };

        var input = MediaImportInput.FromDialogSelection(selected);

        Assert.Equal(selected, input.FilePaths);
        Assert.Equal(0, input.SkippedCount);
    }

    [Fact]
    public void AnalyzeExternalDropFiltersUnsupportedMediaAndCountsSkippedCandidates()
    {
        var input = MediaImportInput.AnalyzeExternalDrop([
            "C:\\media\\clip.MP4",
            "C:\\media\\portrait.png",
            "C:\\media\\sound.wav",
            "C:\\media\\notes.txt",
            "C:\\media\\folder"
        ]);

        Assert.Equal([
            "C:\\media\\clip.MP4",
            "C:\\media\\portrait.png",
            "C:\\media\\sound.wav"
        ], input.FilePaths);
        Assert.Equal(2, input.SkippedCount);
    }
}
