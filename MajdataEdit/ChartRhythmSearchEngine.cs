using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MajdataCore;

namespace MajdataEdit;

internal static class ChartRhythmSearchEngine
{
    private const int Resolution = 2880;
    private static readonly Regex WhiteSpace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex BpmRegex = new(@"\((\d+(?:\.\d+)?)\)", RegexOptions.Compiled);
    private static readonly Regex DivRegex = new(@"^\{(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new(@"&title=(.+?)(?=\n&|\z)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ArtistRegex = new(@"&artist=(.+?)(?=\n&|\z)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex SectionRegex = new(@"&inote_(\d+)=(.*?)(?=\n&[a-z_]|\z)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LevelRegex = new(@"&lv_(\d+)=(.+?)(?=\n&|\z)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HasNoteRegex = new(@"[1-8A-E]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HasNote0Regex = new(@"[0-8A-E]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> DifficultyNames = new()
    {
        ["1"] = "Easy",
        ["2"] = "Basic",
        ["3"] = "Advanced",
        ["4"] = "Expert",
        ["5"] = "Master",
        ["6"] = "Re:Master",
        ["7"] = "Original"
    };

    public static QueryPattern ParseQuery(string text, bool exact)
    {
        var (tokens, _) = Tokenize(text);
        if (tokens.Count == 0)
            return new QueryPattern(new HashSet<int>(), 0, exact ? new Dictionary<int, HashSet<int>>() : null);

        var timeline = BuildTimeline(tokens, exact);
        var noteTicks = new HashSet<int>();
        var positions = exact ? new Dictionary<int, HashSet<int>>() : null;
        foreach (var item in timeline.Items)
        {
            if (!item.HasNote)
                continue;

            noteTicks.Add(item.Tick);
            if (!exact || positions == null)
                continue;

            var pos = GetQueryPositions(tokens[item.TokenIndex]);
            if (pos.Count > 0)
                positions[item.Tick] = pos;
        }

        return new QueryPattern(noteTicks, timeline.TotalTicks, positions);
    }

    public static IReadOnlyList<RhythmSearchResult> Search(
        string root,
        string query,
        bool exact,
        bool fuzzy,
        ISet<string>? selectedDifficulties,
        CancellationToken cancellationToken,
        IProgress<RhythmSearchProgress>? progress = null)
    {
        var pattern = ParseQuery(query, exact);
        if (pattern.TotalTicks <= 0)
            return Array.Empty<RhythmSearchResult>();

        var files = Directory.EnumerateFiles(root, "maidata.txt", SearchOption.AllDirectories).ToList();
        var results = new List<RhythmSearchResult>();
        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[fileIndex];
            progress?.Report(new RhythmSearchProgress(fileIndex + 1, files.Count, file));
            var chart = LoadChart(file);
            if (chart == null)
                continue;

            foreach (var diff in chart.Difficulties)
            {
                if (selectedDifficulties != null && selectedDifficulties.Count > 0 &&
                    !selectedDifficulties.Contains(diff.Key))
                    continue;

                var matches = SearchDifficulty(diff.Value, pattern, fuzzy);
                if (matches.Count == 0)
                    continue;

                results.Add(new RhythmSearchResult
                {
                    File = file,
                    Directory = Path.GetDirectoryName(file) ?? "",
                    Title = chart.Title,
                    Artist = chart.Artist,
                    Difficulty = DifficultyNames.TryGetValue(diff.Key, out var name) ? name : $"Diff {diff.Key}",
                    DifficultyNumber = diff.Key,
                    Level = chart.Levels.TryGetValue(diff.Key, out var level) ? level : "",
                    Matches = matches
                });
            }
        }

        return results
            .OrderByDescending(r => r.Matches.Count)
            .ThenByDescending(r => r.SortDensity)
            .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<RhythmMatch> SearchDifficulty(ParsedDifficulty chart, QueryPattern pattern, bool fuzzy)
    {
        var matches = new List<RhythmMatch>();
        var timeline = chart.Timeline;
        var n = timeline.Count;
        for (var start = 0; start < n; start++)
        {
            var winStart = timeline[start].Tick;
            var winEnd = winStart + pattern.TotalTicks;
            if (!chart.BoundaryTicks.Contains(winEnd))
                continue;

            var windowTicks = new HashSet<int>();
            var windowPositions = pattern.Positions == null ? null : new Dictionary<int, HashSet<int>>();
            for (var j = start; j < n; j++)
            {
                var item = timeline[j];
                if (item.Tick >= winEnd)
                    break;
                if (!item.HasNote)
                    continue;

                var rel = item.Tick - winStart;
                windowTicks.Add(rel);
                if (windowPositions != null)
                    windowPositions[rel] = GetChartPositions(chart.Tokens[item.TokenIndex]);
            }

            if (fuzzy)
            {
                if (!pattern.NoteTicks.IsSubsetOf(windowTicks))
                    continue;
            }
            else if (!windowTicks.SetEquals(pattern.NoteTicks))
            {
                continue;
            }

            if (pattern.Positions != null)
            {
                var ok = true;
                foreach (var pair in pattern.Positions)
                {
                    if (windowPositions == null ||
                        !windowPositions.TryGetValue(pair.Key, out var chartPos) ||
                        chartPos.Count == 0)
                    {
                        ok = false;
                        break;
                    }

                    if (!pair.Value.Contains(0) && !chartPos.SetEquals(pair.Value))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok && !fuzzy)
                {
                    foreach (var rel in windowTicks)
                    {
                        if (!pattern.Positions.ContainsKey(rel))
                        {
                            ok = false;
                            break;
                        }
                    }
                }

                if (!ok)
                    continue;
            }

            var endIndex = FindEndIndex(timeline, winEnd);
            var ctxStart = Math.Max(0, start - 4);
            var ctxEnd = Math.Min(n, endIndex + 4);
            var match = string.Join(",", chart.Tokens.Skip(start).Take(endIndex - start));
            var endTime = endIndex < chart.SecondsMap.Count ? chart.SecondsMap[endIndex] : chart.SecondsMap[^1];
            var duration = Math.Max(0.001, endTime - chart.SecondsMap[start]);
            var combo = CountCombo(match);

            matches.Add(new RhythmMatch
            {
                Before = string.Join(",", chart.Tokens.Skip(ctxStart).Take(start - ctxStart)),
                Match = match,
                After = string.Join(",", chart.Tokens.Skip(endIndex).Take(ctxEnd - endIndex)),
                Bpm = FindBpm(chart.BpmList, start),
                TimeSeconds = chart.SecondsMap[start],
                EndSeconds = endTime,
                Combo = combo,
                ComboBefore = chart.CumulativeCombo[start],
                StarCount = CountStars(match),
                Density = combo / duration
            });
        }

        return matches;
    }

    private static ParsedChart? LoadChart(string path)
    {
        var mtime = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out var entry) && entry.LastWriteTime == mtime)
            return entry.Chart;

        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var chart = ParseChart(text);
            if (chart != null)
                Cache[path] = new CacheEntry(mtime, chart);
            return chart;
        }
        catch
        {
            return null;
        }
    }

    private static ParsedChart? ParseChart(string content)
    {
        var title = MatchValue(TitleRegex, content, "Unknown");
        var artist = MatchValue(ArtistRegex, content, "");
        var levels = LevelRegex.Matches(content)
            .Cast<Match>()
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        var difficulties = new Dictionary<string, ParsedDifficulty>();

        foreach (Match match in SectionRegex.Matches(content))
        {
            var diff = match.Groups[1].Value;
            var chartText = match.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(chartText))
                continue;

            var (tokens, bpmList) = Tokenize(chartText);
            if (tokens.Count == 0)
                continue;

            var timeline = BuildTimeline(tokens, includeZero: false, fromChart: true);
            var secondsMap = BuildSecondsMap(tokens, bpmList);
            var boundaries = timeline.Items.Select(i => i.Tick).Append(timeline.TotalTicks).ToHashSet();
            var cumulative = new int[tokens.Count + 1];
            for (var i = 0; i < tokens.Count; i++)
                cumulative[i + 1] = cumulative[i] + CountComboToken(tokens[i]);

            difficulties[diff] = new ParsedDifficulty(tokens, bpmList, timeline.Items, timeline.TotalTicks, secondsMap, boundaries, cumulative);
        }

        return difficulties.Count == 0 ? null : new ParsedChart(title, artist, levels, difficulties);
    }

