using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Un4seen.Bass;
using TagFile = TagLib.File;
using Line = System.Windows.Shapes.Line;
using Polygon = System.Windows.Shapes.Polygon;

namespace MajdataEdit;

internal sealed record MediaMarkerRequest(string Name, double? Time);
internal sealed record MediaPlayheadRequest(double Time);

public partial class MediaTimelineEditor : UserControl
{
    private enum ClipDragMode
    {
        None,
        Move,
        TrimLeft,
        TrimRight
    }

    private const double DefaultImageDuration = 5d;
    private const double MinimumClipDuration = 0.05d;
    private const double TrimHandleWidth = 8d;
    private const double RulerHeight = 34d;
    private const double LaneStart = 42d;
    private const double LaneHeight = 58d;
    private const double LaneGap = 8d;
    private const double DragThreshold = 3d;

    private MediaTimelineProject project = new();
    private string chartDirectory = "";
    private List<double> strongBeats = new();
    private List<double> weakBeats = new();
    private List<double> snapBeats = new();
    private double fallbackBeatDuration = 0.5d;
    private double songDuration;
    private double? startMarker;
    private double? endMarker;
    private double playhead;
    private double pixelsPerSecond = 16d;
    private MediaTimelineClip? selectedClip;
    private MediaTimelineClip? copiedClip;
    private MediaTimelineClip? draggedClip;
    private ClipDragMode clipDragMode;
    private Point dragOrigin;
    private double dragOriginalStart;
    private double dragOriginalSourceOffset;
    private double dragOriginalDuration;
    private bool dragChanged;
    private bool dragStarted;
    private bool playheadDragging;
    private Line? playheadLine;
    private Polygon? playheadHead;
    private readonly Stack<TimelineSnapshot> undoHistory = new();
    private readonly Stack<TimelineSnapshot> redoHistory = new();
    private TimelineSnapshot? dragSnapshot;
    private TimelineSnapshot committedSnapshot = new(new List<MediaTimelineClip>(), null);
    private string loadedChartDirectory = "";
    private bool hasPendingChanges;

