using System.IO;
using Un4seen.Bass;

namespace MajdataEdit;

public partial class MainWindow
{
    private const int TimelineWaveSamplesPerSecond = 1000;
    private readonly object timelineWaveCacheLock = new();
    private readonly Dictionary<string, DecodedTimelineWave> timelineWaveCache = new(StringComparer.OrdinalIgnoreCase);
    private int timelineWaveBuildGeneration;
    private double waveformDisplayLength = double.NaN;
    private string? waveformSourcePath;

    private bool CommitChartMediaChanges()
    {
        if (MediaTimelinePanel.IsConfiguredFor(maidataDir)) return MediaTimelinePanel.CommitPendingChanges();
        if (!MediaTimelineProject.HasTemporaryFile(maidataDir)) return true;
        try
        {
            var path = Path.Combine(maidataDir, MediaTimelineProject.TemporaryFileName);
            var project = Newtonsoft.Json.JsonConvert.DeserializeObject<MediaTimelineProject>(File.ReadAllText(path))
                ?? throw new InvalidDataException("剪辑草稿为空：" + path);
            project.Save(maidataDir);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            System.Windows.MessageBox.Show(this, error.Message, "保存剪辑", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    private void DiscardChartMediaChanges()
    {
        if (MediaTimelinePanel.IsConfiguredFor(maidataDir)) { MediaTimelinePanel.DiscardPendingChanges(); return; }
        if (!MediaTimelineProject.HasTemporaryFile(maidataDir)) return;
        MediaTimelineProject.DeleteTemporary(maidataDir);
        QueueTimelineAudioRefresh();
        QueueMediaTimelineWaveformRefreshFromDisk();
    }

    private List<MediaChange> GetEffectiveMediaTable()
    {
        var syntaxEvents = SimaiProcess.mediaTable.ToList();
        if (string.IsNullOrWhiteSpace(maidataDir) || !Directory.Exists(maidataDir))
            return syntaxEvents;
        var project = MediaTimelineProject.LoadWorking(maidataDir);
        if (project.Clips.Count == 0)
            return syntaxEvents;

        var defaultVideoPassThrough = IsDefaultVideoPassThrough(project);
        // The timeline supplies BGM; AUDIO commands remain a separate overlay.
        var events = syntaxEvents;
        foreach (var clip in project.Clips)
        {
            if (defaultVideoPassThrough && clip.Track == MediaTrackKind.Video)
                continue;
            var fullPath = project.ResolveSourcePath(maidataDir, clip);
            if (!File.Exists(fullPath))
                continue;
            var relativePath = Path.GetRelativePath(maidataDir, fullPath);
            if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
                continue;
            var kind = clip.Track == MediaTrackKind.Video ? "pvOverlay" : "audio";
            events.Add(new MediaChange
            {
                time = clip.TimelineStart,
                kind = kind,
                enabled = true,
                path = relativePath.Replace('\\', '/'),
                track = clip.TrackIndex,
                sourceOffset = clip.SourceOffset,
                duration = clip.Duration,
                timelineClip = true,
                opacity = clip.Opacity,
                brightness = clip.Brightness
            });
            events.Add(new MediaChange
            {
                time = clip.TimelineEnd,
                kind = kind,
                enabled = false,
                track = clip.TrackIndex,
                timelineClip = true
            });
        }
        return events.OrderBy(item => item.time).ThenBy(item => item.enabled ? 1 : 0).ToList();
    }

    private bool IsDefaultVideoPassThrough(MediaTimelineProject project)
    {
        var clips = project.Clips.Where(clip => clip.Track == MediaTrackKind.Video).ToList();
        if (clips.Count != 1)
            return false;

        var clip = clips[0];
        var fullPath = project.ResolveSourcePath(maidataDir, clip);
        var isDefaultFile = fullPath.Equals(MajdataCore.ChartCover.FindBackground(maidataDir), StringComparison.OrdinalIgnoreCase);
        return isDefaultFile && clip.TrackIndex == 0 && clip.TimelineStart <= 0.0001d &&
               clip.SourceOffset <= 0.0001d && clip.Duration + 0.02d >= clip.SourceDuration;
    }

    private void QueueTimelineAudioRefresh()
    {
        if (string.IsNullOrWhiteSpace(maidataDir) || !Directory.Exists(maidataDir))
            return;

        var preservedPosition = MediaTimelinePanel.Visibility ==
                                System.Windows.Visibility.Visible
            ? MediaTimelinePanel.CurrentPlayhead
            : GetTimelinePosition();
        var generation = Interlocked.Increment(ref timelineAudioBuildGeneration);
        var task = MediaTools.BuildTimelineAudioAsync(maidataDir);
        timelineAudioBuildTask = task;
        _ = ApplyTimelineAudioBuildAsync(task, generation, preservedPosition);
    }

    private async Task ApplyTimelineAudioBuildAsync(
        Task<string?> task,
        int generation,
        double preservedPosition)
    {
        string? path;
        try
        {
            path = await task;
        }
        catch
        {
            // Playback awaits this same task and reports a failed mix instead
            // of silently playing the unedited source.
            return;
        }

        if (generation != timelineAudioBuildGeneration)
            return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (generation == timelineAudioBuildGeneration && !isPlaying)
                SwitchTimelineAudioSource(path, preservedPosition);
        });
    }

    private async Task<bool> EnsureTimelineAudioReadyAsync()
    {
        var task = timelineAudioBuildTask;
        if (task == null)
            return true;

        var path = await task;
        if (!ReferenceEquals(task, timelineAudioBuildTask)) return false;
        SwitchTimelineAudioSource(path);
        return true;
    }

    private void SwitchTimelineAudioSource(
        string? timelinePath,
        double? preservedPosition = null)
    {
        var originalPath = GetOriginalTrackPath();
        var targetPath = !string.IsNullOrWhiteSpace(timelinePath) && File.Exists(timelinePath)
            ? timelinePath
            : originalPath;
        if (targetPath == null)
            return;

        if (!string.IsNullOrWhiteSpace(loadedTrackPath) &&
            Path.GetFullPath(loadedTrackPath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            MediaTools.PruneTimelineAudioCache(maidataDir, targetPath);
            return;
        }

        var position = preservedPosition ?? (bgmStream > 0
            ? Bass.BASS_ChannelBytes2Seconds(
                bgmStream, Bass.BASS_ChannelGetPosition(bgmStream))
            : 0d);
        // The waveform timer can read the channel while the replacement stream is
        // being decoded. Keep an explicit cursor until the new channel is ready so
        // the playhead never renders one frame at zero.
        flowTimelineCursor = position;
        var volume = editorSetting?.Default_BGM_Level ?? 1f;
        var tempo = 0f;
        if (bgmStream > 0)
        {
            Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, ref volume);
            Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_TEMPO, ref tempo);
            Bass.BASS_ChannelStop(bgmStream);
            Bass.BASS_StreamFree(bgmStream);
            bgmStream = -1024;
        }

        timelineAudioSourcePath = Path.GetFullPath(targetPath).Equals(
            Path.GetFullPath(originalPath!), StringComparison.OrdinalIgnoreCase)
            ? null
            : targetPath;
        ReloadCurrentTrack(position, volume, tempo);
        MediaTools.PruneTimelineAudioCache(maidataDir, targetPath);
        MediaTimelinePanel.SyncPlayhead(position);
    }