    // An Alpha command may hold commas in its arguments, as <Shake*(1,2,8:1)>
    // does. Splitting those would turn one command into several slots and move
    // every Note after it, so commands are stepped over the way the chart parser
    // steps over them.
    internal static List<string> SplitSlots(string text)
    {
        var parts = new List<string>();
        var slot = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' &&
                AlphaCommandBoundary.TryGetCommand(text, i, out var close))
            {
                slot.Append(text, i, close - i + 1);
                i = close;
                continue;
            }

            if (text[i] == ',')
            {
                parts.Add(slot.ToString());
                slot.Clear();
                continue;
            }

            slot.Append(text[i]);
        }

        parts.Add(slot.ToString());
        return parts;
    }

    private static (List<string> Tokens, List<double?> BpmList) Tokenize(string text)
    {
        var raw = SplitSlots(WhiteSpace.Replace(text, ""));
        var tokens = new List<string>();
        var bpmList = new List<double?>();
        double? currentBpm = null;

        foreach (var rawToken in raw)
        {
            if (rawToken == "E")
                continue;

            // A command carries numbers of its own, so the BPM marker is read
            // from what is left once the commands are gone.
            var bpmMatch = BpmRegex.Match(RhythmBody(rawToken));
            if (bpmMatch.Success && double.TryParse(bpmMatch.Groups[1].Value, out var bpm))
                currentBpm = bpm;

            tokens.Add(rawToken);
            bpmList.Add(currentBpm);
        }

        while (tokens.Count > 0 && tokens[^1] == "")
        {
            tokens.RemoveAt(tokens.Count - 1);
            bpmList.RemoveAt(bpmList.Count - 1);
        }

        return (tokens, bpmList);
    }

    private static TimelineResult BuildTimeline(
        IReadOnlyList<string> tokens, bool includeZero, bool fromChart = false)
    {
        var currentInv = 4;
        var currentTick = 0;
        var items = new List<TimelineItem>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var (newInv, tickDur) = TokenToInv(RhythmBody(tokens[i]), currentInv);
            var has = fromChart
                ? ChartSlotHasNote(tokens[i])
                : HasNote(StripDiv(tokens[i]), includeZero);
            items.Add(new TimelineItem(currentTick, has, tickDur, i));
            currentInv = newInv;
            currentTick += tickDur;
        }

        return new TimelineResult(items, currentTick);
    }

    private static List<double> BuildSecondsMap(IReadOnlyList<string> tokens, IReadOnlyList<double?> bpmList)
    {
        var currentInv = 4;
        var currentTime = 0d;
        var times = new List<double>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var (newInv, _) = TokenToInv(RhythmBody(tokens[i]), currentInv);
            times.Add(currentTime);
            var bpm = bpmList[i] ?? 120d;
            currentTime += 240d / (newInv * bpm);
            currentInv = newInv;
        }

        return times;
    }

    private static (int NewInv, int TickDur) TokenToInv(string token, int currentInv)
    {
        if (!token.Contains('{'))
            return (currentInv, Resolution / currentInv);

        var inv = currentInv;
        var remaining = token;
        while (true)
        {
            var match = DivRegex.Match(remaining);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out inv))
                break;
            remaining = remaining[match.Length..];
        }

        return (inv, Resolution / inv);
    }

    private static bool HasNote(string body, bool includeZero) =>
        (includeZero ? HasNote0Regex : HasNoteRegex).IsMatch(body);

    // An Alpha command carries letters and digits of its own, so a slot holding
    // only <SV*slide=1.5> used to look like a slot holding notes.
    internal static bool ChartSlotHasNote(string token) =>
        SlotNotes(token).Any(entry => HasNoteRegex.IsMatch(entry.text));

    private static string StripDiv(string token) => DivRegex.Replace(token, "");

    // What is left of a slot once the Alpha commands and the (bpm) marker are
    // gone: the rhythm and the notes.
    private static string RhythmBody(string token) =>
        BpmRegex.Replace(
            AlphaCommandBoundary.RemoveCommands(token), string.Empty);

    // A query is typed by hand and may use 0 as "any position", so it is not read
    // as a chart slot.
    private static HashSet<int> GetQueryPositions(string token)
    {
        var positions = new HashSet<int>();
        foreach (var part in StripDiv(token).Split('/'))
        {
            var item = part.Trim();
            if (item.Length == 0)
                continue;
            if (char.IsDigit(item[0]))
            {
                var value = item[0] - '0';
                if (value >= 0 && value <= 8)
                    positions.Add(value);
            }
            else if ("ABCDE".Contains(char.ToUpperInvariant(item[0])))
            {
                positions.Add(-1);
            }
        }

        return positions;
    }

    internal static HashSet<int> GetChartPositions(string token)
    {
        var positions = new HashSet<int>();
        foreach (var entry in SlotNotes(token))
        {
            if (NoteExpressionParser.TryParse(entry.text, out var expression, out _))
            {
                positions.Add(
                    expression.position.IsKey ? expression.position.position : -1);
                continue;
            }

            var item = entry.text.Trim();
            if (item.Length == 0)
                continue;
            if (item[0] is >= '1' and <= '8')
                positions.Add(item[0] - '0');
            else if ("ABCDE".Contains(char.ToUpperInvariant(item[0])))
                positions.Add(-1);
        }

        return positions;
    }

    internal static int CountCombo(string text)
    {
        var (tokens, _) = Tokenize(text);
        return tokens.Sum(CountComboToken);
    }

    // The notes of one timing slot, read with the shared splitter so that the
    // statistics shown here count what the runtime actually plays: "12" is two
    // Taps, and a same-head group is every one of its branches.
    internal static List<NoteSlotEntry> SlotNotes(string token)
    {
        var body = StripDiv(RhythmBody(token)).Trim();
        if (string.IsNullOrEmpty(body))
            return new List<NoteSlotEntry>();

        if (NoteSlotParser.TrySplit(body, out var entries, out _))
            return entries;

        // Charts in the library may hold syntax this editor rejects. Counting
        // what can still be read beats reporting the whole slot as empty.
        var parts = NoteSlotParser.SplitTopLevel(body);
        var fallback = new List<NoteSlotEntry>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
            fallback.Add(new NoteSlotEntry { text = parts[i], groupIndex = i });
        return fallback;
    }

    // What one Note contributes to combo. The rule belongs to the game and lives
    // in JsonDataLoader.CountNoteSum: every Note counts once, and a Slide adds
    // its guide star unless the star was removed with ! or ?, or the branch
    // shares an earlier branch's star.
    private static int NoteCombo(string note, bool ownsHead)
    {
        if (!NoteExpressionParser.TryParse(note, out var expression, out _))
            return string.IsNullOrWhiteSpace(note) ? 0 : 1;
        if (expression.kind != NoteExpressionKind.Slide)
            return 1;
        return !ownsHead || expression.modifiers.HasAny(NoteModifierFlags.NoHead)
            ? 1
            : 2;
    }

    internal static int CountComboToken(string token)
    {
        var total = 0;
        var starOwners = new HashSet<int>();
        foreach (var entry in SlotNotes(token))
            total += NoteCombo(
                entry.text,
                !entry.fromSameHead || starOwners.Add(entry.groupIndex));
        return total;
    }

    internal static int CountStars(string text)
    {
        var (tokens, _) = Tokenize(text);
        var total = 0;
        foreach (var token in tokens)
        foreach (var entry in SlotNotes(token))
            if (NoteExpressionParser.TryParse(entry.text, out var expression, out _) &&
                expression.kind == NoteExpressionKind.Slide &&
                !expression.isTouchPath)
                total++;
        return total;
    }

    private static int FindEndIndex(IReadOnlyList<TimelineItem> timeline, int winEnd)
    {
        for (var i = 0; i < timeline.Count; i++)
            if (timeline[i].Tick >= winEnd)
                return i;
        return timeline.Count;
    }

    private static double? FindBpm(IReadOnlyList<double?> bpmList, int start)
    {
        for (var i = Math.Min(start, bpmList.Count - 1); i >= 0; i--)
            if (bpmList[i].HasValue)
                return bpmList[i];
        return null;
    }

    private static string MatchValue(Regex regex, string text, string fallback)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : fallback;
    }

    private sealed record CacheEntry(DateTime LastWriteTime, ParsedChart Chart);
    private sealed record ParsedChart(string Title, string Artist, Dictionary<string, string> Levels, Dictionary<string, ParsedDifficulty> Difficulties);
    private sealed record ParsedDifficulty(List<string> Tokens, List<double?> BpmList, List<TimelineItem> Timeline, int TotalTicks, List<double> SecondsMap, HashSet<int> BoundaryTicks, int[] CumulativeCombo);
    private sealed record TimelineResult(List<TimelineItem> Items, int TotalTicks);
    private sealed record TimelineItem(int Tick, bool HasNote, int TickDur, int TokenIndex);
}