    public MediaTimelineEditor()
    {
        InitializeComponent();
        TimelineCanvas.ContextMenu = BuildTimelineContextMenu();
        Loaded += (_, _) =>
        {
            ThemeManager.ThemeChanged += OnThemeChanged;
            RenderTimeline();
        };
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? ProjectChanged;
    internal event EventHandler<MediaMarkerRequest>? MarkerRequested;
    internal event EventHandler<MediaPlayheadRequest>? PlayheadChanged;
    internal bool HasPendingChanges => hasPendingChanges;

    public async Task ConfigureAsync(
        string directory,
        double chartDuration,
        IEnumerable<double> measureBeats,
        IEnumerable<double> subdivisionBeats,
        double beatDuration,
        double? trimStart,
        double? trimEnd)
    {
        var normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sameChart = normalizedDirectory.Equals(loadedChartDirectory, StringComparison.OrdinalIgnoreCase);
        chartDirectory = directory;
        songDuration = Math.Max(0d, chartDuration);
        SetBeatGrid(measureBeats, subdivisionBeats);
        fallbackBeatDuration = double.IsFinite(beatDuration) && beatDuration > 0d ? beatDuration : 0.5d;
        startMarker = trimStart;
        endMarker = trimEnd;
        playhead = 0d;
        selectedClip = null;
        if (sameChart)
        {
            RenderTimeline();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        loadedChartDirectory = normalizedDirectory;
        copiedClip = null;
        undoHistory.Clear();
        redoHistory.Clear();
        project = MediaTimelineProject.Load(directory);
        hasPendingChanges = false;
        var hadTemporaryProject = MediaTimelineProject.HasTemporaryFile(directory);

        if (project.Clips.Count == 0)
            await AddDefaultChartMediaAsync();
        committedSnapshot = CaptureSnapshot();

        if (hadTemporaryProject)
        {
            project = MediaTimelineProject.LoadWorking(directory);
            hasPendingChanges = true;
        }
        var metadataChanged = await RefreshSourceMetadataAsync();
        if (metadataChanged && hasPendingChanges)
            project.SaveTemporary(chartDirectory);
        else if (metadataChanged)
            committedSnapshot = CaptureSnapshot();

        RenderTimeline();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshChartState(
        double chartDuration,
        IEnumerable<double> measureBeats,
        IEnumerable<double> subdivisionBeats,
        double beatDuration,
        double? trimStart,
        double? trimEnd)
    {
        songDuration = Math.Max(0d, chartDuration);
        SetBeatGrid(measureBeats, subdivisionBeats);
        fallbackBeatDuration = double.IsFinite(beatDuration) && beatDuration > 0d ? beatDuration : 0.5d;
        startMarker = trimStart;
        endMarker = trimEnd;
        RenderTimeline();
    }

    internal IReadOnlyList<MediaTimelineClip> GetResolvedClips(MediaTrackKind track)
    {
        return project.Clips
            .Where(clip => clip.Track == track)
            .Select(clip =>
            {
                var copy = clip.Copy();
                copy.SourcePath = project.ResolveSourcePath(chartDirectory, clip);
                return copy;
            })
            .ToList();
    }

    internal static bool CanImportFile(string path) => TryGetTrackKind(path, out _);

    internal async Task AddFilesAsync(IEnumerable<string> files, double? insertionTime = null)
    {
        var snapshot = CaptureSnapshot();
        var added = false;
        var insertionByTrack = new Dictionary<MediaTrackKind, double>();
        foreach (var path in files.Where(File.Exists))
        {
            if (!TryGetTrackKind(path, out var track))
                continue;
            if (!insertionByTrack.TryGetValue(track, out var insertion))
            {
                var trackEnd = project.Clips.Where(clip => clip.Track == track)
                    .Select(clip => clip.TimelineEnd).DefaultIfEmpty(0d).Max();
                insertion = insertionTime ?? (playhead > 0.001d ? playhead : trackEnd);
            }
            insertion = Snap(insertion);
            var clip = await CreateClipAsync(path, track, insertion);
            clip.TrackIndex = ResolveTrackIndex(track, insertion, clip.Duration);
            project.Clips.Add(clip);
            added = true;
            insertionByTrack[track] = clip.TimelineEnd;
            selectedClip = clip;
        }
        if (!added)
            return;
        CommitEdit(snapshot);
        SaveProject("已添加媒体片段");
        RenderTimeline();
        Focus();
    }

    private void SetBeatGrid(IEnumerable<double> measureBeats, IEnumerable<double> subdivisionBeats)
    {
        strongBeats = NormalizeBeats(measureBeats);
        weakBeats = NormalizeBeats(subdivisionBeats);
        snapBeats = strongBeats.Concat(weakBeats).Distinct().OrderBy(value => value).ToList();
    }

    private static List<double> NormalizeBeats(IEnumerable<double> values) =>
        values.Where(double.IsFinite).Where(value => value >= 0d).Distinct().OrderBy(value => value).ToList();

    private async Task AddDefaultChartMediaAsync()
    {
        var candidates = new[]
        {
            ("pv.mp4", MediaTrackKind.Video),
            ("track.ogg", MediaTrackKind.Audio),
            ("track.mp3", MediaTrackKind.Audio)
        };
        foreach (var (name, track) in candidates)
        {
            if (track == MediaTrackKind.Audio && project.Clips.Any(clip => clip.Track == MediaTrackKind.Audio))
                continue;
            var path = Path.Combine(chartDirectory, name);
            if (!File.Exists(path))
                continue;
            project.Clips.Add(await CreateClipAsync(path, track, 0d));
        }
    }

    private async Task<MediaTimelineClip> CreateClipAsync(string path, MediaTrackKind track, double start)
    {
        path = await ImportMediaFileAsync(path);
        var isStillImage = IsImagePath(path);
        var duration = isStillImage ? DefaultImageDuration : await ProbeDurationAsync(path);
        return new MediaTimelineClip
        {
            Track = track,
            SourcePath = MediaTimelineProject.StoreSourcePath(chartDirectory, path),
            Name = Path.GetFileName(path),
            TimelineStart = Math.Max(0d, start),
            SourceDuration = Math.Max(MinimumClipDuration, duration),
            Duration = Math.Max(MinimumClipDuration, duration),
            IsStillImage = isStillImage
        };
    }

    private async Task<string> ImportMediaFileAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var chartRoot = Path.GetFullPath(chartDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(chartRoot, StringComparison.OrdinalIgnoreCase))
            return fullPath;

        var mediaDirectory = Path.Combine(chartDirectory, "media");
        Directory.CreateDirectory(mediaDirectory);
        var destination = Path.Combine(mediaDirectory, Path.GetFileName(fullPath));
        var suffix = 1;
        while (File.Exists(destination))
        {
            destination = Path.Combine(
                mediaDirectory,
                $"{Path.GetFileNameWithoutExtension(fullPath)}_{suffix++}{Path.GetExtension(fullPath)}");
        }

        await using var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(target);
        return destination;
    }

    private async Task<bool> RefreshSourceMetadataAsync()
    {
        var changed = false;
        foreach (var clip in project.Clips)
        {
            var sourcePath = project.ResolveSourcePath(chartDirectory, clip);
            if (Path.IsPathRooted(clip.SourcePath) && File.Exists(sourcePath))
            {
                sourcePath = await ImportMediaFileAsync(sourcePath);
                clip.SourcePath = MediaTimelineProject.StoreSourcePath(chartDirectory, sourcePath);
                changed = true;
            }
            var isStillImage = IsImagePath(sourcePath);
            changed |= clip.IsStillImage != isStillImage;
            clip.IsStillImage = isStillImage;
            if (clip.IsStillImage || !File.Exists(sourcePath))
                continue;
            var sourceDuration = await ProbeDurationAsync(sourcePath);
            var oldSourceDuration = clip.SourceDuration;
            var oldDuration = clip.Duration;
            clip.SourceDuration = sourceDuration;
            clip.Duration = Math.Min(
                clip.Duration,
                Math.Max(MinimumClipDuration, sourceDuration - clip.SourceOffset));
            changed |= Math.Abs(oldSourceDuration - clip.SourceDuration) > 0.001d ||
                       Math.Abs(oldDuration - clip.Duration) > 0.001d;
        }
        return changed;
    }

    private static Task<double> ProbeDurationAsync(string path) => Task.Run(() =>
    {
        if (IsAudioPath(path))
        {
            var stream = Bass.BASS_StreamCreateFile(
                path,
                0L,
                0L,
                BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_STREAM_PRESCAN);
            if (stream != 0)
            {
                try
                {
                    var byteLength = Bass.BASS_ChannelGetLength(stream, BASSMode.BASS_POS_BYTE);
                    var seconds = Bass.BASS_ChannelBytes2Seconds(stream, byteLength);
                    if (double.IsFinite(seconds) && seconds > 0d)
                        return seconds;
                }
                finally
                {
                    Bass.BASS_StreamFree(stream);
                }
            }
        }

        try
        {
            using var file = TagFile.Create(path);
            var seconds = file.Properties.Duration.TotalSeconds;
            return double.IsFinite(seconds) && seconds > 0d ? seconds : 10d;
        }
        catch
        {
            return 10d;
        }
    });

    private static bool IsImagePath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp";
    }

    private static bool IsAudioPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".mp3" or ".ogg" or ".wav" or ".flac" or ".m4a" or ".aac";
    }

