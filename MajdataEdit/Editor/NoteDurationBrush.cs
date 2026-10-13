using System.Globalization;
using System.Text;
using MajdataCore;

namespace MajdataEdit.Editor;

internal static class NoteDurationBrush
{
    private sealed record Target(int Start, int Length, string Prefix, string Value, double Seconds);

    // Only note durations are edited. Commands, skin paths and inherited paths
    // remain intact; delay/BPM prefixes in a slide duration are retained.
    public static string Transform(string selected, string mode, double bpm = 120)
    {
        var targets = new List<Target>();
        var start = 0;
        void ReadNote(int end)
        {
            var raw = selected.Substring(start, end - start);
            var leading = raw.Length - raw.TrimStart().Length;
            var noteText = raw.Trim();
            if (noteText.Length == 0) return;
            var candidate = noteText;
            if (SlidePathParser.ContainsSlideShape(candidate[..1])) candidate = "1" + candidate;
            if (!NoteExpressionParser.TryParse(candidate, out var note, out _, forPreview: true) ||
                note.kind is not (NoteExpressionKind.Hold or NoteExpressionKind.TouchHold or
                    NoteExpressionKind.Slide or NoteExpressionKind.NoiseZone)) return;
            var count = 0;
            var insertAt = noteText.Length;
            for (var i = 0; i < noteText.Length; i++)
            {
                if (noteText[i] != '[') continue;
                var close = MatchingBracket(noteText, i);
                if (close < 0) return;
                if (i > 0 && noteText[i - 1] == '~')
                { insertAt = Math.Min(insertAt, i - 1); i = close; continue; }
                var duration = noteText.Substring(i, close - i + 1);
                if (!SlideSyntaxValidator.TryGetLengthSeconds(duration, bpm, out var seconds))
                { i = close; continue; }
                var body = duration[1..^1];
                var prefixEnd = body.LastIndexOf('#') + 1;
                var prefix = body[..prefixEnd];
                targets.Add(new(start + leading + i, duration.Length, prefix, body[prefixEnd..], seconds));
                count++;
                i = close;
            }
            if (count == 0)
                targets.Add(new(start + leading + insertAt, 0, "", "384:1", 240d / bpm / 384));
        }
        for (var i = 0; i <= selected.Length; i++)
        {
            if (i == selected.Length) { ReadNote(i); break; }
            if (selected[i] == '[')
            { var close = MatchingBracket(selected, i); if (close >= 0) i = close; continue; }
            if (selected[i] == '<' && AlphaCommandBoundary.TryGetCommand(selected, i, out var commandEnd))
            { ReadNote(i); i = commandEnd; start = i + 1; continue; }
            if (selected[i] is '{' or '(')
            {
                var close = selected.IndexOf(selected[i] == '{' ? '}' : ')', i + 1);
                if (close < 0) continue;
                ReadNote(i);
                if (selected[i] == '(' && double.TryParse(selected.Substring(i + 1, close - i - 1),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var tempo) && tempo > 0) bpm = tempo;
                i = close; start = i + 1; continue;
            }
            if (selected[i] == '|' && i + 1 < selected.Length && selected[i + 1] == '|')
            {
                ReadNote(i);
                var newline = selected.IndexOf('\n', i);
                i = newline < 0 ? selected.Length : newline; start = i + 1; continue;
            }
            if (selected[i] is ',' or '/' or '`' or '*' or '\r' or '\n')
            { ReadNote(i); start = i + 1; }
        }
        if (targets.Count == 0) return selected;
        var extreme = mode == "max" ? targets.MaxBy(t => t.Seconds) : targets.MinBy(t => t.Seconds);
        var output = new StringBuilder(selected);
        foreach (var target in targets.OrderByDescending(t => t.Start))
        {
            var value = mode is "max" or "min" ? extreme!.Value : mode;
            // An explicit seconds length needs a '#' even when the target originally
            // used beat fractions. Existing BPM/delay prefixes still take priority.
            var prefix = target.Prefix;
            if (value.Contains(':') && prefix == "#") prefix = "";
            if (!value.Contains(':') && prefix.Length == 0) prefix = "#";
            output.Remove(target.Start, target.Length);
            output.Insert(target.Start, "[" + prefix + value + "]");
        }
        return output.ToString();
    }

    public static double BpmBefore(string text, int offset)
    {
        var bpm = 120d;
        for (var i = 0; i < Math.Min(text.Length, offset); i++)
        {
            if (text[i] == '<' && AlphaCommandBoundary.TryGetCommand(text, i, out var end)) { i = end; continue; }
            if (text[i] == '[') { var endBracket = MatchingBracket(text, i); if (endBracket >= 0) i = endBracket; continue; }
            if (text[i] == '|' && i + 1 < text.Length && text[i + 1] == '|')
            { var newline = text.IndexOf('\n', i); if (newline < 0) break; i = newline; continue; }
            if (text[i] != '(') continue;
            var close = text.IndexOf(')', i + 1);
            if (close < 0 || close >= offset) continue;
            if (double.TryParse(text.Substring(i + 1, close - i - 1), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var tempo) && tempo > 0) bpm = tempo;
            i = close;
        }
        return bpm;
    }

    private static int MatchingBracket(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '[') depth++;
            if (text[i] == ']' && --depth == 0) return i;
        }
        return -1;
    }
}
