using System.Text.RegularExpressions;
using MajdataCore;

namespace MajdataEdit;

/// <summary>
/// Asks the one question the other tools do not: the editor accepted this beat,
/// can the view actually build it?
/// </summary>
/// <remarks>
/// v0.4.2's view never validated anything - it read the note's own fields and
/// scanned characters for a shape. Validation in the build path is new, and a
/// rule that is right for a whole note can be wrong for the pieces the view
/// splits that note into: a connected slide is rebuilt as one note per segment,
/// and under total-duration syntax every segment but the last carries no
/// duration of its own. That mismatch is invisible to the parser diff, because
/// the parse is fine - the note dies later, while being built, which in a player
/// means it silently is not there.
///
/// So this walks the build path itself, in the order JsonDataLoader walks it, and
/// reports anything the editor accepted that the build would throw on.
/// </remarks>
internal static class ViewBuildDiff
{
    private static readonly Regex PrefabKey =
        new(@"^\s*\{""(?<key>[A-Za-z0-9]+)"",\s*\d+\s*\}", RegexOptions.Compiled);

    /// <summary>
    /// The prefab names the view can actually instantiate, read out of the view so
    /// the two cannot drift apart.
    /// </summary>
    public static HashSet<string> LoadPrefabKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var inMap = false;
        foreach (var line in File.ReadAllLines("Assets/Scripts/JsonDataLoader.cs"))
        {
            if (line.Contains("SLIDE_PREFAB_MAP = new Dictionary<string, int>()",
                    StringComparison.Ordinal))
            {
                inMap = true;
                continue;
            }
            if (!inMap)
                continue;
            if (line.Contains("};", StringComparison.Ordinal))
                break;
            var match = PrefabKey.Match(line);
            if (match.Success)
                keys.Add(match.Groups["key"].Value);
        }
        if (keys.Count == 0)
            throw new InvalidOperationException(
                "Could not read SLIDE_PREFAB_MAP out of JsonDataLoader.");
        return keys;
    }

    /// <summary>
    /// Everything the view does to a slide note between reading it and drawing it.
    /// Returns the reason it would be dropped, or null when it builds.
    /// </summary>
    public static int ConnectedSlidesSeen { get; private set; }
    public static int TouchSlidesSeen { get; private set; }
    public static int TotalDurationSlidesSeen { get; private set; }
    public static int PerSegmentDurationSlidesSeen { get; private set; }

    public static void ResetCoverage()
    {
        ConnectedSlidesSeen = 0;
        TouchSlidesSeen = 0;
        TotalDurationSlidesSeen = 0;
        PerSegmentDurationSlidesSeen = 0;
    }

    public static string? WhyViewCannotBuild(
        SimaiNote note,
        double currentBpm,
        HashSet<string> prefabKeys)
    {
        if (note.noteType != SimaiNoteType.Slide)
            return null;

        // JsonDataLoader.ResolveSlidePath, in its own order.
        List<SlidePathSegmentData> segments;
        var expression = !string.IsNullOrEmpty(note.pathExpression)
            ? note.pathExpression
            : note.slidePath is { Count: > 0 }
                ? null
                : note.noteContent;
        if (expression != null)
        {
            if (!SlidePathParser.TryParsePath(expression, out var path))
                return $"path cannot be parsed: '{expression}'";
            if (!SlideSyntaxValidator.TryValidate(path, out var error))
                return $"path rejected: {Flatten(error)} ('{expression}')";
            if (!NoteModifierParser.TryParse(expression, path.segments, out _))
                return $"modifiers rejected: '{expression}'";
            segments = path.segments;
        }
        else
        {
            segments = note.slidePath!;
            if (!SlideSyntaxValidator.TryValidateSegments(segments, out var error))
                return $"stored path rejected: {Flatten(error)}";
        }

        if (segments.Count == 0)
            return "path resolved to no segments";

        // A touch slide is drawn from geometry and needs two points, no prefab.
        if (note.isTouchSlide)
        {
            TouchSlidesSeen++;
            return null;
        }

        if (segments.Count > 1)
            ConnectedSlidesSeen++;

        // InstantiateStarGroup: one note per segment, each one built on its own.
        var durationCount = 0;
        foreach (var segment in segments)
        {
            if (!string.IsNullOrEmpty(segment.duration))
                durationCount++;

            if (!SlideShapeResolver.TryResolve(
                    segment, out var prefabKey, out _, out var shapeError))
                return $"shape unresolved: {Flatten(shapeError)} " +
                       $"('{segment.ToExpression(true)}')";
            var shape = prefabKey;
            if (shape.StartsWith("-", StringComparison.Ordinal))
                shape = shape.Substring(1);
            if (shape.StartsWith("r", StringComparison.Ordinal))
                shape = shape.Substring(1);
            if (!prefabKeys.Contains(shape))
                return $"no prefab named '{shape}' " +
                       $"('{segment.ToExpression(true)}')";

            // The segment is handed back as a note of its own, so whatever the
            // build does to it has to be legal for one segment.
            var lone = segment.ToExpression(includeDZone: true);
            if (!SlidePathParser.TryParsePath(lone, out var lonePath) ||
                lonePath.segments.Count != 1)
                return $"segment does not survive being written out: '{lone}'";
        }

        if (durationCount != 1 && durationCount != segments.Count)
            return $"{durationCount} duration(s) across {segments.Count} segment(s)";

        if (segments.Count > 1)
        {
            if (durationCount == 1)
                TotalDurationSlidesSeen++;
            else
                PerSegmentDurationSlidesSeen++;
        }

        if (durationCount != 1)
            foreach (var segment in segments)
                if (!SlideSyntaxValidator.TryGetLengthSeconds(
                        segment.duration, currentBpm, out _))
                    return $"segment duration unreadable: '{segment.duration}'";

        if (segments.Count > 1 &&
            (note.noteContent ?? string.Empty).Contains('w'))
            return "wifi cannot be part of a connected slide";

        return null;
    }

    private static string Flatten(string? text) =>
        (text ?? string.Empty).Replace('\n', ' ').Trim();

    public static int Run(string root, int fileLimit)
    {
        var prefabKeys = LoadPrefabKeys();
        var files = Directory
            .EnumerateFiles(root, "maidata.txt", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(fileLimit)
            .ToList();

        var drops = new SortedDictionary<string, (string Sample, int Count)>(
            StringComparer.Ordinal);
        var charts = 0;
        var beatsRead = 0;
        var slides = 0;

        foreach (var file in files)
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }
            foreach (var body in SplitDifficulties(text))
            {
                charts++;
                try { SimaiProcess.Serialize(body); }
                catch { continue; }
                foreach (var timing in SimaiProcess.notelist)
                {
                    if (!string.IsNullOrEmpty(timing.noteParseError))
                        continue;
                    List<SimaiNote> notes;
                    try { notes = timing.getNotes(); }
                    catch { continue; }
                    if (!string.IsNullOrEmpty(timing.noteParseError))
                        continue;
                    beatsRead++;
                    foreach (var note in notes)
                    {
                        if (note.noteType != SimaiNoteType.Slide)
                            continue;
                        slides++;
                        string? reason;
                        try
                        {
                            reason = WhyViewCannotBuild(
                                note, timing.currentBpm, prefabKeys);
                        }
                        catch (Exception e)
                        {
                            reason = "build threw: " + e.Message;
                        }
                        if (reason == null)
                            continue;
                        var key = Generalize(reason);
                        var sample = $"'{timing.notesContent}' -> {reason}";
                        if (drops.TryGetValue(key, out var existing))
                            drops[key] = (existing.Sample, existing.Count + 1);
                        else
                            drops[key] = (sample, 1);
                    }
                }
            }
        }

        // What it looked at matters as much as what it found: a run that never
        // reached a connected slide would report a clean corpus for free.
        Console.WriteLine(
            $"charts={charts} beats={beatsRead} slides={slides} " +
            $"connected={ConnectedSlidesSeen} totalDuration={TotalDurationSlidesSeen} " +
            $"perSegment={PerSegmentDurationSlidesSeen} " +
            $"touch={TouchSlidesSeen} drop kinds={drops.Count}");
        foreach (var (key, value) in drops)
        {
            Console.WriteLine($"  x{value.Count,-6} {key}");
            Console.WriteLine($"          e.g. {value.Sample}");
        }
        if (drops.Count == 0)
            Console.WriteLine(
                "  every slide the editor accepts can be built by the view.");
        return drops.Count == 0 ? 0 : 1;
    }

    // Group by the shape of the failure, not the note that hit it.
    private static string Generalize(string reason)
    {
        var cut = reason.IndexOf(" ('", StringComparison.Ordinal);
        return cut < 0 ? reason : reason.Substring(0, cut);
    }

    private static IEnumerable<string> SplitDifficulties(string text)
    {
        var body = new System.Text.StringBuilder();
        var collecting = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("&", StringComparison.Ordinal))
            {
                if (collecting && body.Length > 0)
                    yield return body.ToString();
                body.Clear();
                collecting = raw.StartsWith("&inote_", StringComparison.Ordinal);
                if (collecting)
                {
                    var eq = raw.IndexOf('=');
                    if (eq >= 0)
                        body.AppendLine(raw.Substring(eq + 1));
                }
                continue;
            }
            if (collecting)
                body.AppendLine(raw);
        }
        if (collecting && body.Length > 0)
            yield return body.ToString();
    }
}