    private void RenderTimeline()
    {
        if (!IsLoaded)
            return;

        TimelineCanvas.Children.Clear();
        var duration = Math.Max(30d, Math.Max(songDuration, project.Clips.Count == 0
            ? 0d
            : project.Clips.Max(clip => clip.TimelineEnd)) + 2d);
        duration = Math.Max(duration, Math.Max(startMarker ?? 0d, endMarker ?? 0d) + 2d);
        TimelineCanvas.Width = Math.Max(900d, duration * pixelsPerSecond + 120d);
        TimelineCanvas.Height = GetTimelineBottom() + 14d;
        TrackLabelCanvas.Height = TimelineCanvas.Height;

        RenderTrackLabels();
        for (var index = 0; index < GetVisibleTrackCount(MediaTrackKind.Video); index++)
            AddLane(GetLaneTop(MediaTrackKind.Video, index), "#163B82F6");
        for (var index = 0; index < GetVisibleTrackCount(MediaTrackKind.Audio); index++)
            AddLane(GetLaneTop(MediaTrackKind.Audio, index), "#1622C55E");
        DrawMusicalRuler(duration);
        DrawBeatGrid(duration);

        foreach (var clip in project.Clips.OrderBy(clip => clip.Track)
                     .ThenBy(clip => clip.TrackIndex)
                     .ThenBy(clip => clip.TimelineStart))
            AddClipElement(clip);

        if (startMarker.HasValue)
            AddMarkerLine(startMarker.Value, "START", Color.FromRgb(56, 189, 248));
        if (endMarker.HasValue)
            AddMarkerLine(endMarker.Value, "END", Color.FromRgb(244, 114, 182));
        AddPlayhead();
        CursorText.Text = $"播放头 {FormatTime(playhead)}";
    }

    private static int GetVisibleTrackCount(MediaTrackKind kind) => 2;

    private double GetLaneTop(MediaTrackKind kind, int trackIndex)
    {
        if (kind == MediaTrackKind.Video)
            return LaneStart + Math.Clamp(trackIndex, 0, 1) * (LaneHeight + LaneGap);
        return LaneStart + GetVisibleTrackCount(MediaTrackKind.Video) * (LaneHeight + LaneGap) + LaneGap +
               Math.Clamp(trackIndex, 0, 1) * (LaneHeight + LaneGap);
    }

    private double GetTimelineBottom()
    {
        var lastAudio = GetVisibleTrackCount(MediaTrackKind.Audio) - 1;
        return GetLaneTop(MediaTrackKind.Audio, lastAudio) + LaneHeight;
    }

    private int ResolveTrackIndex(MediaTrackKind kind, double start, double duration)
    {
        var end = start + duration;
        var overlapsPrimary = project.Clips.Any(clip => clip.Track == kind && clip.TrackIndex == 0 &&
            clip.TimelineStart < end - 0.0001d && clip.TimelineEnd > start + 0.0001d);
        return overlapsPrimary ? 1 : 0;
    }

    private void RenderTrackLabels()
    {
        TrackLabelCanvas.Children.Clear();
        var timeLabel = new TextBlock
        {
            Text = "时间",
            Width = 82d,
            Height = RulerHeight,
            TextAlignment = TextAlignment.Center,
            FontFamily = this.FontFamily,
            FontSize = this.FontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("HelperForeground", Colors.LightGray)
        };
        Canvas.SetTop(timeLabel, 9d);
        TrackLabelCanvas.Children.Add(timeLabel);

        AddTrackLabels(MediaTrackKind.Video, "视频");
        AddTrackLabels(MediaTrackKind.Audio, "音频");
    }

