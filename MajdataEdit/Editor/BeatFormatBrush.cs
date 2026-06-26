using System.Text;
using System.Text.RegularExpressions;

namespace MajdataEdit.Editor;

internal static class BeatFormatBrush
{
    private const int MeasureUnits = 384;
    private static readonly Regex BeatMarker = new(@"\{(?<beat>\d+)\}", RegexOptions.Compiled);

    public static string Transform(string text, int? requestedBeat)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sourceBeat = FindFirstBeat(text);
        if (sourceBeat <= 0)
            return text;

        var targetBeat = requestedBeat ?? FindMaxBeat(text);
        return TransformWithContext(text, sourceBeat, targetBeat);
    }

    public static string TransformSelection(string fullText, int selectionStart, int selectionLength, int? requestedBeat)
    {
        if (string.IsNullOrEmpty(fullText) || selectionLength <= 0)
            return string.Empty;

        selectionStart = Math.Clamp(selectionStart, 0, fullText.Length);
        selectionLength = Math.Clamp(selectionLength, 0, fullText.Length - selectionStart);
        var selected = fullText.Substring(selectionStart, selectionLength);
        var activeBeat = FindActiveBeatBefore(fullText, selectionStart);
        var sourceBeat = activeBeat > 0 ? activeBeat : FindFirstBeat(selected);
        if (sourceBeat <= 0)
            return selected;

        var targetBeat = requestedBeat ?? new[] { FindMaxBeat(selected), sourceBeat, activeBeat }.Max();
        return TransformWithContext(selected, sourceBeat, targetBeat);
    }

    private static string TransformWithContext(string text, int defaultBeat, int targetBeat)
    {
        if (defaultBeat <= 0 || targetBeat <= 0)
            return text;

        var tokens = Tokenize(text, defaultBeat);
        if (tokens.Count == 0)
            return StripBeatMarkers(text);

        var output = new StringBuilder(text.Length);
        var currentBeat = 0;
        var emptyUnits = 0;

        void SwitchBeat(int beat)
        {
            if (beat <= 0 || currentBeat == beat)
                return;
            output.Append('{').Append(beat).Append('}');
            currentBeat = beat;
        }

        void EmitCommasOnly(int units, int preferredBeat)
        {
            if (units <= 0)
                return;

            var preferredUnit = Unit(preferredBeat);
            if (preferredUnit > 0 && units % preferredUnit == 0)
            {
                SwitchBeat(preferredBeat);
                output.Append(',', units / preferredUnit);
                return;
            }

            var defaultUnit = Unit(defaultBeat);
            if (defaultUnit > 0 && units % defaultUnit == 0)
            {
                SwitchBeat(defaultBeat);
                output.Append(',', units / defaultUnit);
                return;
            }

            if (preferredUnit > 0 && units > preferredUnit)
            {
                var preferredCount = units / preferredUnit;
                var consumed = preferredCount * preferredUnit;
                SwitchBeat(preferredBeat);
                output.Append(',', preferredCount);
                EmitCommasOnly(units - consumed, defaultBeat);
            }
        }

        void FlushEmpty()
        {
            if (emptyUnits <= 0)
                return;
            EmitCommasOnly(emptyUnits, targetBeat);
            emptyUnits = 0;
        }

        void EmitTextWithCommas(string textPart, int units, int sourceBeat)
        {
            if (units <= 0)
            {
                output.Append(textPart);
                return;
            }

            var targetUnit = Unit(targetBeat);
            var sourceUnit = Unit(sourceBeat);
            if (targetUnit > 0 && sourceUnit > 0 && currentBeat != targetBeat)
            {
                // Do not emit note{beat}; keep the note visible by leaving the
                // smallest possible source-beat comma prefix, then switch.
                for (var sourceCount = 1; sourceCount * sourceUnit <= units; sourceCount++)
                {
                    var remaining = units - sourceCount * sourceUnit;
                    if (remaining == 0 || remaining % targetUnit != 0)
                        continue;

                    SwitchBeat(sourceBeat);
                    output.Append(textPart);
                    output.Append(',', sourceCount);
                    EmitCommasOnly(remaining, targetBeat);
                    return;
                }
            }

            if (targetUnit > 0 && units % targetUnit == 0)
            {
                SwitchBeat(targetBeat);
                output.Append(textPart);
                output.Append(',', units / targetUnit);
                return;
            }

            if (sourceUnit > 0)
            {
                var sourceCount = units % targetUnit == 0
                    ? 0
                    : units % targetUnit / sourceUnit;
                if (sourceCount > 0 && sourceCount * sourceUnit < units)
                {
                    SwitchBeat(sourceBeat);
                    output.Append(textPart);
                    output.Append(',', sourceCount);
                    EmitCommasOnly(units - sourceCount * sourceUnit, targetBeat);
                    return;
                }

                if (units % sourceUnit == 0)
                {
                    SwitchBeat(sourceBeat);
                    output.Append(textPart);
                    output.Append(',', units / sourceUnit);
                    return;
                }
            }

            output.Append(textPart);
            EmitCommasOnly(units, targetBeat);
        }

        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            var textPart = StripBeatMarkers(token.Text);
            var commaUnits = token.HasComma ? Unit(token.Beat) : 0;

            if (string.IsNullOrWhiteSpace(textPart))
            {
                emptyUnits += commaUnits;
                if (!token.HasComma)
                    output.Append(textPart);
                continue;
            }

            FlushEmpty();
            if (token.HasComma)
            {
                while (tokenIndex + 1 < tokens.Count)
                {
                    var next = tokens[tokenIndex + 1];
                    var nextText = StripBeatMarkers(next.Text);
                    if (!next.HasComma || !string.IsNullOrWhiteSpace(nextText))
                        break;
                    commaUnits += Unit(next.Beat);
                    tokenIndex++;
                }
                EmitTextWithCommas(textPart, commaUnits, token.Beat);
            }
            else
                output.Append(textPart);
        }

        FlushEmpty();
        return output.ToString();
    }

    private static List<CommaToken> Tokenize(string text, int initialBeat)
    {
        var result = new List<CommaToken>();
        var start = 0;
        var beat = initialBeat;
        var squareDepth = 0;
        var angleDepth = 0;
        var roundDepth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (squareDepth == 0 && angleDepth == 0 && roundDepth == 0 && text[i] == '{')
            {
                var close = text.IndexOf('}', i + 1);
                if (close > i && int.TryParse(text.Substring(i + 1, close - i - 1), out var parsedBeat))
                {
                    beat = parsedBeat;
                    i = close;
                    continue;
                }
            }

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
                result.Add(new CommaToken(text.Substring(start, i - start), true, beat));
                start = i + 1;
            }
        }

        result.Add(new CommaToken(text.Substring(start), false, beat));
        return result;
    }

    private static int Unit(int beat) => beat > 0 ? MeasureUnits / beat : 0;

    private static string StripBeatMarkers(string text) => BeatMarker.Replace(text, "");

    private static int FindFirstBeat(string text)
    {
        var match = BeatMarker.Match(text);
        return match.Success && int.TryParse(match.Groups["beat"].Value, out var beat) ? beat : -1;
    }

    private static int FindMaxBeat(string text)
    {
        var max = -1;
        foreach (Match match in BeatMarker.Matches(text))
            if (int.TryParse(match.Groups["beat"].Value, out var beat))
                max = Math.Max(max, beat);
        return max;
    }

    private static int FindActiveBeatBefore(string text, int position)
    {
        var source = text.Substring(0, Math.Clamp(position, 0, text.Length));
        var matches = BeatMarker.Matches(source);
        if (matches.Count == 0)
            return -1;

        return int.TryParse(matches[^1].Groups["beat"].Value, out var beat) ? beat : -1;
    }

    private static bool IsAlphaTokenStart(string text, int index)
    {
        if (index + 2 >= text.Length || !char.IsLetter(text[index + 1]))
            return false;
        var close = text.IndexOf('>', index + 1);
        var star = text.IndexOf('*', index + 1);
        return close > index && star > index && star < close;
    }

    private readonly record struct CommaToken(string Text, bool HasComma, int Beat);
}
