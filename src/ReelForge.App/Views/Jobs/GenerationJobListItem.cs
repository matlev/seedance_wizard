using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReelForge.Application;

namespace ReelForge.App.Views.Jobs;

public sealed class GenerationJobListItem : INotifyPropertyChanged
{
    private TrackedGenerationJob _job;
    private string _elapsedText = "0:00";
    private string _undoSendRemainingText = "0s";

    public GenerationJobListItem(TrackedGenerationJob job)
    {
        _job = job;
        RefreshElapsed(DateTimeOffset.UtcNow);
    }

    public Guid GenerationId => _job.GenerationId;
    public string ProjectName => _job.ProjectName;
    public string ProviderAndModel => $"{_job.ProviderDisplayName} • {_job.ModelVersion}";
    public string StatusText => _job.Status.ToString();
    public string Message => _job.Message;
    public string ElapsedText => _elapsedText;
    public string UndoSendRemainingText => _undoSendRemainingText;
    public bool IsUndoSendPending => _job.IsAwaitingSubmission && _job.UndoSendExpiresAt.HasValue;
    public bool WasCancelledBeforeSubmission => _job.WasCancelledBeforeSubmission;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(TrackedGenerationJob job)
    {
        _job = job;
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProviderAndModel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(IsUndoSendPending));
        OnPropertyChanged(nameof(WasCancelledBeforeSubmission));
        RefreshElapsed(DateTimeOffset.UtcNow);
    }

    public void RefreshElapsed(DateTimeOffset now)
    {
        if (IsUndoSendPending && _job.UndoSendExpiresAt is { } expiresAt)
        {
            var remainingSeconds = Math.Max(0, (int)Math.Ceiling((expiresAt - now).TotalSeconds));
            var remainingText = $"{remainingSeconds}s";
            if (remainingText != _undoSendRemainingText)
            {
                _undoSendRemainingText = remainingText;
                OnPropertyChanged(nameof(UndoSendRemainingText));
            }
            return;
        }

        var elapsedUntil = _job.CompletedAt ?? now;
        var elapsedFrom = _job.ProviderSubmittedAt ?? _job.RequestedAt;
        var elapsed = elapsedUntil > elapsedFrom ? elapsedUntil - elapsedFrom : TimeSpan.Zero;
        var text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        if (text == _elapsedText) return;
        _elapsedText = text;
        OnPropertyChanged(nameof(ElapsedText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
