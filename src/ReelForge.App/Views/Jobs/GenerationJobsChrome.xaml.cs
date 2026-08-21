using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.Jobs;

public partial class GenerationJobsChrome : UserControl, IDisposable
{
    private readonly IReadOnlyList<BitmapSource> _spriteFrames;
    private readonly DispatcherTimer _animationTimer;
    private GenerationJobCoordinator? _coordinator;
    private int _spriteFrameIndex;
    private bool _jobsOpen;
    private bool _disposed;

    public GenerationJobsChrome()
    {
        InitializeComponent();
        _spriteFrames = LoadSpriteFrames();
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _animationTimer.Tick += (_, _) => UpdateSprite(advanceFrame: true);
        _animationTimer.Start();
    }

    public event EventHandler? OpenRequested;

    public void Initialize(GenerationJobCoordinator coordinator)
    {
        if (_coordinator is not null) throw new InvalidOperationException("The jobs chrome is already initialized.");
        _coordinator = coordinator;
        _coordinator.JobsChanged += Coordinator_JobsChanged;
        _coordinator.JobStatusChanged += Coordinator_JobStatusChanged;
        UpdateSprite(advanceFrame: false);
    }

    public void SetJobsOpen(bool isOpen)
    {
        _jobsOpen = isOpen;
        if (isOpen) ActivityIndicator.Visibility = Visibility.Collapsed;
    }

    private void Coordinator_JobsChanged(object? sender, EventArgs e)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() => UpdateSprite(advanceFrame: false), DispatcherPriority.Background);
    }

    private void Coordinator_JobStatusChanged(object? sender, GenerationJobStatusChangedEventArgs e)
    {
        if (_disposed || _jobsOpen || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            ActivityIndicator.Visibility = Visibility.Visible;
            ActivityIndicator.ToolTip =
                $"{e.ProjectName}: job status changed from {e.PreviousStatus} to {e.CurrentStatus}.";
        }, DispatcherPriority.Background);
    }

    private void UpdateSprite(bool advanceFrame)
    {
        var hasActiveJob = _coordinator?.GetSnapshot().Any(job =>
            job.Status is GenerationStatus.Queued or GenerationStatus.Running) == true;
        if (!hasActiveJob)
        {
            ActiveJobSprite.Visibility = Visibility.Collapsed;
            _spriteFrameIndex = 0;
            ActiveJobSprite.Source = _spriteFrames[0];
            return;
        }

        if (ActiveJobSprite.Visibility != Visibility.Visible)
        {
            _spriteFrameIndex = 0;
            ActiveJobSprite.Source = _spriteFrames[0];
            ActiveJobSprite.Visibility = Visibility.Visible;
            return;
        }

        if (!advanceFrame) return;
        _spriteFrameIndex = (_spriteFrameIndex + 1) % _spriteFrames.Count;
        ActiveJobSprite.Source = _spriteFrames[_spriteFrameIndex];
    }

    private static IReadOnlyList<BitmapSource> LoadSpriteFrames()
    {
        var uri = new Uri(
            "pack://application:,,,/ReelForge.App;component/Assets/Sprites/forging_reel_animation.png",
            UriKind.Absolute);
        var resource = System.Windows.Application.GetResourceStream(uri)
            ?? throw new InvalidDataException("The forging-reel sprite resource is missing.");
        using var stream = resource.Stream;
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var sheet = decoder.Frames[0];
        if (sheet.PixelWidth % 2 != 0 || sheet.PixelWidth / 2 != sheet.PixelHeight)
            throw new InvalidDataException(
                "The forging-reel sprite must contain two equal square frames side by side.");

        var frameWidth = sheet.PixelWidth / 2;
        return Enumerable.Range(0, 2).Select(index =>
        {
            var frame = new CroppedBitmap(
                sheet,
                new Int32Rect(index * frameWidth, 0, frameWidth, sheet.PixelHeight));
            frame.Freeze();
            return (BitmapSource)frame;
        }).ToArray();
    }

    private void Jobs_Click(object sender, RoutedEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animationTimer.Stop();
        if (_coordinator is not null)
        {
            _coordinator.JobsChanged -= Coordinator_JobsChanged;
            _coordinator.JobStatusChanged -= Coordinator_JobStatusChanged;
        }
        GC.SuppressFinalize(this);
    }
}
