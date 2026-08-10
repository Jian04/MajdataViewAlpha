using System.IO;
using Newtonsoft.Json;

namespace MajdataEdit;

internal enum MediaTrackKind
{
    Video,
    Audio
}

internal sealed class MediaTimelineClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public MediaTrackKind Track { get; set; }
    public int TrackIndex { get; set; }
    public string SourcePath { get; set; } = "";
    public string Name { get; set; } = "";
    public double TimelineStart { get; set; }
    public double SourceOffset { get; set; }
    public double SourceDuration { get; set; }
    public double Duration { get; set; } = 1d;
    public bool IsStillImage { get; set; }

    [JsonIgnore]
    public double TimelineEnd => TimelineStart + Duration;

    public MediaTimelineClip Copy() => new()
    {
        Track = Track,
        TrackIndex = TrackIndex,
        SourcePath = SourcePath,
        Name = Name,
        TimelineStart = TimelineStart,
        SourceOffset = SourceOffset,
        SourceDuration = SourceDuration,
        IsStillImage = IsStillImage,
        Duration = Duration
    };
}

internal sealed class MediaTimelineProject
{
    public const string FileName = "media_timeline.json";
    public const string TemporaryFileName = FileName + ".tmp";

    public int Version { get; set; } = 1;
    public List<MediaTimelineClip> Clips { get; set; } = new();

    public static MediaTimelineProject Load(string chartDirectory)
    {
        return LoadFile(Path.Combine(chartDirectory, FileName)) ?? new MediaTimelineProject();
    }

    public static MediaTimelineProject LoadWorking(string chartDirectory)
    {
        return LoadFile(Path.Combine(chartDirectory, TemporaryFileName)) ?? Load(chartDirectory);
    }

    public static bool HasTemporaryFile(string chartDirectory) =>
        File.Exists(Path.Combine(chartDirectory, TemporaryFileName));

    private static MediaTimelineProject? LoadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var project = JsonConvert.DeserializeObject<MediaTimelineProject>(File.ReadAllText(path)) ?? new();
            project.Normalize();
            return project;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string chartDirectory)
    {
        if (string.IsNullOrWhiteSpace(chartDirectory) || !Directory.Exists(chartDirectory))
            return;

        Normalize();
        var path = Path.Combine(chartDirectory, FileName);
        var writePath = path + ".write";
        File.WriteAllText(writePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        File.Move(writePath, path, true);
        DeleteTemporary(chartDirectory);
    }

    public void SaveTemporary(string chartDirectory)
    {
        if (string.IsNullOrWhiteSpace(chartDirectory) || !Directory.Exists(chartDirectory))
            return;

        Normalize();
        var path = Path.Combine(chartDirectory, TemporaryFileName);
        var writePath = path + ".write";
        File.WriteAllText(writePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        File.Move(writePath, path, true);
    }

    public static void DeleteTemporary(string chartDirectory)
    {
        var path = Path.Combine(chartDirectory, TemporaryFileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public string ResolveSourcePath(string chartDirectory, MediaTimelineClip clip)
    {
        if (Path.IsPathRooted(clip.SourcePath))
            return clip.SourcePath;
        return Path.GetFullPath(Path.Combine(chartDirectory, clip.SourcePath));
    }

    public static string StoreSourcePath(string chartDirectory, string sourcePath)
    {
        var fullSource = Path.GetFullPath(sourcePath);
        var fullChart = Path.GetFullPath(chartDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullSource.StartsWith(fullChart, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(chartDirectory, fullSource)
            : fullSource;
    }

    private void Normalize()
    {
        Clips ??= new List<MediaTimelineClip>();
        foreach (var clip in Clips)
        {
            clip.Id = string.IsNullOrWhiteSpace(clip.Id) ? Guid.NewGuid().ToString("N") : clip.Id;
            clip.Name = string.IsNullOrWhiteSpace(clip.Name)
                ? Path.GetFileName(clip.SourcePath)
                : clip.Name;
            clip.TrackIndex = Math.Clamp(clip.TrackIndex, 0, 1);
            clip.TimelineStart = Math.Max(0d, FiniteOr(clip.TimelineStart, 0d));
            clip.SourceOffset = Math.Max(0d, FiniteOr(clip.SourceOffset, 0d));
            clip.Duration = Math.Max(0.01d, FiniteOr(clip.Duration, 1d));
            clip.SourceDuration = Math.Max(
                clip.SourceOffset + clip.Duration,
                FiniteOr(clip.SourceDuration, clip.SourceOffset + clip.Duration));
        }
    }

    private static double FiniteOr(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;
}
