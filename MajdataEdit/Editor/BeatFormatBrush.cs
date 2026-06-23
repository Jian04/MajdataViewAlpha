using System.Text;
using System.Text.RegularExpressions;

namespace MajdataEdit.Editor;

internal static class BeatFormatBrush
{
    private static readonly Regex BeatMarker = new(@"\{(?<beat>\d+)\}", RegexOptions.Compiled);

    public static string Transform(string text, int? requestedBeat)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var matches = BeatMarker.Matches(text);
        if (matches.Count == 0)
            return text;

        var target = requestedBeat ?? matches
            .Select(match => int.Parse(match.Groups["beat"].Value))
            .Max();
        if (target <= 0)
            return text;

        var result = new StringBuilder(text.Length);
        var cursor = 0;
        for (var i = 0; i < matches.Count; i++)
        {
            var marker = matches[i];
            var bodyStart = marker.Index + marker.Length;
            var bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var sourceBeat = int.Parse(marker.Groups["beat"].Value);
            var body = text.Substring(bodyStart, bodyEnd - bodyStart);

            result.Append(text, cursor, marker.Index - cursor);
            if (TryTransformBody(body, sourceBeat, target, out var transformed))
            {
                result.Append('{').Append(target).Append('}');
                result.Append(transformed);
            }
            else
            {
                result.Append(marker.Value);
                result.Append(body);
            }
            cursor = bodyEnd;
        }
        result.Append(text, cursor, text.Length - cursor);
        return result.ToString();
    }

    private static bool TryTransformBody(string body, int sourceBeat, int targetBeat, out string transformed)
    {
        transformed = body;
        if (sourceBeat == targetBeat)
            return true;

        if (targetBeat > sourceBeat)
        {
            if (targetBeat % sourceBeat != 0)
                return false;
            transformed = ExpandCommas(body, targetBeat / sourceBeat);
            return true;
        }

        if (sourceBeat % targetBeat != 0)
            return false;
        return TryCollapseCommas(body, sourceBeat / targetBeat, out transformed);
    }

    private static string ExpandCommas(string body, int ratio)
    {
        var result = new StringBuilder(body.Length * ratio);
        foreach (var part in SplitTopLevelCommas(body))
        {
            result.Append(part.Text);
            if (!part.HasComma)
                continue;
            result.Append(',', ratio);
        }
        return result.ToString();
    }

    private static bool TryCollapseCommas(string body, int ratio, out string transformed)
    {
        transformed = body;
        var parts = SplitTopLevelCommas(body);
        var commaCount = parts.Count(part => part.HasComma);
        if (commaCount == 0 || commaCount % ratio != 0)
            return false;

        var result = new StringBuilder(body.Length);
        for (var i = 0; i < parts.Count;)
        {
            var first = parts[i];
            result.Append(first.Text);
            if (!first.HasComma)
            {
                i++;
                continue;
            }

            for (var offset = 1; offset < ratio; offset++)
            {
                var skippedIndex = i + offset;
                if (skippedIndex >= parts.Count ||
                    !parts[skippedIndex].HasComma ||
                    !string.IsNullOrWhiteSpace(parts[skippedIndex].Text))
                    return false;
            }

            result.Append(',');
            i += ratio;
        }

        transformed = result.ToString();
        return true;
    }

    private static List<(string Text, bool HasComma)> SplitTopLevelCommas(string text)
    {
        var result = new List<(string, bool)>();
        var start = 0;
        var squareDepth = 0;
        var angleDepth = 0;
        var roundDepth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[': squareDepth++; break;
                case ']': squareDepth = Math.Max(0, squareDepth - 1); break;
                case '<' when IsAlphaTokenStart(text, i): angleDepth++; break;
                case '>': angleDepth = Math.Max(0, angleDepth - 1); break;
                case '(': roundDepth++; break;
                case ')': roundDepth = Math.Max(0, roundDepth - 1); break;
            }
            if (text[i] == ',' && squareDepth == 0 && angleDepth == 0 && roundDepth == 0)
            {
                result.Add((text.Substring(start, i - start), true));
                start = i + 1;
            }
        }
        result.Add((text.Substring(start), false));
        return result;
    }

    private static bool IsAlphaTokenStart(string text, int index)
    {
        if (index + 2 >= text.Length || !char.IsLetter(text[index + 1]))
            return false;
        var close = text.IndexOf('>', index + 1);
        var star = text.IndexOf('*', index + 1);
        return close > index && star > index && star < close;
    }
}
