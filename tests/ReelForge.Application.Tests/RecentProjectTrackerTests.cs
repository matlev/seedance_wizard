using ReelForge.Application;

namespace ReelForge.Application.Tests;

public sealed class RecentProjectTrackerTests
{
    [Fact]
    public async Task AddRecentPreservesLastProjectAndDeduplicatesNewestEntry()
    {
        var settings = new ApplicationSettings();
        settings.General.LastProjectFilePath = Path.GetFullPath("active.rfp");
        settings.General.RecentProjectFilePaths.Add(Path.GetFullPath("other.rfp"));
        var store = new RecordingSettingsStore();
        var tracker = new RecentProjectTracker(store);

        await tracker.AddRecentAsync(settings, "other.rfp");

        Assert.Equal(Path.GetFullPath("active.rfp"), settings.General.LastProjectFilePath);
        Assert.Equal(Path.GetFullPath("other.rfp"), settings.General.RecentProjectFilePaths[0]);
        Assert.Single(settings.General.RecentProjectFilePaths);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task AddRecentCapsListAtTwelveEntries()
    {
        var settings = new ApplicationSettings();
        settings.General.LastProjectFilePath = Path.GetFullPath("active.rfp");
        for (var i = 0; i < 12; i++)
            settings.General.RecentProjectFilePaths.Add(Path.GetFullPath($"project-{i}.rfp"));
        var tracker = new RecentProjectTracker(new RecordingSettingsStore());

        await tracker.AddRecentAsync(settings, "new-project.rfp");

        Assert.Equal(12, settings.General.RecentProjectFilePaths.Count);
        Assert.Equal(Path.GetFullPath("new-project.rfp"), settings.General.RecentProjectFilePaths[0]);
        Assert.DoesNotContain(Path.GetFullPath("project-11.rfp"), settings.General.RecentProjectFilePaths);
        Assert.Equal(Path.GetFullPath("active.rfp"), settings.General.LastProjectFilePath);
    }

    private sealed class RecordingSettingsStore : IApplicationSettingsStore
    {
        public string LocalSettingsPath => "settings.json";
        public int SaveCount { get; private set; }
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationSettings());
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
