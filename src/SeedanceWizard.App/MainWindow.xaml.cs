using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SeedanceWizard.Application;
using SeedanceWizard.Core;
using SeedanceWizard.Infrastructure;

namespace SeedanceWizard.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ProjectAsset> _assets = [];
    private readonly ObservableCollection<GenerationRecord> _generations = [];
    private readonly ProjectWorkspace _workspace;
    private readonly FfprobeMediaInspectionService _mediaInspector;
    private readonly IVideoGenerationProvider _generationProvider;
    private readonly IMediaToolDiscovery _mediaToolDiscovery;
    private readonly IMediaToolSettingsStore _mediaToolSettingsStore;
    private MediaToolAvailability _mediaTools;
    private readonly DispatcherTimer _positionTimer;

    public MainWindow()
    {
        InitializeComponent();

        _mediaToolDiscovery = new MediaToolDiscovery();
        _mediaToolSettingsStore = new JsonMediaToolSettingsStore();
        var configuredTools = LoadMediaToolConfiguration();
        _mediaTools = _mediaToolDiscovery.Discover(configuredTools.FfmpegPath, configuredTools.FfprobePath);
        var processRunner = new ExternalProcessRunner();
        _mediaInspector = new FfprobeMediaInspectionService(_mediaTools.FfprobePath, processRunner);
        var projectStore = new PortableProjectStore();
        var assetImporter = new AssetImportService(_mediaInspector);
        _workspace = new ProjectWorkspace(projectStore, assetImporter);
        _generationProvider = new FakeVideoGenerationProvider();

        AssetsList.ItemsSource = _assets;
        ReferenceAssetsList.ItemsSource = _assets;
        GenerationsList.ItemsSource = _generations;
        ConfigureGenerationPanel();

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => UpdatePlaybackPosition();
        _positionTimer.Start();

        MediaToolsText.Text = _mediaTools.Summary;
        FfmpegPathTextBox.Text = _mediaTools.FfmpegPath ?? configuredTools.FfmpegPath ?? string.Empty;
        FfprobePathTextBox.Text = _mediaTools.FfprobePath ?? configuredTools.FfprobePath ?? string.Empty;
        MediaToolSettingsStatusText.Text = _mediaTools.Summary;
    }

    private MediaToolConfiguration LoadMediaToolConfiguration()
    {
        try
        {
            return _mediaToolSettingsStore.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return new MediaToolConfiguration();
        }
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e) =>
        BrowseForMediaTool("Select ffmpeg.exe", "ffmpeg.exe", FfmpegPathTextBox);

    private void BrowseFfprobe_Click(object sender, RoutedEventArgs e) =>
        BrowseForMediaTool("Select ffprobe.exe", "ffprobe.exe", FfprobePathTextBox);

    private void BrowseForMediaTool(string title, string expectedFileName, TextBox target)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{expectedFileName}|{expectedFileName}|Executables (*.exe)|*.exe|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FileName;
        }
    }

    private void AutoDetectTools_Click(object sender, RoutedEventArgs e)
    {
        var detected = _mediaToolDiscovery.Discover();
        FfmpegPathTextBox.Text = detected.FfmpegPath ?? string.Empty;
        FfprobePathTextBox.Text = detected.FfprobePath ?? string.Empty;
        MediaToolSettingsStatusText.Text = detected.Summary;
    }

    private async void SaveTools_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "Saving media tool settings…",
            async () =>
            {
                var configuration = new MediaToolConfiguration
                {
                    FfmpegPath = ValidateExecutablePath(FfmpegPathTextBox.Text, "ffmpeg.exe"),
                    FfprobePath = ValidateExecutablePath(FfprobePathTextBox.Text, "ffprobe.exe")
                };

                await _mediaToolSettingsStore.SaveAsync(configuration);
                _mediaTools = _mediaToolDiscovery.Discover(configuration.FfmpegPath, configuration.FfprobePath);
                _mediaInspector.UpdateExecutablePath(_mediaTools.FfprobePath);
                MediaToolsText.Text = _mediaTools.Summary;
                MediaToolSettingsStatusText.Text = _mediaTools.Summary;
                StatusText.Text = "Media tool settings saved and applied.";
            });
    }

    private static string? ValidateExecutablePath(string value, string expectedFileName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(value.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The selected {expectedFileName} does not exist.", fullPath);
        }

        if (!Path.GetFileName(fullPath).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Select {expectedFileName}, not {Path.GetFileName(fullPath)}.");
        }

        return fullPath;
    }

    private void ConfigureGenerationPanel()
    {
        var capabilities = _generationProvider.Capabilities;
        ProviderText.Text = $"{capabilities.DisplayName}\n{capabilities.ModelVersion} • no paid API calls";

        ModeComboBox.ItemsSource = capabilities.Modes;
        ModeComboBox.SelectedItem = capabilities.Modes.Contains(GenerationMode.ReferenceToVideo)
            ? GenerationMode.ReferenceToVideo
            : capabilities.Modes[0];

        DurationSlider.Minimum = capabilities.MinimumDurationSeconds;
        DurationSlider.Maximum = capabilities.MaximumDurationSeconds;
        DurationSlider.Value = Math.Clamp(15, capabilities.MinimumDurationSeconds, capabilities.MaximumDurationSeconds);

        AspectRatioComboBox.ItemsSource = capabilities.AspectRatios;
        AspectRatioComboBox.SelectedItem = capabilities.AspectRatios.Contains("16:9")
            ? "16:9"
            : capabilities.AspectRatios[0];

        ResolutionComboBox.ItemsSource = capabilities.Resolutions;
        ResolutionComboBox.SelectedItem = capabilities.Resolutions.Contains("720p")
            ? "720p"
            : capabilities.Resolutions[0];
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose an empty folder for the portable Seedance Wizard project",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var projectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(dialog.FolderName));
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = "Untitled project";
        }

        await RunUiActionAsync(
            "Creating project…",
            async () =>
            {
                await _workspace.CreateAsync(dialog.FolderName, projectName);
                RefreshProjectUi();
            });
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Seedance Wizard project",
            Filter = "Seedance Wizard project (project.json)|project.json|JSON files (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunUiActionAsync(
            "Opening project…",
            async () =>
            {
                await _workspace.OpenAsync(dialog.FileName);
                RefreshProjectUi();
            });
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "Saving project…",
            async () =>
            {
                await _workspace.SaveAsync();
                StatusText.Text = "Project saved.";
            });
    }

    private async void ImportAssets_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectOpen())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import image, video, or audio assets",
            Filter = "Supported media|*.bmp;*.gif;*.heic;*.heif;*.jpeg;*.jpg;*.png;*.tif;*.tiff;*.webp;*.avi;*.m4v;*.mkv;*.mov;*.mp4;*.webm;*.wmv;*.aac;*.flac;*.m4a;*.mp3;*.ogg;*.wav;*.wma|All files|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunUiActionAsync(
            $"Importing {dialog.FileNames.Length} asset(s)…",
            async () =>
            {
                var imported = await _workspace.ImportAssetsAsync(dialog.FileNames);
                foreach (var asset in imported)
                {
                    _assets.Add(asset);
                }

                StatusText.Text = $"Imported {imported.Count} asset(s).";
            });
    }

    private async void AssetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetsList.SelectedItem is not ProjectAsset asset)
        {
            return;
        }

        GenerationsList.SelectedItem = null;

        await RunUiActionAsync(
            $"Inspecting {asset.FileName}…",
            async () =>
            {
                if (asset.MediaType is MediaType.Video or MediaType.Audio &&
                    asset.Encoding is null &&
                    _mediaTools.FfprobePath is not null)
                {
                    asset.Encoding = await _mediaInspector.InspectAsync(_workspace.GetAbsoluteAssetPath(asset));
                    asset.DurationSeconds = asset.Encoding.DurationSeconds;
                    asset.Width = asset.Encoding.Video?.Width;
                    asset.Height = asset.Encoding.Video?.Height;
                    await _workspace.SaveAsync();
                }

                InspectorText.Text = FormatAssetInspector(asset);
                ShowAssetPreview(asset);
                StatusText.Text = $"Selected {asset.FileName}.";
            });
    }

    private void GenerationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GenerationsList.SelectedItem is not GenerationRecord generation)
        {
            return;
        }

        AssetsList.SelectedItem = null;
        InspectorText.Text = FormatGenerationInspector(generation);
        StatusText.Text = $"Selected generation {generation.Id}.";
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectOpen())
        {
            return;
        }

        var request = new GenerationRequest
        {
            Prompt = PromptTextBox.Text,
            Mode = (GenerationMode)(ModeComboBox.SelectedItem ?? GenerationMode.ReferenceToVideo),
            DurationSeconds = (int)DurationSlider.Value,
            AspectRatio = (string)(AspectRatioComboBox.SelectedItem ?? "16:9"),
            Resolution = (string)(ResolutionComboBox.SelectedItem ?? "720p"),
            ReferenceAssetIds = ReferenceAssetsList.SelectedItems.Cast<ProjectAsset>().Select(asset => asset.Id).ToList()
        };

        GenerateButton.IsEnabled = false;
        GenerationStatusText.Text = "Submitting to the fake provider…";
        try
        {
            var generation = await _workspace.SubmitGenerationAsync(_generationProvider, request);
            _generations.Insert(0, generation);
            GenerationStatusText.Text = $"{generation.Status}: {generation.ProviderJobId}\nNo API call was made and no media file was produced.";
            StatusText.Text = "Fake generation recorded in project history.";
        }
        catch (GenerationValidationException exception)
        {
            GenerationStatusText.Text = exception.Message;
        }
        catch (Exception exception)
        {
            ShowError("Generation failed", exception);
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private void ShowAssetPreview(ProjectAsset asset)
    {
        VideoPreview.Stop();
        VideoPreview.Source = null;
        VideoPreview.Visibility = Visibility.Collapsed;
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PositionSlider.Value = 0;

        var absolutePath = _workspace.GetAbsoluteAssetPath(asset);
        if (asset.MediaType == MediaType.Image)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(absolutePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            ImagePreview.Source = bitmap;
            ImagePreview.Visibility = Visibility.Visible;
            return;
        }

        VideoPreview.Source = new Uri(absolutePath, UriKind.Absolute);
        VideoPreview.Visibility = Visibility.Visible;
    }

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = VideoPreview.NaturalDuration.TimeSpan.TotalSeconds;
        }

        VideoPreview.Pause();
        UpdatePlaybackPosition();
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Pause();
        UpdatePlaybackPosition();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.Source is not null)
        {
            VideoPreview.Play();
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => VideoPreview.Pause();

    private void PositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (VideoPreview.Source is not null)
        {
            VideoPreview.Position = TimeSpan.FromSeconds(PositionSlider.Value);
        }
    }

    private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DurationText is not null)
        {
            DurationText.Text = $"{(int)e.NewValue}s";
        }
    }

    private void UpdatePlaybackPosition()
    {
        if (VideoPreview.Source is null)
        {
            TimeText.Text = "00:00 / 00:00";
            return;
        }

        var current = VideoPreview.Position;
        var duration = VideoPreview.NaturalDuration.HasTimeSpan
            ? VideoPreview.NaturalDuration.TimeSpan
            : TimeSpan.Zero;

        PositionSlider.Value = current.TotalSeconds;
        TimeText.Text = $"{FormatTime(current)} / {FormatTime(duration)}";
    }

    private void RefreshProjectUi()
    {
        _assets.Clear();
        _generations.Clear();

        if (_workspace.Project is null)
        {
            return;
        }

        foreach (var asset in _workspace.Project.Assets)
        {
            _assets.Add(asset);
        }

        foreach (var generation in _workspace.Project.Generations.OrderByDescending(item => item.RequestedAt))
        {
            _generations.Add(generation);
        }

        ProjectTitleText.Text = $"{_workspace.Project.Name}  •  {_assets.Count} assets";
        Title = $"{_workspace.Project.Name} — Seedance Wizard";
        StatusText.Text = $"Opened {_workspace.Location!.ProjectFilePath}";
    }

    private bool EnsureProjectOpen()
    {
        if (_workspace.Project is not null)
        {
            return true;
        }

        MessageBox.Show(this, "Create or open a project first.", "Seedance Wizard", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private async Task RunUiActionAsync(string status, Func<Task> action)
    {
        StatusText.Text = status;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowError("Operation failed", exception);
        }
    }

    private void ShowError(string title, Exception exception)
    {
        StatusText.Text = exception.Message;
        InspectorText.Text = $"{title}\n\n{exception}";
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string FormatAssetInspector(ProjectAsset asset)
    {
        var builder = new StringBuilder();
        builder.AppendLine(asset.FileName);
        builder.AppendLine($"ID: {asset.Id}");
        builder.AppendLine($"Type: {asset.MediaType}");
        builder.AppendLine($"Origin: {asset.Origin}");
        builder.AppendLine($"Path: {asset.RelativePath}");
        builder.AppendLine($"Created: {asset.CreatedAt.LocalDateTime:g}");

        if (asset.DurationSeconds is not null)
        {
            builder.AppendLine($"Duration: {asset.DurationSeconds:0.###} seconds");
        }

        var encoding = asset.Encoding;
        if (encoding is null)
        {
            builder.AppendLine();
            builder.AppendLine("Encoding metadata unavailable. Install/configure ffprobe, then reselect the asset.");
            return builder.ToString();
        }

        builder.AppendLine();
        builder.AppendLine("CONTAINER");
        builder.AppendLine($"Format: {encoding.ContainerFormat ?? "—"}");
        builder.AppendLine($"Size: {FormatBytes(encoding.SizeBytes)}");
        builder.AppendLine($"Bit rate: {encoding.BitRate?.ToString("N0", CultureInfo.InvariantCulture) ?? "—"} bps");

        if (encoding.Video is { } video)
        {
            builder.AppendLine();
            builder.AppendLine("VIDEO");
            builder.AppendLine($"Codec: {video.Codec ?? "—"} / {video.CodecProfile ?? "—"}");
            builder.AppendLine($"Dimensions: {video.Width?.ToString(CultureInfo.InvariantCulture) ?? "—"} × {video.Height?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            builder.AppendLine($"Pixel format: {video.PixelFormat ?? "—"}");
            builder.AppendLine($"Frame rate: {video.FrameRate ?? "—"}");
            builder.AppendLine($"Time base: {video.TimeBase ?? "—"}");
            builder.AppendLine($"Codec level: {video.CodecLevel?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
        }

        if (encoding.Audio is { } audio)
        {
            builder.AppendLine();
            builder.AppendLine("AUDIO");
            builder.AppendLine($"Codec: {audio.Codec ?? "—"}");
            builder.AppendLine($"Sample rate: {audio.SampleRate?.ToString(CultureInfo.InvariantCulture) ?? "—"} Hz");
            builder.AppendLine($"Channels: {audio.Channels?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            builder.AppendLine($"Layout: {audio.ChannelLayout ?? "—"}");
        }

        return builder.ToString();
    }

    private static string FormatGenerationInspector(GenerationRecord generation)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Generation {generation.Id}");
        builder.AppendLine($"Status: {generation.Status}");
        builder.AppendLine($"Provider: {generation.ProviderId}");
        builder.AppendLine($"Model: {generation.ModelVersion}");
        builder.AppendLine($"Provider job: {generation.ProviderJobId ?? "—"}");
        builder.AppendLine($"Requested: {generation.RequestedAt.LocalDateTime:g}");
        builder.AppendLine($"Completed: {generation.CompletedAt?.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) ?? "—"}");
        builder.AppendLine();
        builder.AppendLine("PROMPT");
        builder.AppendLine(generation.Request.Prompt);
        builder.AppendLine();
        builder.AppendLine("SETTINGS");
        builder.AppendLine($"Mode: {generation.Request.Mode}");
        builder.AppendLine($"Duration: {generation.Request.DurationSeconds}s");
        builder.AppendLine($"Aspect ratio: {generation.Request.AspectRatio}");
        builder.AppendLine($"Resolution: {generation.Request.Resolution}");
        builder.AppendLine($"References: {generation.Request.ReferenceAssetIds.Count}");

        foreach (var pair in generation.ResponseMetadata)
        {
            builder.AppendLine($"{pair.Key}: {pair.Value}");
        }

        if (generation.Error is not null)
        {
            builder.AppendLine();
            builder.AppendLine("ERROR");
            builder.AppendLine(generation.Error.Message);
            builder.AppendLine(generation.Error.TechnicalDetails);
        }

        return builder.ToString();
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