    private void MediaTimelinePanel_PlayheadChanged(object? sender, MediaPlayheadRequest request)
    {
        CancelNotePreview();
        PrepareTimelineDrag();
        SetTimelinePosition(request.Time);
        SimaiProcess.ClearNoteListPlayedState();
        if (FollowPlayCheck.IsChecked == true && request.Time >= 0d && request.Time <= songLength)
            SeekTextFromTime();
        DrawWave();
    }

    private double GetCurrentBeatDuration()
    {
        try
        {
            return GetBeatDuration(1d);
        }
        catch
        {
            return 0.5d;
        }
    }

    private async void MediaTimelinePanel_ProjectChanged(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(maidataDir) || !MediaTimelinePanel.IsConfiguredFor(maidataDir)) return;
        if (MediaTimelinePanel.HasPendingChanges)
            SetSavedState(false);
        var audioClips = MediaTimelinePanel.GetResolvedClips(MediaTrackKind.Audio);
        var videoClips = MediaTimelinePanel.GetResolvedClips(MediaTrackKind.Video);
        var projectEnd = audioClips.Concat(videoClips)
            .Select(clip => clip.TimelineEnd)
            .DefaultIfEmpty(songLength)
            .Max();
        BuildWaveBeatLines(Math.Max(songLength, projectEnd), out var strongBeats, out var weakBeats);
        double beatDuration;
        try
        {
            beatDuration = GetBeatDuration(1d);
        }
        catch
        {
            beatDuration = 0.5d;
        }
        MediaTimelinePanel.RefreshChartState(
            songLength,
            strongBeats,
            weakBeats,
            beatDuration,
            SimaiProcess.mediaTrimStart,
            SimaiProcess.mediaTrimEnd);
        var visualEnd = videoClips.Select(clip => clip.TimelineEnd).DefaultIfEmpty(songLength).Max();
        QueueTimelineAudioRefresh();
        if (pausedTimelinePreviewActive || pausedTimelinePreviewRequested)
        {
            pausedTimelinePreviewNeedsReload = true;
            QueueNotePreview();
        }
        await RebuildTimelineWaveformAsync(audioClips, visualEnd);
    }

    private void QueueMediaTimelineWaveformRefreshFromDisk()
    {
        if (string.IsNullOrWhiteSpace(maidataDir) || !Directory.Exists(maidataDir))
            return;
        var projectPath = Path.Combine(maidataDir, MediaTimelineProject.FileName);
        if (!File.Exists(projectPath) && !MediaTimelineProject.HasTemporaryFile(maidataDir))
        {
            waveformDisplayLength = songLength;
            MediaTools.PruneTimelineAudioCache(maidataDir, loadedTrackPath);
            return;
        }

        var project = MediaTimelineProject.LoadWorking(maidataDir);
        var visualEnd = project.Clips
            .Where(clip => clip.Track == MediaTrackKind.Video)
            .Select(clip => clip.TimelineEnd)
            .DefaultIfEmpty(songLength)
            .Max();
        var clips = project.Clips
            .Where(clip => clip.Track == MediaTrackKind.Audio)
            .Select(clip =>
            {
                var copy = clip.Copy();
                copy.SourcePath = project.ResolveSourcePath(maidataDir, clip);
                return copy;
            })
            .ToList();
        _ = RebuildTimelineWaveformAsync(clips, visualEnd);
        QueueTimelineAudioRefresh();
    }

    private async Task RebuildTimelineWaveformAsync(
        IReadOnlyList<MediaTimelineClip> clips,
        double? requestedLength = null)
    {
        var generation = Interlocked.Increment(ref timelineWaveBuildGeneration);
        var chartLength = Math.Max(0.1d, Math.Max(songLength, requestedLength ?? 0d));
        if (clips.Count == 1 && waveformSourcePath != null && chartLength <= songLength &&
            Path.GetFullPath(clips[0].SourcePath).Equals(Path.GetFullPath(waveformSourcePath), StringComparison.OrdinalIgnoreCase) &&
            clips[0].TimelineStart <= 0.0001d && clips[0].SourceOffset <= 0.0001d &&
            clips[0].Duration + 0.02d >= songLength && Math.Abs(clips[0].Volume - 1d) <= 0.0001d &&
            clips[0].FadeIn <= 0.0001d && clips[0].FadeOut <= 0.0001d)
        {
            waveformDisplayLength = songLength;
            return;
        }
        TimelineWaveResult result;
        try
        {
            result = await Task.Run(() => BuildTimelineWaveform(clips, chartLength));
        }
        catch
        {
            return;
        }
        if (generation != timelineWaveBuildGeneration)
            return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (generation != timelineWaveBuildGeneration)
                return;
            waveformSourcePath = null;
            waveRaws[0] = result.Full;
            waveRaws[1] = result.Medium;
            waveRaws[2] = result.Low;
            waveformDisplayLength = result.Duration;
            UpdateWaveformBars(result.Duration);
            densityAudioEnvelopeSource = null;
            densityAudioEnvelope = Array.Empty<float>();
            DrawWave();
        });
    }

    private TimelineWaveResult BuildTimelineWaveform(
        IReadOnlyList<MediaTimelineClip> clips,
        double chartLength)
    {
        var preparedClips = new List<(MediaTimelineClip Clip, DecodedTimelineWave Source, int SourceStart, int Length)>();
        var duration = chartLength;
        foreach (var clip in clips)
        {
            if (!File.Exists(clip.SourcePath) || clip.Duration <= 0d)
                continue;
            var source = GetDecodedTimelineWave(clip.SourcePath);
            if (source == null)
                continue;
            var sourceStart = Math.Max(0, (int)Math.Round(clip.SourceOffset * TimelineWaveSamplesPerSecond));
            var requestedLength = Math.Max(0, (int)Math.Round(clip.Duration * TimelineWaveSamplesPerSecond));
            var length = Math.Min(requestedLength, Math.Max(0, source.Samples.Length - sourceStart));
            if (length <= 0)
                continue;
            preparedClips.Add((clip, source, sourceStart, length));
            duration = Math.Max(duration, clip.TimelineStart + length / (double)TimelineWaveSamplesPerSecond);
        }
        var sampleCount = Math.Max(1, checked((int)Math.Ceiling(duration * TimelineWaveSamplesPerSecond)));
        var mixed = new int[sampleCount];

        foreach (var prepared in preparedClips)
        {
            var clip = prepared.Clip;
            var destinationStart = Math.Max(0, (int)Math.Round(clip.TimelineStart * TimelineWaveSamplesPerSecond));
            var sourceStart = prepared.SourceStart;
            var length = prepared.Length;
            length = Math.Min(length, mixed.Length - destinationStart);
            for (var i = 0; i < length; i++)
            {
                var elapsed = i / (double)TimelineWaveSamplesPerSecond;
                var gain = clip.Volume;
                if (clip.FadeIn > 0d) gain *= Math.Clamp(elapsed / clip.FadeIn, 0d, 1d);
                if (clip.FadeOut > 0d)
                {
                    var fadeStart = Math.Max(0d, clip.Duration - clip.FadeOut);
                    if (elapsed >= fadeStart)
                        gain *= Math.Clamp(1d - (elapsed - fadeStart) / clip.FadeOut, 0d, 1d);
                }
                mixed[destinationStart + i] += (int)Math.Round(prepared.Source.Samples[sourceStart + i] * gain);
            }
        }

        var full = new short[mixed.Length];
        for (var i = 0; i < mixed.Length; i++)
            full[i] = (short)Math.Clamp(mixed[i], short.MinValue, short.MaxValue);
        return new TimelineWaveResult(full, ReduceWave(full, 3), ReduceWave(full, 6), duration);
    }

    private DecodedTimelineWave? GetDecodedTimelineWave(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var stamp = File.GetLastWriteTimeUtc(fullPath).Ticks;
        var key = fullPath + "|" + stamp;
        lock (timelineWaveCacheLock)
        {
            if (timelineWaveCache.TryGetValue(key, out var cached))
                return cached;
        }

        var decoded = DecodeTimelineWave(fullPath);
        if (decoded == null)
            return null;
        lock (timelineWaveCacheLock)
        {
            foreach (var stale in timelineWaveCache.Keys
                         .Where(cacheKey => cacheKey.StartsWith(fullPath + "|", StringComparison.OrdinalIgnoreCase))
                         .ToList())
                timelineWaveCache.Remove(stale);
            timelineWaveCache[key] = decoded;
        }
        return decoded;
    }

    private static DecodedTimelineWave? DecodeTimelineWave(string path)
    {
        var durationStream = Bass.BASS_StreamCreateFile(
            path,
            0L,
            0L,
            BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_STREAM_PRESCAN);
        if (durationStream == 0)
            return null;
        double duration;
        try
        {
            duration = Bass.BASS_ChannelBytes2Seconds(
                durationStream,
                Bass.BASS_ChannelGetLength(durationStream, BASSMode.BASS_POS_BYTE));
        }
        finally
        {
            Bass.BASS_StreamFree(durationStream);
        }
        if (!double.IsFinite(duration) || duration <= 0d)
            return null;

        var sample = Bass.BASS_SampleLoad(path, 0L, 0, 1, BASSFlag.BASS_DEFAULT);
        if (sample == 0)
            return null;
        try
        {
            var info = Bass.BASS_SampleGetInfo(sample);
            var channels = Math.Max(1, info.chans);
            if (info.freq <= 0)
                return null;

            var rawCountLong = (long)Math.Ceiling(duration * info.freq * channels);
            if (rawCountLong <= 0 || rawCountLong > int.MaxValue)
                return null;
            var raw = new short[(int)rawCountLong];
            if (!Bass.BASS_SampleGetData(sample, raw))
                return null;

            var output = new short[Math.Max(1,
                (int)Math.Ceiling(duration * TimelineWaveSamplesPerSecond))];
            var framesPerOutput = Math.Max(1d, info.freq / (double)TimelineWaveSamplesPerSecond);
            for (var i = 0; i < output.Length; i++)
            {
                var firstFrame = Math.Max(0, (int)Math.Floor(i * framesPerOutput));
                var lastFrame = Math.Min((int)(raw.LongLength / channels),
                    Math.Max(firstFrame + 1, (int)Math.Ceiling((i + 1) * framesPerOutput)));
                short peak = 0;
                var peakMagnitude = -1;
                for (var frame = firstFrame; frame < lastFrame; frame++)
                {
                    var sum = 0;
                    for (var channel = 0; channel < channels; channel++)
                        sum += raw[frame * channels + channel];
                    var value = (short)(sum / channels);
                    var magnitude = Math.Abs((int)value);
                    if (magnitude <= peakMagnitude)
                        continue;
                    peakMagnitude = magnitude;
                    peak = value;
                }
                output[i] = peak;
            }
            return new DecodedTimelineWave(output);
        }
        finally
        {
            Bass.BASS_SampleFree(sample);
        }
    }

    private static short[] ReduceWave(short[] source, int factor)
    {
        var output = new short[Math.Max(1, (source.Length + factor - 1) / factor)];
        for (var i = 0; i < output.Length; i++)
        {
            var start = i * factor;
            var end = Math.Min(source.Length, start + factor);
            short peak = 0;
            var magnitude = -1;
            for (var j = start; j < end; j++)
            {
                var nextMagnitude = Math.Abs((int)source[j]);
                if (nextMagnitude <= magnitude)
                    continue;
                magnitude = nextMagnitude;
                peak = source[j];
            }
            output[i] = peak;
        }
        return output;
    }

    private sealed record DecodedTimelineWave(short[] Samples);
    private sealed record TimelineWaveResult(short[] Full, short[] Medium, short[] Low, double Duration);
}
