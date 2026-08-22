using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.Jobs;

public sealed class GenerationJobCancelRequestedEventArgs(Guid generationId) : EventArgs
{
    public Guid GenerationId { get; } = generationId;
}

public sealed class GenerationJobsPanelErrorEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public partial class GenerationJobsPanel : UserControl, IDisposable
{
    private readonly ObservableCollection<GenerationJobListItem> _jobs = [];
    private readonly HashSet<Guid> _viewedTerminalJobIds = [];
    private readonly DispatcherTimer _elapsedTimer;
    private GenerationJobCoordinator? _coordinator;
    private bool _dismissingViewedJobs;
    private bool _disposed;

    public GenerationJobsPanel()
    {
        InitializeComponent();
        JobsList.ItemsSource = _jobs;
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => RefreshElapsedTimes();
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    public event EventHandler? Closed;
    public event EventHandler<GenerationJobCancelRequestedEventArgs>? CancelRequested;
    public event EventHandler<GenerationJobsPanelErrorEventArgs>? ErrorOccurred;

    public void Initialize(GenerationJobCoordinator coordinator)
    {
        if (_coordinator is not null) throw new InvalidOperationException("The jobs panel is already initialized.");
        _coordinator = coordinator;
        _coordinator.JobsChanged += Coordinator_JobsChanged;
        _coordinator.JobStatusChanged += Coordinator_JobStatusChanged;
        Refresh();
    }

    public void ShowJobs()
    {
        Visibility = Visibility.Visible;
        MarkVisibleTerminalJobsViewed();
        Refresh();
    }

    public async Task HideJobsAsync()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        UpdateElapsedTimer();
        await DismissViewedTerminalJobsAsync();
    }

    public void Refresh()
    {
        if (_coordinator is null) return;
        var snapshot = _coordinator.GetSnapshot();
        var activeIds = snapshot.Select(job => job.GenerationId).ToHashSet();
        for (var index = _jobs.Count - 1; index >= 0; index--)
        {
            if (!activeIds.Contains(_jobs[index].GenerationId)) _jobs.RemoveAt(index);
        }

        foreach (var job in snapshot)
        {
            var existing = _jobs.FirstOrDefault(item => item.GenerationId == job.GenerationId);
            if (existing is null) _jobs.Add(new GenerationJobListItem(job));
            else existing.Update(job);
        }

        EmptyText.Visibility = _jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        JobsList.Visibility = _jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (IsOpen) MarkVisibleTerminalJobsViewed();
        RefreshElapsedTimes();
        UpdateElapsedTimer();
    }

    private void Coordinator_JobsChanged(object? sender, EventArgs e)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || Dispatcher.HasShutdownStarted) return;
            Refresh();
        }, DispatcherPriority.Background);
    }

    private void Coordinator_JobStatusChanged(object? sender, GenerationJobStatusChangedEventArgs e)
    {
        if (_disposed || !IsTerminal(e.CurrentStatus) || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || Dispatcher.HasShutdownStarted) return;
            if (IsOpen) _viewedTerminalJobIds.Add(e.GenerationId);
        }, DispatcherPriority.Background);
    }

    private void MarkVisibleTerminalJobsViewed()
    {
        if (_coordinator is null) return;
        foreach (var job in _coordinator.GetSnapshot().Where(job => IsTerminal(job.Status)))
            _viewedTerminalJobIds.Add(job.GenerationId);
    }

    private async Task DismissViewedTerminalJobsAsync()
    {
        if (_coordinator is null || _dismissingViewedJobs || _viewedTerminalJobIds.Count == 0) return;
        _dismissingViewedJobs = true;
        try
        {
            var dismissed = await _coordinator.DismissAsync(_viewedTerminalJobIds.ToArray());
            foreach (var id in dismissed) _viewedTerminalJobIds.Remove(id);
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(
                this,
                new GenerationJobsPanelErrorEventArgs(
                    $"Completed jobs could not be cleared: {exception.Message}"));
        }
        finally
        {
            _dismissingViewedJobs = false;
        }
    }

    private void RefreshElapsedTimes()
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var job in _jobs) job.RefreshElapsed(now);
    }

    private void UpdateElapsedTimer()
    {
        if (_disposed || Dispatcher.HasShutdownStarted)
        {
            _elapsedTimer.Stop();
            return;
        }

        var hasActiveJob = _coordinator?.GetSnapshot().Any(job =>
            job.Status is GenerationStatus.Queued or GenerationStatus.Running) == true;
        if (IsOpen && hasActiveJob)
        {
            if (!_elapsedTimer.IsEnabled) _elapsedTimer.Start();
            return;
        }

        _elapsedTimer.Stop();
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        await HideJobsAsync();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid generationId })
            CancelRequested?.Invoke(this, new GenerationJobCancelRequestedEventArgs(generationId));
    }

    private static bool IsTerminal(GenerationStatus status) =>
        status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _elapsedTimer.Stop();
        if (_coordinator is not null)
        {
            _coordinator.JobsChanged -= Coordinator_JobsChanged;
            _coordinator.JobStatusChanged -= Coordinator_JobStatusChanged;
        }
        GC.SuppressFinalize(this);
    }
}
