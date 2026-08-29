using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ReelForge.Application;
using ReelForge.App.Views.ProjectMedia;

namespace ReelForge.App.Views.Editing;

public partial class CompositionTimelineControl : UserControl, IDisposable
{
    private const double TrackTop = 25;
    private const double TrackRowHeight = 68;
    private const double TrackHeaderHeight = 18;
    private const double AudioLaneHeight = 34;

    private sealed record StickyContent(
        FrameworkElement Element,
        double Left,
        double Width,
        double MinimumTrailingWidth);

    private readonly DispatcherTimer _externalDragAutoScrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(40)
    };

    private readonly DispatcherTimer _itemDragAutoScrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(40)
    };

    private readonly List<StickyContent> _stickyContent = [];
    private CompositionTimelineState _state = CompositionTimelineState.Empty;
    private CompositionTimelineLayoutResult? _layout;
    private Line? _playhead;
    private double _zoom = 1;
    private int _zoomRevision;
    private bool _renderScheduled;
    private bool _rendering;
    private bool _mutationPending;
    private bool _disposed;
    private bool _scrubbing;
    private Guid? _selectedTrackId;
    private bool _resumePlayback;
    private Guid? _pendingSegmentDragId;
    private Guid? _activeSegmentDragId;
    private Point _segmentDragStart;
    private double _segmentDragPointerX;
    private int _segmentDragOriginalIndex = -1;
    private int _segmentDragTargetIndex = -1;
    private Guid? _pendingAudioDragId;
    private Guid? _activeAudioDragId;
    private Point _audioDragStart;
    private double _audioDragPointerOffset;
    private double _audioDraftStartSeconds;
    private long _audioOriginalStartTicks;
    private double _itemDragViewportX;
    private double _itemDragAutoScrollDelta;
    private CompositionTimelineDropDescriptor? _dragDescriptor;
    private Guid? _dragTargetTrackId;
    private double _dragViewportX;
    private double _dragAutoScrollDelta;

    public CompositionTimelineControl()
    {
        InitializeComponent();
        _externalDragAutoScrollTimer.Tick += ExternalDragAutoScrollTimer_Tick;
        _itemDragAutoScrollTimer.Tick += ItemDragAutoScrollTimer_Tick;
        Unloaded += Timeline_Unloaded;
    }

    public event EventHandler<CompositionTimelineActivationEventArgs>? ActivationRequested;
    public event EventHandler<CompositionTimelineSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<CompositionTimelineSeekEventArgs>? SeekRequested;
    public event EventHandler<CompositionTimelineReorderEventArgs>? SegmentReorderRequested;
    public event EventHandler<CompositionTimelineAudioMoveEventArgs>? AudioMoveRequested;
    public event EventHandler<CompositionTimelineDropEventArgs>? MediaDropRequested;
    public event EventHandler<CompositionTimelineItemEventArgs>? SplitRequested;
    public event EventHandler<CompositionTimelineItemEventArgs>? ShiftLeftRequested;
    public event EventHandler<CompositionTimelineItemEventArgs>? ShiftRightRequested;
    public event EventHandler<CompositionTimelineItemEventArgs>? DetachAudioRequested;
    public event EventHandler<CompositionTimelineItemEventArgs>? RemoveRequested;
    public event EventHandler<CompositionTimelineTrackEventArgs>? TrackSelected;
    public event EventHandler<CompositionTimelineTrackKindEventArgs>? TrackAppendRequested;
    public event EventHandler<CompositionTimelineTrackEventArgs>? TrackCreateRequested;
    public event EventHandler<CompositionTimelineTrackEventArgs>? TrackDeleteRequested;
    public event EventHandler<CompositionTimelineTrackReorderEventArgs>? TrackMoveUpRequested;
    public event EventHandler<CompositionTimelineTrackReorderEventArgs>? TrackMoveDownRequested;
    public event EventHandler<CompositionTimelineTrackBooleanEventArgs>? TrackLockChanged;
    public event EventHandler<CompositionTimelineTrackBooleanEventArgs>? VideoTrackVisibilityChanged;
    public event EventHandler<CompositionTimelineTrackBooleanEventArgs>? AudioTrackMuteChanged;

    public double ProjectedDurationSeconds => _layout?.ProjectedDurationSeconds ?? 0;
    public Guid? SelectedTrackId => _selectedTrackId;

    private void AddVideoTrack_Click(object sender, RoutedEventArgs e) =>
        TrackAppendRequested?.Invoke(this, new CompositionTimelineTrackKindEventArgs(CompositionTimelineTrackKind.Video));

    private void AddAudioTrack_Click(object sender, RoutedEventArgs e) =>
        TrackAppendRequested?.Invoke(this, new CompositionTimelineTrackKindEventArgs(CompositionTimelineTrackKind.Audio));

    public bool TryGetSegmentSpan(Guid segmentId, out CompositionTimelineSegmentSpan span)
    {
        var result = _layout?.Segments.SingleOrDefault(item => item.SegmentId == segmentId);
        if (result is null)
        {
            span = default!;
            return false;
        }

        span = new CompositionTimelineSegmentSpan(
            result.SegmentId,
            result.StartSeconds,
            result.DurationSeconds);
        return true;
    }

    public void UpdateState(CompositionTimelineState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        TimingWarningText.Visibility = state.DegradedOccurrenceCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        TimingWarningText.ToolTip = state.TimingWarningSummary;
        _layout = CalculateLayoutProjection();
        if (!_mutationPending)
        {
            ScheduleRender();
        }
    }

    /// <summary>
    /// Completes a control-to-shell mutation handoff after the shell has pushed
    /// the authoritative timeline state for either success or failure.
    /// </summary>
    public void CompletePendingMutation()
    {
        if (!_mutationPending)
        {
            return;
        }

        _mutationPending = false;
        ResetItemDrag();
        IsHitTestVisible = true;
        ScheduleRender();
    }

    public void Clear()
    {
        _mutationPending = false;
        IsHitTestVisible = true;
        CancelInteractions();
        _state = CompositionTimelineState.Empty;
        _layout = null;
        _playhead = null;
        TimelineCanvas.Children.Clear();
        TimelineCanvas.Width = 1;
        DurationText.Text = "No segments";
        TimingWarningText.Visibility = Visibility.Collapsed;
        TimingWarningText.ToolTip = null;
        HideDropFeedback();
    }

    public void ScheduleRender()
    {
        if (_renderScheduled || _disposed)
        {
            return;
        }

        _renderScheduled = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _renderScheduled = false;
            if (!_disposed)
            {
                Render();
            }
        }, DispatcherPriority.ContextIdle);
    }

    public void UpdatePlayback(double seconds, bool isPlaying, bool isVisible, bool isInteractive)
    {
        _state = _state with
        {
            PlaybackSeconds = seconds,
            IsPlaying = isPlaying,
            IsPlaybackVisible = isVisible,
            IsInteractive = isInteractive
        };
        UpdatePlayhead(seconds);
    }

    public void CancelExternalDrag()
    {
        HideDropFeedback();
    }

    public void CancelInteractions()
    {
        if (_mutationPending)
        {
            return;
        }

        var wasScrubbing = _scrubbing;
        _scrubbing = false;
        _resumePlayback = false;
        ResetItemDrag();
        TimelineCanvas.Cursor = null;
        if (Mouse.Captured == TimelineCanvas)
        {
            Mouse.Capture(null);
        }

        if (wasScrubbing)
        {
            SeekRequested?.Invoke(
                this,
                new CompositionTimelineSeekEventArgs(
                    _state.PlaybackSeconds,
                    false,
                    CompositionTimelineSeekPhase.Cancelled));
        }
    }

    private void TimelineScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Render();
    }

    private void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_rendering && Math.Abs(e.HorizontalChange) > 0.001)
        {
            UpdateStickyContent();
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var zoom = Math.Round(Math.Clamp(e.NewValue, 1, 8) * 4) / 4;
        if (ZoomText is not null)
        {
            ZoomText.Text = $"{zoom * 100:0}%";
        }
        if (Math.Abs(zoom - _zoom) < 0.001)
        {
            return;
        }

        var oldWidth = _layout?.ContentWidth ?? 0;
        var focusRatio = oldWidth > 0 && TimelineScrollViewer.ViewportWidth > 0
            ? Math.Clamp(
                (TimelineScrollViewer.HorizontalOffset + TimelineScrollViewer.ViewportWidth / 2) /
                oldWidth,
                0,
                1)
            : 0.5;
        _zoom = zoom;
        Render();
        var revision = ++_zoomRevision;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || revision != _zoomRevision)
            {
                return;
            }

            TimelineScrollViewer.UpdateLayout();
            var offset = focusRatio * TimelineScrollViewer.ExtentWidth -
                         TimelineScrollViewer.ViewportWidth / 2;
            TimelineScrollViewer.ScrollToHorizontalOffset(Math.Clamp(
                offset,
                0,
                Math.Max(0, TimelineScrollViewer.ExtentWidth - TimelineScrollViewer.ViewportWidth)));
        }, DispatcherPriority.Render);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 1;
    }

    private void Render()
    {
        if (_rendering || _mutationPending || _disposed)
        {
            return;
        }

        _rendering = true;
        try
        {
            var items = _state.Segments;
            _layout = CalculateLayoutProjection(items);
            TimelineCanvas.Children.Clear();
            _stickyContent.Clear();
            TimelineCanvas.Width = _layout.ContentWidth;

            TimelineCanvas.Height = Math.Max(
                124,
                TrackTop + _state.Tracks.Count * TrackRowHeight + 4);
            DurationText.Text = _layout.Segments.Count == 0
                ? "No segments"
                : _layout.HasUnknownDurations
                    ? $"~{FormatTime(_layout.ProjectedDurationSeconds)} total • estimated"
                    : $"{FormatTime(_layout.KnownDurationSeconds)} total";

            DrawRuler();
            foreach (var track in _state.Tracks)
            {
                DrawTrackHeader(track);
                var top = GetTrackItemTop(track.TrackId);
                if (track.Kind == CompositionTimelineTrackKind.Video)
                {
                    foreach (var item in items.Where(item => item.TrackId == track.TrackId))
                        DrawSegment(item, top);
                }
                else
                {
                    foreach (var item in _state.AudioClips.Where(item => item.TrackId == track.TrackId))
                        DrawAudio(item, top);
                }
            }

            _playhead = new Line
            {
                Y1 = 16,
                Y2 = TimelineCanvas.Height - 2,
                Stroke = FindResource("AccentBrush") as Brush ?? Brushes.MediumPurple,
                StrokeThickness = 2,
                IsHitTestVisible = false,
                Visibility = _activeSegmentDragId is not null || _activeAudioDragId is not null
                    ? Visibility.Collapsed
                    : Visibility.Visible
            };
            Panel.SetZIndex(_playhead, 10);
            TimelineCanvas.Children.Add(_playhead);
            UpdateStickyContent();
            UpdatePlayhead(_state.PlaybackSeconds);
        }
        finally
        {
            _rendering = false;
        }
    }

    private CompositionTimelineLayoutResult CalculateLayoutProjection(
        IReadOnlyList<CompositionSegmentListItem>? segments = null)
    {
        var source = segments ?? _state.Segments;
        return CompositionTimelineLayout.Calculate(
            source.Select(item => new CompositionTimelineSegmentInput(
                item.SegmentId,
                item.DurationSeconds,
                item.TimelineStart)).ToArray(),
            GetViewportWidth(),
            zoomFactor: _zoom);
    }

    private void DrawRuler()
    {
        if (_layout is null || _layout.Segments.Count == 0)
        {
            return;
        }

        var tickCount = Math.Clamp((int)(_layout.ContentWidth / 140), 2, 80);
        for (var index = 0; index <= tickCount; index++)
        {
            var x = _layout.ContentWidth * index / tickCount;
            var seconds = _layout.ProjectedDurationSeconds * index / tickCount;
            TimelineCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 17,
                Y2 = 23,
                Stroke = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
            var label = new TextBlock
            {
                Text = _zoom > 1.001 ? FormatRulerTime(seconds) : FormatTime(seconds),
                Foreground = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
                FontSize = 9,
                IsHitTestVisible = false
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(
                x - label.DesiredSize.Width / 2,
                1,
                Math.Max(1, _layout.ContentWidth - label.DesiredSize.Width - 1)));
            Canvas.SetTop(label, 1);
            TimelineCanvas.Children.Add(label);
        }
    }

    private double GetTrackItemTop(Guid trackId)
    {
        var index = _state.Tracks.ToList().FindIndex(track => track.TrackId == trackId);
        return TrackTop + Math.Max(0, index) * TrackRowHeight + TrackHeaderHeight + 2;
    }

    private void DrawTrackHeader(CompositionTimelineTrackRow track)
    {
        var top = GetTrackItemTop(track.TrackId) - TrackHeaderHeight - 1;
        var background = new Rectangle
        {
            Width = _layout?.ContentWidth ?? 1,
            Height = TrackHeaderHeight,
            Fill = new SolidColorBrush(track.TrackId == _selectedTrackId
                ? Color.FromRgb(49, 43, 82)
                : Color.FromRgb(20, 24, 34)),
            IsHitTestVisible = false
        };
        Canvas.SetTop(background, top);
        TimelineCanvas.Children.Add(background);
        var header = new Border
        {
            Height = TrackHeaderHeight,
            Background = Brushes.Transparent,
            Cursor = Cursors.Arrow,
            ToolTip = $"{track.DisplayName} • {track.ItemCount} item{(track.ItemCount == 1 ? string.Empty : "s")}",
            Child = new DockPanel { LastChildFill = false }
        };
        var panel = (DockPanel)header.Child;
        panel.Children.Add(new TextBlock
        {
            Text = $"{track.DisplayName}  {track.StatusText}" + (track.IsLocked ? "  Locked" : string.Empty),
            Foreground = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0)
        });
        panel.Children.Add(CreateTrackButton(track, track.IsLocked ? "Unlock" : "Lock", () =>
            TrackLockChanged?.Invoke(this, new CompositionTimelineTrackBooleanEventArgs(track.TrackId, !track.IsLocked))));
        panel.Children.Add(CreateTrackButton(track, track.Kind == CompositionTimelineTrackKind.Video
            ? (track.IsVisibleOrMuted ? "Hide" : "Show")
            : (track.IsVisibleOrMuted ? "Unmute" : "Mute"), () =>
        {
            if (track.Kind == CompositionTimelineTrackKind.Video)
                VideoTrackVisibilityChanged?.Invoke(this, new CompositionTimelineTrackBooleanEventArgs(track.TrackId, !track.IsVisibleOrMuted));
            else
                AudioTrackMuteChanged?.Invoke(this, new CompositionTimelineTrackBooleanEventArgs(track.TrackId, !track.IsVisibleOrMuted));
        }));
        panel.Children.Add(CreateTrackButton(track, "↑", () => TrackMoveUpRequested?.Invoke(this,
            new CompositionTimelineTrackReorderEventArgs(track.TrackId, track.Index - 1)), enabled: track.Index > 0));
        var sameKindCount = _state.Tracks.Count(candidate => candidate.Kind == track.Kind);
        panel.Children.Add(CreateTrackButton(track, "↓", () => TrackMoveDownRequested?.Invoke(this,
            new CompositionTimelineTrackReorderEventArgs(track.TrackId, track.Index + 1)), enabled: track.Index < sameKindCount - 1));
        panel.Children.Add(CreateTrackButton(track, "+", () => TrackCreateRequested?.Invoke(this,
            new CompositionTimelineTrackEventArgs(track.TrackId))));
        panel.Children.Add(CreateTrackButton(track, "Delete", () => TrackDeleteRequested?.Invoke(this,
            new CompositionTimelineTrackEventArgs(track.TrackId)), enabled: track.ItemCount == 0));
        header.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _selectedTrackId = track.TrackId;
            Render();
            TrackSelected?.Invoke(this, new CompositionTimelineTrackEventArgs(track.TrackId));
        };
        Canvas.SetTop(header, top);
        Canvas.SetLeft(header, 0);
        TimelineCanvas.Children.Add(header);
    }

    private static Button CreateTrackButton(CompositionTimelineTrackRow track, string label, Action action, bool enabled = true)
    {
        var button = new Button
        {
            Content = label,
            FontSize = 9,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(0, 1, 3, 1),
            IsEnabled = enabled && (!track.IsLocked || label == "Unlock"),
            ToolTip = label
        };
        button.Click += (_, e) => { e.Handled = true; action(); };
        return button;
    }

    private void DrawSegment(CompositionSegmentListItem item, double top)
    {
        if (_layout is null)
        {
            return;
        }

        var span = _layout.Segments.Single(candidate => candidate.SegmentId == item.SegmentId);
        var isDragging = item.SegmentId == _activeSegmentDragId;
        var isSelected = item.SegmentId == _state.SelectedSegmentId;
        var border = new Border
        {
            Tag = item.SegmentId,
            Width = Math.Max(1, span.Width - 3),
            Height = 57,
            Background = new SolidColorBrush(isSelected
                ? Color.FromRgb(62, 54, 105)
                : Color.FromRgb(31, 37, 51)),
            BorderBrush = isSelected
                ? FindResource("AccentBrush") as Brush ?? Brushes.MediumPurple
                : FindResource("BorderBrush") as Brush ?? Brushes.DimGray,
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 5, 7, 4),
            ClipToBounds = true,
            Opacity = isDragging ? 0.84 : 1,
            Cursor = Cursors.Arrow,
            ToolTip = $"{item.DisplayName}\n" +
                      $"Starts at {FormatTime(span.StartSeconds)}\n" +
                      $"{item.DurationText} • {item.AudioText}\n" +
                      "Click to select"
        };
        if (isDragging)
        {
            border.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 12,
                ShadowDepth = 3,
                Opacity = 0.65
            };
        }

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            Foreground = FindResource("TextBrush") as Brush ?? Brushes.White,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = $"{item.DurationText} • {item.AudioText}",
            Foreground = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var identity = new Border
        {
            Child = text,
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 24, 34)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 2, 4, 2)
        };
        var contents = new Grid();
        contents.Children.Add(identity);
        if (item.IsTimingDegraded)
        {
            contents.Children.Add(CreateTimingWarningGlyph(item.TimingWarningToolTip));
        }
        var remove = CreateRemoveButton(item.SegmentId);
        remove.IsEnabled = CanRemove(_state, item.SegmentId);
        contents.Children.Add(remove);
        border.Child = contents;
        border.ContextMenu = CreateRemoveOnlyMenu(item.SegmentId);
        border.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
        border.MouseLeave += (_, _) => remove.Visibility = Visibility.Collapsed;
        border.MouseLeftButtonDown += Segment_MouseLeftButtonDown;
        var left = span.Left + 1;
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        if (isDragging)
        {
            Panel.SetZIndex(border, 20);
        }

        TimelineCanvas.Children.Add(border);
        _stickyContent.Add(new StickyContent(identity, left, border.Width, 64));
    }

    private void DrawAudio(CompositionAudioClipListItem item, double top)
    {
        if (_layout is null)
        {
            return;
        }

        var isDragging = item.AudioClipId == _activeAudioDragId;
        var start = Math.Clamp(
            isDragging ? _audioDraftStartSeconds : item.TimelineStart.TotalSeconds,
            0,
            _layout.ProjectedDurationSeconds);
        var left = _layout.GetPlayheadX(start);
        var right = _layout.GetPlayheadX(Math.Min(
            _layout.ProjectedDurationSeconds,
            start + Math.Max(0.25, item.DurationSeconds)));
        var width = Math.Max(56, right - left);
        if (left + width > _layout.ContentWidth)
        {
            width = Math.Max(1, _layout.ContentWidth - left);
        }

        var isSelected = item.AudioClipId == _state.SelectedAudioClipId;
        var border = new Border
        {
            Tag = item.AudioClipId,
            Width = width,
            Height = AudioLaneHeight,
            Background = new SolidColorBrush(isSelected
                ? Color.FromRgb(42, 91, 74)
                : Color.FromRgb(28, 65, 55)),
            BorderBrush = isSelected
                ? FindResource("AccentBrush") as Brush ?? Brushes.MediumPurple
                : new SolidColorBrush(Color.FromRgb(55, 136, 107)),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 3, 6, 3),
            ClipToBounds = true,
            Opacity = isDragging ? 0.86 : 1,
            Cursor = Cursors.Arrow,
            ToolTip = $"Audio: {item.DisplayName}\n" +
                      $"Starts at {FormatTimePrecise(start)}\n" +
                      $"{item.DurationText} • {item.MixText}\n" +
                      "Click to select"
        };
        if (isDragging)
        {
            border.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                ShadowDepth = 3,
                Opacity = 0.65
            };
        }

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = $"♪ {item.DisplayName}",
            Foreground = FindResource("TextBrush") as Brush ?? Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = item.MixText,
            Foreground = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
            FontSize = 9,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var identity = new Border
        {
            Child = text,
            MaxWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(220, 13, 31, 26)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1)
        };
        var contents = new Grid();
        contents.Children.Add(identity);
        if (item.IsTimingDegraded)
        {
            contents.Children.Add(CreateTimingWarningGlyph(item.TimingWarningToolTip));
        }
        var remove = CreateRemoveButton(item.AudioClipId);
        remove.IsEnabled = CanRemove(_state, item.AudioClipId);
        contents.Children.Add(remove);
        border.Child = contents;
        border.ContextMenu = CreateRemoveOnlyMenu(item.AudioClipId);
        border.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
        border.MouseLeave += (_, _) => remove.Visibility = Visibility.Collapsed;
        border.MouseLeftButtonDown += Audio_MouseLeftButtonDown;
        Canvas.SetLeft(border, left + 1);
        Canvas.SetTop(border, top);
        TimelineCanvas.Children.Add(border);
        _stickyContent.Add(new StickyContent(identity, left + 1, width, 48));
    }

    private Button CreateRemoveButton(Guid itemId)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 12,
                Foreground = Brushes.White
            },
            Width = 27,
            Height = 25,
            Padding = new Thickness(4),
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Style = FindResource("DangerButtonStyle") as Style,
            ToolTip = "Remove from composition"
        };
        button.Click += (_, e) =>
        {
            e.Handled = true;
            RemoveRequested?.Invoke(this, new CompositionTimelineItemEventArgs(itemId));
        };
        Panel.SetZIndex(button, 30);
        return button;
    }

    private static TextBlock CreateTimingWarningGlyph(string? toolTip) => new()
    {
        Text = "⚠",
        FontFamily = new FontFamily("Segoe UI Symbol"),
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(255, 204, 70)),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 0, 31, 0),
        ToolTip = toolTip
    };

    private ContextMenu CreateRemoveOnlyMenu(Guid itemId)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "Remove from composition", RemoveRequested, itemId);
        menu.Opened += (_, _) => UpdateRemoveOnlyMenuCapability(menu, itemId);
        UpdateRemoveOnlyMenuCapability(menu, itemId);
        return menu;
    }

    private void UpdateRemoveOnlyMenuCapability(ContextMenu menu, Guid itemId)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = CanRemove(_state, itemId);
        }
    }

    internal static bool CanRemove(CompositionTimelineState state, Guid itemId) =>
        state.Capabilities.TryGetValue(itemId, out var capabilities) && capabilities.CanRemove;

    private ContextMenu CreateSegmentMenu(Guid itemId)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "Detach audio…", DetachAudioRequested, itemId);
        AddMenuItem(menu, _state.SplitActionLabel, SplitRequested, itemId);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Shift Left", ShiftLeftRequested, itemId);
        AddMenuItem(menu, "Shift Right", ShiftRightRequested, itemId);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Remove", RemoveRequested, itemId, true);
        menu.Opened += (_, _) => UpdateMenuCapabilities(menu, itemId);
        UpdateMenuCapabilities(menu, itemId);
        return menu;
    }

    private ContextMenu CreateAudioMenu(Guid itemId)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "Remove", RemoveRequested, itemId, true);
        menu.Opened += (_, _) => UpdateMenuCapabilities(menu, itemId);
        UpdateMenuCapabilities(menu, itemId);
        return menu;
    }

    private void AddMenuItem(
        ContextMenu menu,
        string header,
        EventHandler<CompositionTimelineItemEventArgs>? request,
        Guid itemId,
        bool danger = false)
    {
        if (request is null)
        {
            return;
        }

        var item = new MenuItem { Header = header };
        if (danger)
        {
            item.Foreground = new SolidColorBrush(Color.FromRgb(145, 24, 47));
        }
        item.Click += (_, _) => request(this, new CompositionTimelineItemEventArgs(itemId));
        menu.Items.Add(item);
    }

    private void UpdateMenuCapabilities(ContextMenu menu, Guid itemId)
    {
        if (!_state.Capabilities.TryGetValue(itemId, out var caps))
        {
            caps = new CompositionTimelineItemCapabilities();
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = item.Header?.ToString() switch
            {
                "Detach audio…" => caps.CanDetachAudio,
                "Shift Left" => caps.CanShiftLeft,
                "Shift Right" => caps.CanShiftRight,
                "Remove" => caps.CanRemove,
                _ => caps.CanSplit && CanSplitAtCurrentPlayback(itemId)
            };
        }
    }

    private bool CanSplitAtCurrentPlayback(Guid itemId)
    {
        if (!TryGetSegmentSpan(itemId, out var span))
        {
            return false;
        }

        const double epsilon = 0.000_000_1;
        var playbackSeconds = _state.PlaybackSeconds;
        var isAfterStart = _state.SplitAfterSelectedFrame
            ? playbackSeconds >= span.StartSeconds - epsilon
            : playbackSeconds > span.StartSeconds + epsilon;
        return isAfterStart &&
               playbackSeconds < span.StartSeconds + span.DurationSeconds - epsilon;
    }

    private void Segment_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Guid id })
        {
            return;
        }
        SelectionChanged?.Invoke(this, new CompositionTimelineSelectionChangedEventArgs(id, null));
        e.Handled = true;
    }

    private void Audio_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Guid id })
        {
            return;
        }

        var audio = _state.AudioClips.SingleOrDefault(item => item.AudioClipId == id);
        if (audio is null)
        {
            return;
        }

        SelectionChanged?.Invoke(this, new CompositionTimelineSelectionChangedEventArgs(null, id));
        e.Handled = true;
    }

    private void TimelineCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(TimelineCanvas);
        if (!_state.IsCompositionSelected)
        {
            var seek = point.Y is >= 0 and <= 24 && _layout is not null
                ? _layout.GetTimeAtX(point.X)
                : (double?)null;
            ActivationRequested?.Invoke(this, new CompositionTimelineActivationEventArgs(seek));
            e.Handled = seek is not null;
            return;
        }

        if (!_state.IsInteractive || _layout is null || point.Y is < 0 or > 24)
        {
            return;
        }

        _resumePlayback = _state.IsPlaying;
        _scrubbing = true;
        TimelineCanvas.CaptureMouse();
        TimelineCanvas.Cursor = Cursors.SizeWE;
        RaiseSeek(point.X, CompositionTimelineSeekPhase.Started);
        e.Handled = true;
    }

    private void TimelineCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                RaiseSeek(e.GetPosition(TimelineCanvas).X, CompositionTimelineSeekPhase.Changed);
                e.Handled = true;
            }

            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelInteractions();
            return;
        }

        var point = e.GetPosition(TimelineCanvas);
        if (_pendingSegmentDragId is Guid segmentId)
        {
            if (_activeSegmentDragId is null &&
                Math.Abs(point.X - _segmentDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _segmentDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _activeSegmentDragId = segmentId;
            _segmentDragPointerX = point.X;
            _segmentDragTargetIndex = CalculateReorderIndex(segmentId, point.X);
            UpdateItemAutoScroll(point.X);
            TimelineCanvas.Cursor = Cursors.SizeAll;
            Render();
            e.Handled = true;
            return;
        }

        if (_pendingAudioDragId is Guid audioId && _layout is not null)
        {
            if (_activeAudioDragId is null &&
                Math.Abs(point.X - _audioDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _audioDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _activeAudioDragId = audioId;
            _audioDraftStartSeconds = _layout.GetTimeAtX(point.X - _audioDragPointerOffset);
            UpdateItemAutoScroll(point.X);
            TimelineCanvas.Cursor = Cursors.SizeWE;
            Render();
            e.Handled = true;
        }
    }

    private void TimelineCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(TimelineCanvas);
        if (_scrubbing)
        {
            RaiseSeek(point.X, CompositionTimelineSeekPhase.Completed);
            _scrubbing = false;
            _resumePlayback = false;
            ReleaseMouse();
            e.Handled = true;
            return;
        }

        if (_pendingSegmentDragId is null && _pendingAudioDragId is null)
        {
            return;
        }

        if (_activeSegmentDragId is Guid segmentId &&
            _segmentDragTargetIndex >= 0 &&
            _segmentDragTargetIndex != _segmentDragOriginalIndex &&
            point.Y >= 0 &&
            point.Y <= TimelineCanvas.ActualHeight &&
            SegmentReorderRequested is { } reorderRequested)
        {
            BeginPendingMutation();
            reorderRequested.Invoke(
                this,
                new CompositionTimelineReorderEventArgs(segmentId, _segmentDragTargetIndex));
            e.Handled = true;
            return;
        }

        if (_activeAudioDragId is Guid audioId &&
            point.Y >= 0 &&
            point.Y <= TimelineCanvas.ActualHeight &&
            AudioMoveRequested is { } moveRequested)
        {
            var start = TimeSpan.FromMilliseconds(Math.Round(
                Math.Max(0, _audioDraftStartSeconds) * 1000,
                MidpointRounding.AwayFromZero));
            if (start.Ticks != _audioOriginalStartTicks)
            {
                BeginPendingMutation();
                moveRequested.Invoke(
                    this,
                    new CompositionTimelineAudioMoveEventArgs(audioId, start));
                e.Handled = true;
                return;
            }
        }

        ResetItemDrag();
        ReleaseMouse();
        Render();
        e.Handled = true;
    }

    private void BeginPendingMutation()
    {
        _mutationPending = true;
        _itemDragAutoScrollTimer.Stop();
        _itemDragAutoScrollDelta = 0;
        IsHitTestVisible = false;
        ReleaseMouse();
    }

    private void TimelineCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        CancelInteractions();
    }

    private void RaiseSeek(double x, CompositionTimelineSeekPhase phase)
    {
        if (_layout is null)
        {
            return;
        }

        var seconds = _layout.GetTimeAtX(x);
        SeekRequested?.Invoke(
            this,
            new CompositionTimelineSeekEventArgs(seconds, _resumePlayback, phase));
    }

    private void ResetItemDrag()
    {
        _pendingSegmentDragId = null;
        _activeSegmentDragId = null;
        _pendingAudioDragId = null;
        _activeAudioDragId = null;
        _segmentDragStart = default;
        _segmentDragPointerX = 0;
        _segmentDragOriginalIndex = -1;
        _segmentDragTargetIndex = -1;
        _audioDragStart = default;
        _audioDragPointerOffset = 0;
        _audioDraftStartSeconds = 0;
        _audioOriginalStartTicks = 0;
        _itemDragViewportX = 0;
        _itemDragAutoScrollTimer.Stop();
        _itemDragAutoScrollDelta = 0;
    }

    private void ReleaseMouse()
    {
        TimelineCanvas.Cursor = null;
        if (Mouse.Captured == TimelineCanvas)
        {
            Mouse.Capture(null);
        }
    }

    private int CalculateReorderIndex(Guid segmentId, double contentX)
    {
        return CompositionTimelineLayout.CalculateReorder(
            _state.Segments.Select(item => new CompositionTimelineSegmentInput(
                item.SegmentId,
                item.DurationSeconds,
                item.TimelineStart)).ToArray(),
            segmentId,
            contentX,
            GetViewportWidth(),
            zoomFactor: _zoom).InsertionIndex;
    }

    private double GetViewportWidth()
    {
        var width = TimelineScrollViewer.ActualWidth;
        if (!double.IsFinite(width) || width <= 1)
        {
            width = TimelineScrollViewer.ViewportWidth;
        }

        return double.IsFinite(width) && width > 1 ? width : 1;
    }

    private void UpdateItemAutoScroll(double contentX)
    {
        var width = TimelineScrollViewer.ViewportWidth;
        if (!double.IsFinite(width) || width <= 0)
        {
            _itemDragAutoScrollTimer.Stop();
            return;
        }

        var viewportX = contentX - TimelineScrollViewer.HorizontalOffset;
        _itemDragViewportX = Math.Clamp(viewportX, 0, width);
        _itemDragAutoScrollDelta = CompositionTimelineLayout.GetEdgeAutoScrollDelta(viewportX, width);
        if (Math.Abs(_itemDragAutoScrollDelta) < 0.1)
        {
            _itemDragAutoScrollTimer.Stop();
            return;
        }

        if (!_itemDragAutoScrollTimer.IsEnabled)
        {
            _itemDragAutoScrollTimer.Start();
        }
    }

    private void ItemDragAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_activeSegmentDragId is null && _activeAudioDragId is null)
        {
            _itemDragAutoScrollTimer.Stop();
            return;
        }

        var offset = Math.Clamp(
            TimelineScrollViewer.HorizontalOffset + _itemDragAutoScrollDelta,
            0,
            Math.Max(0, TimelineScrollViewer.ExtentWidth - TimelineScrollViewer.ViewportWidth));
        if (Math.Abs(offset - TimelineScrollViewer.HorizontalOffset) < 0.1)
        {
            _itemDragAutoScrollTimer.Stop();
            return;
        }

        TimelineScrollViewer.ScrollToHorizontalOffset(offset);
        TimelineScrollViewer.UpdateLayout();
        var contentX = TimelineScrollViewer.HorizontalOffset + Math.Clamp(
            _itemDragViewportX,
            0,
            Math.Max(1, TimelineScrollViewer.ViewportWidth));
        if (_activeSegmentDragId is Guid segmentId)
        {
            _segmentDragPointerX = contentX;
            _segmentDragTargetIndex = CalculateReorderIndex(segmentId, contentX);
        }
        else if (_layout is not null)
        {
            _audioDraftStartSeconds = _layout.GetTimeAtX(contentX - _audioDragPointerOffset);
        }

        Render();
    }

    private void Timeline_PreviewDragEnter(object sender, DragEventArgs e)
    {
        UpdateDropFeedback(e);
    }

    private void Timeline_PreviewDragOver(object sender, DragEventArgs e)
    {
        UpdateDropFeedback(e);
    }

    private void Timeline_PreviewDragLeave(object sender, DragEventArgs e)
    {
        HideDropFeedback();
        e.Handled = true;
    }

    private void Timeline_PreviewDrop(object sender, DragEventArgs e)
    {
        var item = ResolveDrop(e.Data);
        var track = item is null ? null : ResolveDropTargetTrack(_state, item.Kind, e.GetPosition(TimelineCanvas).Y);
        if (item is null || track is null || _layout is null)
        {
            e.Effects = DragDropEffects.None;
            HideDropFeedback();
            e.Handled = true;
            return;
        }

        var contentX = TimelineScrollViewer.HorizontalOffset + GetViewportX(e);
        MediaDropRequested?.Invoke(
            this,
            new CompositionTimelineDropEventArgs(
                item.AssetId,
                item.Kind,
                track.TrackId,
                _layout.GetTimeAtX(contentX),
                item.Kind == CompositionTimelineDropKind.Video
                    ? _layout.GetVideoInsertionIndex(contentX)
                    : -1));
        HideDropFeedback();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private CompositionTimelineDropDescriptor? ResolveDrop(IDataObject data)
    {
        if (!data.GetDataPresent(ProjectMediaDragData.Format))
        {
            return null;
        }

        return Guid.TryParse(data.GetData(ProjectMediaDragData.Format)?.ToString(), out var id)
            ? _state.EligibleDropItems.SingleOrDefault(item => item.AssetId == id)
            : null;
    }

    private void UpdateDropFeedback(DragEventArgs e)
    {
        var item = ResolveDrop(e.Data);
        var track = item is null ? null : ResolveDropTargetTrack(_state, item.Kind, e.GetPosition(TimelineCanvas).Y);
        if (item is null || track is null || _layout is null)
        {
            e.Effects = DragDropEffects.None;
            HideDropFeedback();
            e.Handled = true;
            return;
        }

        _dragDescriptor = item;
        _dragTargetTrackId = track.TrackId;
        _dragViewportX = GetViewportX(e);
        _dragAutoScrollDelta = CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            _dragViewportX,
            TimelineScrollViewer.ViewportWidth);
        if (Math.Abs(_dragAutoScrollDelta) < 0.1)
        {
            _externalDragAutoScrollTimer.Stop();
        }
        else if (!_externalDragAutoScrollTimer.IsEnabled)
        {
            _externalDragAutoScrollTimer.Start();
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        RenderDropFeedback();
    }

    private double GetViewportX(DragEventArgs e)
    {
        return Math.Clamp(
            e.GetPosition(TimelineScrollViewer).X,
            0,
            Math.Max(1, TimelineScrollViewer.ViewportWidth));
    }

    private void ExternalDragAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_dragDescriptor is null || _layout is null || DropHint.Visibility != Visibility.Visible)
        {
            _externalDragAutoScrollTimer.Stop();
            return;
        }

        var offset = Math.Clamp(
            TimelineScrollViewer.HorizontalOffset + _dragAutoScrollDelta,
            0,
            Math.Max(0, TimelineScrollViewer.ExtentWidth - TimelineScrollViewer.ViewportWidth));
        if (Math.Abs(offset - TimelineScrollViewer.HorizontalOffset) < 0.1)
        {
            _externalDragAutoScrollTimer.Stop();
            return;
        }

        TimelineScrollViewer.ScrollToHorizontalOffset(offset);
        TimelineScrollViewer.UpdateLayout();
        RenderDropFeedback();
    }

    private void RenderDropFeedback()
    {
        if (_dragDescriptor is null || _layout is null)
        {
            return;
        }

        DropHint.Visibility = Visibility.Visible;
        DropHint.UpdateLayout();
        var origin = TimelineCanvas.TranslatePoint(new Point(0, 0), DropHint);
        var width = Math.Max(1, DropHint.ActualWidth);
        var contentX = TimelineScrollViewer.HorizontalOffset + _dragViewportX;
        var isVideo = _dragDescriptor.Kind == CompositionTimelineDropKind.Video;
        var targetTop = _dragTargetTrackId is { } targetTrackId
            ? GetTrackItemTop(targetTrackId)
            : TrackTop;
        var markerX = isVideo
            ? _layout.GetVideoInsertionX(_layout.GetVideoInsertionIndex(contentX))
            : contentX;
        const double inset = 3;
        Canvas.SetLeft(DropMarker, Math.Clamp(
            markerX - TimelineScrollViewer.HorizontalOffset,
            inset,
            Math.Max(inset, width - DropMarker.Width - inset)));
        Canvas.SetLeft(DropToken, Math.Clamp(
            _dragViewportX - DropToken.Width / 2,
            0,
            Math.Max(0, width - DropToken.Width)));
        DropHintText.Text = _dragDescriptor.DisplayName;
        DropMarker.Height = TrackRowHeight - TrackHeaderHeight - 4;
        Canvas.SetTop(DropMarker, origin.Y + targetTop);
        Canvas.SetTop(DropToken, origin.Y + targetTop + 8);
    }

    private void HideDropFeedback()
    {
        _externalDragAutoScrollTimer.Stop();
        _dragDescriptor = null;
        _dragTargetTrackId = null;
        _dragAutoScrollDelta = 0;
        DropHint.Visibility = Visibility.Hidden;
    }

    internal static CompositionTimelineTrackRow? ResolveDropTargetTrack(
        CompositionTimelineState state,
        CompositionTimelineDropKind kind,
        double timelineY)
    {
        if (!double.IsFinite(timelineY) || timelineY < TrackTop)
            return null;
        var index = (int)Math.Floor((timelineY - TrackTop) / TrackRowHeight);
        if (index < 0 || index >= state.Tracks.Count)
            return null;
        var track = state.Tracks[index];
        var requiredKind = kind == CompositionTimelineDropKind.Video
            ? CompositionTimelineTrackKind.Video
            : CompositionTimelineTrackKind.Audio;
        return track.Kind == requiredKind && !track.IsLocked ? track : null;
    }

    private void UpdateStickyContent()
    {
        var left = Math.Max(0, TimelineScrollViewer.HorizontalOffset);
        foreach (var item in _stickyContent)
        {
            item.Element.Margin = new Thickness(
                CompositionTimelineLayout.GetStickyContentOffset(
                    item.Left,
                    item.Width,
                    left,
                    item.MinimumTrailingWidth),
                0,
                0,
                0);
        }
    }

    private void UpdatePlayhead(double seconds)
    {
        if (_playhead is null || _layout is null || !_state.IsPlaybackVisible)
        {
            if (_playhead is not null)
            {
                _playhead.Visibility = Visibility.Collapsed;
            }

            return;
        }

        var x = _layout.GetPlayheadX(seconds);
        _playhead.X1 = x;
        _playhead.X2 = x;
        _playhead.Visibility = _activeSegmentDragId is null && _activeAudioDragId is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_state.IsPlaying &&
            AutoScrollCheckBox.IsChecked == true &&
            TimelineScrollViewer.ViewportWidth > 0)
        {
            var offset = _layout.GetAutoScrollOffset(
                seconds,
                TimelineScrollViewer.HorizontalOffset,
                TimelineScrollViewer.ViewportWidth);
            if (Math.Abs(offset - TimelineScrollViewer.HorizontalOffset) > 0.5)
            {
                TimelineScrollViewer.ScrollToHorizontalOffset(offset);
            }
        }
    }

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatTimePrecise(double seconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Round(
            Math.Max(0, seconds) * 1000,
            MidpointRounding.AwayFromZero));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatRulerTime(double seconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Round(
            Math.Max(0, seconds) * 1000,
            MidpointRounding.AwayFromZero));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private void Timeline_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelInteractions();
        HideDropFeedback();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mutationPending = false;
        IsHitTestVisible = true;
        CancelInteractions();
        HideDropFeedback();
        _externalDragAutoScrollTimer.Tick -= ExternalDragAutoScrollTimer_Tick;
        _itemDragAutoScrollTimer.Tick -= ItemDragAutoScrollTimer_Tick;
        Unloaded -= Timeline_Unloaded;
        GC.SuppressFinalize(this);
    }
}