internal sealed record QueryPattern(HashSet<int> NoteTicks, int TotalTicks, Dictionary<int, HashSet<int>>? Positions);

internal readonly record struct RhythmSearchProgress(int Current, int Total, string File);

internal sealed class RhythmSearchResult
{
    public string File { get; init; } = "";
    public string Directory { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Difficulty { get; init; } = "";
    public string DifficultyNumber { get; init; } = "";
    public string Level { get; init; } = "";
    public List<RhythmMatch> Matches { get; init; } = new();
    public int Count => Matches.Count;
    public double LevelSort => ParseLevel(Level);
    public double SortBpm => Matches.Count == 0 ? 0 : Matches.Max(m => m.Bpm ?? 0);
    public double SortDensity => Matches.Count == 0 ? 0 : Matches.Max(m => m.Density);
    public int SortStars => Matches.Count == 0 ? 0 : Matches.Max(m => m.StarCount);
    public string Display => $"{Title} [{Difficulty} Lv.{Level}]  x{Count}";

    private static double ParseLevel(string level)
    {
        level = level.Trim();
        if (level.EndsWith("+", StringComparison.Ordinal))
            return double.TryParse(level[..^1], out var plus) ? plus + 0.5 : 0;
        return double.TryParse(level, out var value) ? value : 0;
    }
}

internal sealed class RhythmMatch
{
    public string Before { get; init; } = "";
    public string Match { get; init; } = "";
    public string After { get; init; } = "";
    public double? Bpm { get; init; }
    public double TimeSeconds { get; init; }
    public double EndSeconds { get; init; }
    public int Combo { get; init; }
    public int ComboBefore { get; init; }
    public int StarCount { get; init; }
    public double Density { get; init; }
    public string Display => $"{TimeSeconds:F2}s -> {EndSeconds:F2}s   {Combo}cb   ★{StarCount}";
    public string Context => $"{(string.IsNullOrEmpty(Before) ? "" : "..." + Before + ",")}{Match}{(string.IsNullOrEmpty(After) ? "" : "," + After + "...")}";
}