    private void AddTrackLabels(MediaTrackKind kind, string name)
    {
        var count = GetVisibleTrackCount(kind);
        for (var index = 0; index < count; index++)
        {
            var label = new TextBlock
            {
                Text = $"{name}轨{index + 1}",
                Width = 82d,
                Height = LaneHeight,
                TextAlignment = TextAlignment.Center,
                FontFamily = this.FontFamily,
                FontSize = this.FontSize,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetTop(label, GetLaneTop(kind, index) + 19d);
            TrackLabelCanvas.Children.Add(label);
        }
    }

    private void AddLane(double top, string background)
    {
        var lane = new Border
        {
            Width = TimelineCanvas.Width,
            Height = LaneHeight,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)),
            BorderBrush = ResourceBrush("MenuSeparator", Colors.DimGray),
            BorderThickness = new Thickness(0, 1, 0, 1),
            IsHitTestVisible = false
        };
        Canvas.SetTop(lane, top);
        TimelineCanvas.Children.Add(lane);
    }

    private void DrawMusicalRuler(double duration)
    {
        TimelineCanvas.Children.Add(new Line
        {
            X1 = 0d,
            X2 = TimelineCanvas.Width,
            Y1 = RulerHeight - 1d,
            Y2 = RulerHeight - 1d,
            Stroke = ResourceBrush("MenuSeparator", Colors.Gray),
            StrokeThickness = 1d,
            Opacity = 0.8d,
            IsHitTestVisible = false
        });

        if (strongBeats.Count == 0)
        {
            DrawFallbackRuler(duration);
            return;
        }

        var typicalMeasureSpacing = GetTypicalPixelSpacing(strongBeats);
        var measureLabelStride = Math.Max(1, (int)Math.Ceiling(72d / Math.Max(1d, typicalMeasureSpacing)));
        for (var index = 0; index < strongBeats.Count; index++)
        {
            var time = strongBeats[index];
            if (time > duration)
                break;
            var x = time * pixelsPerSecond;
            TimelineCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 7d,
                Y2 = RulerHeight,
                Stroke = ResourceBrush("MenuSeparator", Colors.Gray),
                StrokeThickness = 1.2d,
                Opacity = 0.95d,
                IsHitTestVisible = false
            });
            if (index % measureLabelStride != 0)
                continue;
            var label = new TextBlock
            {
                Text = $"{index + 1}小节  {FormatTime(time)}",
                FontSize = 10d,
                Foreground = ResourceBrush("ButtonForeground", Colors.White),
                Opacity = 0.86d,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, x + 3d);
            Canvas.SetTop(label, 0d);
            TimelineCanvas.Children.Add(label);
        }

        if (GetTypicalPixelSpacing(snapBeats) < 48d)
            return;
        foreach (var time in weakBeats.Where(time => time <= duration))
        {
            var x = time * pixelsPerSecond;
            TimelineCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 19d,
                Y2 = RulerHeight,
                Stroke = ResourceBrush("MenuSeparator", Colors.Gray),
                StrokeThickness = 0.7d,
                Opacity = 0.65d,
                IsHitTestVisible = false
            });
            var label = new TextBlock
            {
                Text = FormatTime(time),
                FontSize = 9d,
                Foreground = ResourceBrush("HelperForeground", Colors.LightGray),
                Opacity = 0.72d,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, x + 2d);
            Canvas.SetTop(label, 1d);
            TimelineCanvas.Children.Add(label);
        }
    }

    private double GetTypicalPixelSpacing(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return 0d;
        var spacings = new List<double>(Math.Min(values.Count - 1, 32));
        for (var index = 1; index < values.Count && spacings.Count < 32; index++)
        {
            var spacing = (values[index] - values[index - 1]) * pixelsPerSecond;
            if (spacing > 0.01d)
                spacings.Add(spacing);
        }
        if (spacings.Count == 0)
            return 0d;
        spacings.Sort();
        return spacings[spacings.Count / 2];
    }

    private void DrawFallbackRuler(double duration)
    {
        var step = pixelsPerSecond >= 80d ? 1d : pixelsPerSecond >= 20d ? 5d : 10d;
        for (var time = 0d; time <= duration; time += step)
        {
            var x = time * pixelsPerSecond;
            TimelineCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 16d,
                Y2 = RulerHeight,
                Stroke = ResourceBrush("MenuSeparator", Colors.Gray),
                StrokeThickness = 0.8d,
                Opacity = 0.7d,
                IsHitTestVisible = false
            });
            var label = new TextBlock
            {
                Text = FormatTime(time),
                FontSize = 9d,
                Foreground = ResourceBrush("HelperForeground", Colors.LightGray),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, x + 2d);
            Canvas.SetTop(label, 1d);
            TimelineCanvas.Children.Add(label);
        }
    }

    private void DrawBeatGrid(double duration)
    {
        DrawBeatLines(strongBeats, duration, 0.5d, 1.15d);
        DrawBeatLines(weakBeats, duration, 0.2d, 0.7d);
    }

    private void DrawBeatLines(IEnumerable<double> beats, double duration, double opacity, double thickness)
    {
        foreach (var beat in beats)
        {
            if (beat < 0d || beat > duration)
                continue;
            var x = beat * pixelsPerSecond;
            TimelineCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = RulerHeight,
                Y2 = GetTimelineBottom(),
                Stroke = ResourceBrush("HelperForeground", Colors.DodgerBlue),
                StrokeThickness = thickness,
                Opacity = opacity,
                IsHitTestVisible = false
            });
        }
    }

    private void AddClipElement(MediaTimelineClip clip)
    {
        var sourcePath = project.ResolveSourcePath(chartDirectory, clip);
        var missing = !File.Exists(sourcePath);
        var selected = ReferenceEquals(selectedClip, clip);
        var baseColor = clip.Track == MediaTrackKind.Video
            ? Color.FromRgb(59, 130, 246)
            : Color.FromRgb(34, 197, 94);
        var border = new Border
        {
            Tag = clip,
            Width = Math.Max(12d, clip.Duration * pixelsPerSecond),
            Height = LaneHeight - 10d,
            CornerRadius = new CornerRadius(5d),
            Background = new SolidColorBrush(Color.FromArgb(188, baseColor.R, baseColor.G, baseColor.B)),
            BorderBrush = new SolidColorBrush(missing
                ? Color.FromRgb(239, 68, 68)
                : selected ? Color.FromRgb(250, 204, 21) : Color.FromArgb(220, 235, 242, 250)),
            BorderThickness = new Thickness(selected ? 2.2d : 1d),
            Padding = new Thickness(0),
            Cursor = Cursors.SizeWE,
            ToolTip = missing ? $"文件不存在：{sourcePath}" : sourcePath
        };
        var clipGrid = new Grid();
        clipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrimHandleWidth) });
        clipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        clipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrimHandleWidth) });
        var content = new StackPanel
        {
            Margin = new Thickness(4, 4, 4, 3),
            Children =
            {
                new TextBlock
                {
                    Text = clip.Name,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = $"{FormatTime(clip.SourceOffset)} → {FormatTime(clip.SourceOffset + clip.Duration)}",
                    Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                    FontSize = 10d,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
        Grid.SetColumn(content, 1);
        clipGrid.Children.Add(content);
        clipGrid.Children.Add(CreateTrimHandle(0));
        clipGrid.Children.Add(CreateTrimHandle(2));
        border.Child = clipGrid;
        border.MouseLeftButtonDown += Clip_MouseLeftButtonDown;
        border.PreviewMouseRightButtonDown += (_, _) =>
        {
            selectedClip = clip;
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21));
            border.BorderThickness = new Thickness(2.2d);
        };
        border.ContextMenu = BuildClipContextMenu();
        Canvas.SetLeft(border, clip.TimelineStart * pixelsPerSecond);
        Canvas.SetTop(border, GetLaneTop(clip.Track, clip.TrackIndex) + 5d);
        Panel.SetZIndex(border, 4);
        TimelineCanvas.Children.Add(border);
    }

    private static Border CreateTrimHandle(int column)
    {
        var handle = new Border
        {
            Width = TrimHandleWidth,
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = column == 0 ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0),
            Cursor = Cursors.SizeWE,
            ToolTip = column == 0 ? "拖动调整片段入点" : "拖动调整片段出点"
        };
        Grid.SetColumn(handle, column);
        return handle;
    }

    private ContextMenu BuildClipContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = ResourceBrush("WindowBackground", Colors.Black),
            Foreground = ResourceBrush("ButtonForeground", Colors.White),
            BorderBrush = ResourceBrush("MenuSeparator", Colors.DimGray),
            BorderThickness = new Thickness(1d)
        };
        menu.Items.Add(MenuItem("在播放头切割", (_, _) => SplitSelectedClip()));
        menu.Items.Add(MenuItem("复制片段", (_, _) => CopySelectedClip()));
        menu.Items.Add(MenuItem("移到轨道 1", (_, _) => MoveSelectedClipToTrack(0)));
        menu.Items.Add(MenuItem("移到轨道 2", (_, _) => MoveSelectedClipToTrack(1)));
        menu.Items.Add(MenuItem("删除片段", (_, _) => DeleteSelectedClip()));
        return menu;
    }

    private ContextMenu BuildTimelineContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = ResourceBrush("WindowBackground", Colors.Black),
            Foreground = ResourceBrush("ButtonForeground", Colors.White),
            BorderBrush = ResourceBrush("MenuSeparator", Colors.DimGray),
            BorderThickness = new Thickness(1d)
        };
        menu.Items.Add(MenuItem("在此设置 @start", (_, _) => RequestMarker("start", playhead)));
        menu.Items.Add(MenuItem("在此设置 @end", (_, _) => RequestMarker("end", playhead)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("清除 @start", (_, _) => RequestMarker("start", null)));
        menu.Items.Add(MenuItem("清除 @end", (_, _) => RequestMarker("end", null)));
        return menu;
    }

    private void RequestMarker(string name, double? time)
    {
        MarkerRequested?.Invoke(this, new MediaMarkerRequest(name, time));
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler action)
    {
        var item = new MenuItem { Header = header };
        item.Click += action;
        return item;
    }

    private void AddMarkerLine(double time, string label, Color color)
    {
        var x = Math.Max(0d, time) * pixelsPerSecond;
        var line = new Line
        {
            X1 = x,
            X2 = x,
            Y1 = 0d,
            Y2 = GetTimelineBottom(),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2d,
            Opacity = 0.9d,
            Cursor = Cursors.Hand
        };
        line.MouseLeftButtonDown += (_, e) =>
        {
            SetPlayhead(time, false);
            e.Handled = true;
        };
        Panel.SetZIndex(line, 8);
        TimelineCanvas.Children.Add(line);
        var text = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(3d),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock { Text = label, Foreground = Brushes.Black, FontSize = 9d },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(text, x + 3d);
        Canvas.SetTop(text, 15d);
        Panel.SetZIndex(text, 9);
        TimelineCanvas.Children.Add(text);
    }

    private void AddPlayhead()
    {
        var x = playhead * pixelsPerSecond;
        playheadLine = new Line
        {
            X1 = x,
            X2 = x,
            Y1 = 0d,
            Y2 = GetTimelineBottom(),
            Stroke = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            StrokeThickness = 2d,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(playheadLine, 10);
        TimelineCanvas.Children.Add(playheadLine);
        playheadHead = new Polygon
        {
            Points = new PointCollection
            {
                new(x - 6d, 0d), new(x + 6d, 0d), new(x, 9d)
            },
            Fill = playheadLine.Stroke,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(playheadHead, 10);
        TimelineCanvas.Children.Add(playheadHead);
    }

    private void Clip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: MediaTimelineClip clip } border)
            return;
        selectedClip = clip;
        Focus();
        Keyboard.Focus(this);
        var position = e.GetPosition(TimelineCanvas);
        var positionInClip = e.GetPosition(border).X;
        SetPlayhead(
            clip.TimelineStart + Math.Clamp(positionInClip / pixelsPerSecond, 0d, clip.Duration),
            false,
            false);
        if (e.ClickCount >= 2)
        {
            SplitSelectedClip();
            e.Handled = true;
            return;
        }

        draggedClip = clip;
        clipDragMode = positionInClip <= TrimHandleWidth
            ? ClipDragMode.TrimLeft
            : positionInClip >= border.ActualWidth - TrimHandleWidth
                ? ClipDragMode.TrimRight
                : ClipDragMode.Move;
        dragOrigin = position;
        dragOriginalStart = clip.TimelineStart;
        dragOriginalSourceOffset = clip.SourceOffset;
        dragOriginalDuration = clip.Duration;
        dragChanged = false;
        dragStarted = false;
        dragSnapshot = CaptureSnapshot();
        TimelineCanvas.CaptureMouse();
        RenderTimeline();
        e.Handled = true;
    }

    private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindClipFromSource(e.OriginalSource as DependencyObject) != null)
            return;
        Focus();
        Keyboard.Focus(this);
        playheadDragging = true;
        SetPlayhead(e.GetPosition(TimelineCanvas).X / pixelsPerSecond, true, false);
        TimelineCanvas.CaptureMouse();
        e.Handled = true;
    }

    private static MediaTimelineClip? FindClipFromSource(DependencyObject? source)
    {
        for (var current = source; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is Border { Tag: MediaTimelineClip clip })
                return clip;
        return null;
    }

    private void TimelineCanvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetPlayhead(e.GetPosition(TimelineCanvas).X / pixelsPerSecond, true);
    }

    private void TimelineCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        var position = e.GetPosition(TimelineCanvas);
        if (draggedClip != null)
        {
            if (!dragStarted)
            {
                var delta = position - dragOrigin;
                if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                    return;
                dragStarted = true;
            }
            switch (clipDragMode)
            {
                case ClipDragMode.TrimLeft:
                    TrimClipLeft(draggedClip, position.X / pixelsPerSecond);
                    break;
                case ClipDragMode.TrimRight:
                    TrimClipRight(draggedClip, position.X / pixelsPerSecond);
                    break;
                default:
                    var next = Snap(Math.Max(0d,
                        dragOriginalStart + (position.X - dragOrigin.X) / pixelsPerSecond));
                    dragChanged |= Math.Abs(next - draggedClip.TimelineStart) > 0.0001d;
                    draggedClip.TimelineStart = next;
                    var nextTrackIndex = ResolveTrackIndexFromPointer(draggedClip.Track, position.Y);
                    dragChanged |= nextTrackIndex != draggedClip.TrackIndex;
                    draggedClip.TrackIndex = nextTrackIndex;
                    break;
            }
            var element = TimelineCanvas.Children.OfType<Border>()
                .FirstOrDefault(border => ReferenceEquals(border.Tag, draggedClip));
            if (element != null)
            {
                Canvas.SetLeft(element, draggedClip.TimelineStart * pixelsPerSecond);
                Canvas.SetTop(element, GetLaneTop(draggedClip.Track, draggedClip.TrackIndex) + 5d);
                element.Width = Math.Max(12d, draggedClip.Duration * pixelsPerSecond);
            }
            SetPlayhead(clipDragMode == ClipDragMode.TrimRight
                ? draggedClip.TimelineEnd
                : draggedClip.TimelineStart, false, false, false);
            return;
        }
        if (playheadDragging)
            SetPlayhead(position.X / pixelsPerSecond, true, false);
    }

    private void TimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TimelineCanvas.ReleaseMouseCapture();
        if (draggedClip != null && dragChanged)
        {
            if (dragSnapshot != null)
                CommitEdit(dragSnapshot);
            SaveProject(clipDragMode == ClipDragMode.Move ? "已移动片段" : "已调整片段边缘");
        }
        draggedClip = null;
        dragSnapshot = null;
        clipDragMode = ClipDragMode.None;
        dragChanged = false;
        dragStarted = false;
        playheadDragging = false;
        if (selectedClip != null)
            RenderTimeline();
    }

    private void TrimClipLeft(MediaTimelineClip clip, double pointerTime)
    {
        var originalEnd = dragOriginalStart + dragOriginalDuration;
        var minimumStart = clip.IsStillImage
            ? 0d
            : Math.Max(0d, dragOriginalStart - dragOriginalSourceOffset);
        var nextStart = Math.Clamp(Snap(pointerTime), minimumStart, originalEnd - MinimumClipDuration);
        var delta = nextStart - dragOriginalStart;
        clip.TimelineStart = nextStart;
        clip.Duration = originalEnd - nextStart;
        clip.SourceOffset = clip.IsStillImage
            ? dragOriginalSourceOffset
            : Math.Max(0d, dragOriginalSourceOffset + delta);
        dragChanged |= Math.Abs(delta) > 0.0001d;
    }

    private void TrimClipRight(MediaTimelineClip clip, double pointerTime)
    {
        var minimumEnd = dragOriginalStart + MinimumClipDuration;
        var maximumEnd = clip.IsStillImage
            ? double.MaxValue
            : dragOriginalStart + Math.Max(
                MinimumClipDuration,
                clip.SourceDuration - dragOriginalSourceOffset);
        var nextEnd = Math.Clamp(Snap(pointerTime), minimumEnd, maximumEnd);
        clip.TimelineStart = dragOriginalStart;
        clip.SourceOffset = dragOriginalSourceOffset;
        clip.Duration = nextEnd - dragOriginalStart;
        dragChanged |= Math.Abs(clip.Duration - dragOriginalDuration) > 0.0001d;
    }

    private async void Timeline_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;
        e.Handled = true;
        var insertion = Math.Max(0d, e.GetPosition(TimelineCanvas).X / pixelsPerSecond);
        await AddFilesAsync(files, insertion);
    }

    private static bool TryGetTrackKind(string path, out MediaTrackKind track)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
        {
            track = MediaTrackKind.Video;
            return true;
        }
        if (extension is ".mp3" or ".ogg" or ".wav" or ".flac" or ".m4a" or ".aac")
        {
            track = MediaTrackKind.Audio;
            return true;
        }
        track = default;
        return false;
    }

    private void SplitSelectedClip()
    {
        if (selectedClip == null)
        {
            SetStatus("请先选择片段");
            return;
        }
        var split = Snap(playhead);
        if (split <= selectedClip.TimelineStart + 0.01d || split >= selectedClip.TimelineEnd - 0.01d)
        {
            SetStatus("播放头必须位于片段内部");
            return;
        }

        var snapshot = CaptureSnapshot();
        var leftDuration = split - selectedClip.TimelineStart;
        var right = selectedClip.Copy();
        right.TimelineStart = split;
        if (!right.IsStillImage)
            right.SourceOffset += leftDuration;
        right.Duration -= leftDuration;
        selectedClip.Duration = leftDuration;
        project.Clips.Add(right);
        selectedClip = right;
        CommitEdit(snapshot);
        SaveProject("已切割片段");
        RenderTimeline();
    }

    private void CopySelectedClip()
    {
        if (selectedClip == null)
        {
            SetStatus("请先选择片段");
            return;
        }
        copiedClip = selectedClip.Copy();
        SetStatus($"已复制 {selectedClip.Name}");
    }

    private void PasteCopiedClip()
    {
        if (copiedClip == null)
        {
            SetStatus("没有可粘贴的片段");
            return;
        }
        var snapshot = CaptureSnapshot();
        var pasted = copiedClip.Copy();
        pasted.TimelineStart = Snap(playhead);
        project.Clips.Add(pasted);
        selectedClip = pasted;
        CommitEdit(snapshot);
        SaveProject("已粘贴片段");
        RenderTimeline();
    }

    private void DeleteSelectedClip()
    {
        if (selectedClip == null)
        {
            SetStatus("请先选择片段");
            return;
        }
        var snapshot = CaptureSnapshot();
        project.Clips.Remove(selectedClip);
        selectedClip = null;
        CommitEdit(snapshot);
        SaveProject("已删除片段");
        RenderTimeline();
    }

    private void MoveSelectedClipToTrack(int trackIndex)
    {
        if (selectedClip == null)
        {
            SetStatus("请先选择片段");
            return;
        }
        var nextTrack = Math.Clamp(trackIndex, 0, 1);
        if (selectedClip.TrackIndex == nextTrack)
            return;
        var snapshot = CaptureSnapshot();
        selectedClip.TrackIndex = nextTrack;
        CommitEdit(snapshot);
        SaveProject($"已移动到{(selectedClip.Track == MediaTrackKind.Video ? "视频" : "音频")}轨道 {selectedClip.TrackIndex + 1}");
        RenderTimeline();
    }

    internal void SyncPlayhead(double value)
    {
        if (playheadDragging || draggedClip != null)
            return;
        playhead = Math.Max(0d, value);
        CursorText.Text = $"播放头 {FormatTime(playhead)}";
        UpdatePlayheadVisual();
    }

    private void SetPlayhead(double value, bool snap, bool render = true, bool notify = true)
    {
        playhead = Math.Max(0d, snap ? Snap(value) : value);
        CursorText.Text = $"播放头 {FormatTime(playhead)}";
        if (render)
            RenderTimeline();
        else
            UpdatePlayheadVisual();
        if (notify)
            PlayheadChanged?.Invoke(this, new MediaPlayheadRequest(playhead));
    }

    private void UpdatePlayheadVisual()
    {
        var x = playhead * pixelsPerSecond;
        if (playheadLine != null)
        {
            playheadLine.X1 = x;
            playheadLine.X2 = x;
        }
        if (playheadHead != null)
            playheadHead.Points = new PointCollection
            {
                new(x - 6d, 0d), new(x + 6d, 0d), new(x, 9d)
            };
    }

    private double Snap(double value)
    {
        value = Math.Max(0d, value);
        if (SnapCheck.IsChecked != true)
            return value;
        if (snapBeats.Count == 0)
            return Math.Round(value / fallbackBeatDuration) * fallbackBeatDuration;

        var index = snapBeats.BinarySearch(value);
        if (index >= 0)
            return snapBeats[index];
        index = ~index;
        if (index == 0)
            return snapBeats[0];
        if (index >= snapBeats.Count)
        {
            var last = snapBeats[^1];
            return last + Math.Round((value - last) / fallbackBeatDuration) * fallbackBeatDuration;
        }
        return value - snapBeats[index - 1] <= snapBeats[index] - value
            ? snapBeats[index - 1]
            : snapBeats[index];
    }

    private void SaveProject(string? status = null)
    {
        try
        {
            project.SaveTemporary(chartDirectory);
            hasPendingChanges = true;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            if (!string.IsNullOrWhiteSpace(status))
                SetStatus(status);
        }
        catch (Exception error)
        {
            SetStatus($"工程保存失败：{error.Message}");
        }
    }

    private void Timeline_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelectedClip();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopySelectedClip();
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            PasteCopiedClip();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Redo();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.None)
        {
            SplitSelectedClip();
            e.Handled = true;
        }
    }

    private void TimelineScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TimelineScroll.ScrollToHorizontalOffset(TimelineScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;
        var oldX = playhead * pixelsPerSecond;
        pixelsPerSecond = e.NewValue;
        RenderTimeline();
        var newX = playhead * pixelsPerSecond;
        TimelineScroll.ScrollToHorizontalOffset(Math.Max(0d,
            TimelineScroll.HorizontalOffset + newX - oldX));
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    internal bool CommitPendingChanges()
    {
        if (!hasPendingChanges)
            return true;
        try
        {
            project.Save(chartDirectory);
            committedSnapshot = CaptureSnapshot();
            hasPendingChanges = false;
            return true;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "媒体时间线保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    internal void DiscardPendingChanges()
    {
        if (!hasPendingChanges)
            return;
        MediaTimelineProject.DeleteTemporary(chartDirectory);
        project.Clips = committedSnapshot.Clips.Select(CloneClip).ToList();
        selectedClip = null;
        undoHistory.Clear();
        redoHistory.Clear();
        hasPendingChanges = false;
        RenderTimeline();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Split_Click(object sender, RoutedEventArgs e) => SplitSelectedClip();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelectedClip();
    private void Paste_Click(object sender, RoutedEventArgs e) => PasteCopiedClip();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelectedClip();

    private async void ExportAudio_Click(object sender, RoutedEventArgs e)
    {
        var optionsWindow = new MediaExportOptionsWindow(videoMode: false) { Owner = Window.GetWindow(this) };
        if (optionsWindow.ShowDialog() != true)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "导出时间线音频",
            Filter = "MP3 音频|*.mp3|WAV 音频|*.wav|OGG 音频|*.ogg",
            FileName = "timeline_audio.mp3",
            InitialDirectory = chartDirectory
        };
        if (dialog.ShowDialog() != true)
            return;
        await RunExportAsync(
            () => MediaTools.ExportTimelineAudioAsync(
                chartDirectory, dialog.FileName, optionsWindow.AudioOptions),
            "音频导出完成");
    }

    private async void ExportVideo_Click(object sender, RoutedEventArgs e)
    {
        var optionsWindow = new MediaExportOptionsWindow(videoMode: true) { Owner = Window.GetWindow(this) };
        if (optionsWindow.ShowDialog() != true)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "导出时间线视频",
            Filter = "MP4 视频|*.mp4",
            FileName = "timeline_video.mp4",
            InitialDirectory = chartDirectory
        };
        if (dialog.ShowDialog() != true)
            return;
        await RunExportAsync(
            () => MediaTools.ExportTimelineVideoAsync(
                chartDirectory, dialog.FileName, optionsWindow.VideoOptions),
            "视频导出完成");
    }

    private async Task RunExportAsync(Func<Task> export, string successMessage)
    {
        IsHitTestVisible = false;
        ExportProgress.Visibility = Visibility.Visible;
        SetStatus("正在导出...");
        try
        {
            await export();
            SetStatus(successMessage);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("导出失败");
        }
        finally
        {
            ExportProgress.Visibility = Visibility.Collapsed;
            IsHitTestVisible = true;
            Focus();
        }
    }

    private int ResolveTrackIndexFromPointer(MediaTrackKind kind, double y)
    {
        var firstCenter = GetLaneTop(kind, 0) + LaneHeight * 0.5d;
        var secondCenter = GetLaneTop(kind, 1) + LaneHeight * 0.5d;
        return Math.Abs(y - secondCenter) < Math.Abs(y - firstCenter) ? 1 : 0;
    }

    private TimelineSnapshot CaptureSnapshot() => new(
        project.Clips.Select(CloneClip).ToList(),
        selectedClip?.Id);

    private void CommitEdit(TimelineSnapshot snapshot)
    {
        undoHistory.Push(snapshot);
        redoHistory.Clear();
    }

    private void Undo()
    {
        if (undoHistory.Count == 0)
        {
            SetStatus("没有可撤销的操作");
            return;
        }
        redoHistory.Push(CaptureSnapshot());
        RestoreSnapshot(undoHistory.Pop(), "已撤销");
    }

    private void Redo()
    {
        if (redoHistory.Count == 0)
        {
            SetStatus("没有可重做的操作");
            return;
        }
        undoHistory.Push(CaptureSnapshot());
        RestoreSnapshot(redoHistory.Pop(), "已重做");
    }

    private void RestoreSnapshot(TimelineSnapshot snapshot, string status)
    {
        project.Clips = snapshot.Clips.Select(CloneClip).ToList();
        selectedClip = snapshot.SelectedId == null
            ? null
            : project.Clips.FirstOrDefault(clip => clip.Id == snapshot.SelectedId);
        SaveProject(status);
        RenderTimeline();
        Focus();
    }

    private static MediaTimelineClip CloneClip(MediaTimelineClip clip) => new()
    {
        Id = clip.Id,
        Track = clip.Track,
        TrackIndex = clip.TrackIndex,
        SourcePath = clip.SourcePath,
        Name = clip.Name,
        TimelineStart = clip.TimelineStart,
        SourceOffset = clip.SourceOffset,
        SourceDuration = clip.SourceDuration,
        Duration = clip.Duration,
        IsStillImage = clip.IsStillImage
    };

    private sealed record TimelineSnapshot(List<MediaTimelineClip> Clips, string? SelectedId);

    private void OnThemeChanged() => Dispatcher.Invoke(RenderTimeline);

    private void SetStatus(string message) => StatusText.Text = message;

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return value.TotalHours >= 1d
            ? value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static SolidColorBrush ResourceBrush(string key, Color fallback)
    {
        if (Application.Current.TryFindResource(key) is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush(fallback);
    }
}
