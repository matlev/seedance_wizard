using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using ReelForge.Application;

namespace ReelForge.App.Views.Dialogs;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification =
    "The window cancels and disposes its operation token when it closes.")]
public partial class CloneProjectProgressWindow : Window
{
    private readonly Func<IProgress<ProjectCloneProgress>, CancellationToken, Task<ProjectCloneResult>> _clone;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _started;
    private bool _complete;
    private bool _cancellationDisposed;

    public CloneProjectProgressWindow(Func<IProgress<ProjectCloneProgress>, CancellationToken, Task<ProjectCloneResult>> clone)
    {
        _clone = clone;
        InitializeComponent();
        ContentRendered += CloneProjectProgressWindow_ContentRendered;
    }

    public ProjectCloneResult? Result { get; private set; }
    public Exception? Failure { get; private set; }
    public bool WasCanceled { get; private set; }

    public void RequestCancellation()
    {
        if (_cancellationDisposed) return;
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _cancellationDisposed = true;
        }
    }

    private async void CloneProjectProgressWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= CloneProjectProgressWindow_ContentRendered;
        if (_started) return;
        _started = true;
        var progress = new Progress<ProjectCloneProgress>(UpdateProgress);
        try
        {
            Result = await _clone(progress, _cancellation.Token);
            _complete = true;
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            WasCanceled = true;
            _complete = true;
            Close();
        }
        catch (Exception exception)
        {
            Failure = exception;
            _complete = true;
            Close();
        }
    }

    private void UpdateProgress(ProjectCloneProgress progress)
    {
        PhaseText.Text = progress.Phase switch
        {
            ProjectClonePhase.Validating => "Preparing clone…",
            ProjectClonePhase.Scanning => "Scanning project files…",
            ProjectClonePhase.Copying => "Copying project files…",
            ProjectClonePhase.WritingProject => "Writing cloned project…",
            ProjectClonePhase.ValidatingClone => "Checking cloned project…",
            ProjectClonePhase.Publishing => "Finishing clone…",
            ProjectClonePhase.Completed => "Clone complete.",
            _ => "Cloning project…"
        };
        CurrentItemText.Text = progress.CurrentRelativePath ?? string.Empty;
        var canMeasure = progress.TotalBytes > 0;
        CloneProgressBar.IsIndeterminate = !canMeasure && progress.Phase is not ProjectClonePhase.Completed;
        if (canMeasure)
        {
            CloneProgressBar.Value = Math.Clamp(progress.CopiedBytes * 100d / progress.TotalBytes, 0, 100);
            ProgressDetailText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} of {1:N0} files · {2:N1} MB of {3:N1} MB",
                progress.CopiedFileCount, progress.TotalFileCount,
                progress.CopiedBytes / 1024d / 1024d, progress.TotalBytes / 1024d / 1024d);
        }
        else if (progress.TotalFileCount > 0)
        {
            CloneProgressBar.IsIndeterminate = false;
            CloneProgressBar.Value = Math.Clamp(progress.CopiedFileCount * 100d / progress.TotalFileCount, 0, 100);
            ProgressDetailText.Text = string.Format(CultureInfo.CurrentCulture, "{0:N0} of {1:N0} files", progress.CopiedFileCount, progress.TotalFileCount);
        }
        else
        {
            ProgressDetailText.Text = progress.Phase == ProjectClonePhase.Completed ? "Clone completed." : "Please wait…";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Cancelling…";
        PhaseText.Text = "Cancelling clone…";
        RequestCancellation();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_complete && _started)
        {
            e.Cancel = true;
            Cancel_Click(this, new RoutedEventArgs());
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        RequestCancellation();
        _cancellation.Dispose();
        _cancellationDisposed = true;
        base.OnClosed(e);
    }
}
