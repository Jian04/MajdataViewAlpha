using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MajdataEdit.Editor;

/// <summary>
/// Reflows chart timing cells without allowing control syntax to consume a beat.
/// </summary>
internal static class ChartOrganizer
{
    private const int MeasureUnits = 384;
    private const int QuarterUnits = 96;
    private const string MeasureWord = "\u5C0F\u8282";
    private static readonly Regex BeatMarker = new(@"\{(?<beat>\d+)\}", RegexOptions.Compiled);
    private static readonly Regex MeasureComment = new(
        @"^\|\|\s*" + MeasureWord + @"\b", RegexOptions.Compiled);

    public static bool CanOrganize(string text) => FindFirstBeat(text) > 0;

    public static string Organize(string text, bool addMeasureComments = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var initialBeat = FindFirstBeat(text);
        if (initialBeat <= 0)
            return text;

        var parts = Tokenize(text, initialBeat, out var trailing);
        if (parts.Count == 0)
            return text;

        var output = new StringBuilder(text.Length + parts.Count * 8);
        var lineCells = new List<Cell>();
        var measureUnits = 0;
        var currentMeasureUnits = MeasureUnits;
        var completeMeasureCount = 0;
        var measureNumber = 1;
        var groupLabelWritten = false;
        var pendingGroupSeparator = false;

        void FlushLine()
        {
            if (lineCells.Count == 0)
                return;

            if (addMeasureComments && !groupLabelWritten && (measureNumber - 1) % 4 == 0)
            {
                output.Append("||").Append(MeasureWord).Append(' ')
                    .Append(measureNumber).Append('-').Append(measureNumber + 3).Append('\n');
                groupLabelWritten = true;
            }
            AppendMeasure(output, lineCells);
            output.Append('\n');
            lineCells.Clear();
        }

        foreach (var part in parts)
        {
            // A block separator belongs before the next block's leading controls.
            // This keeps <...> at the top of its block instead of leaving a blank
            // line between the command and the notes it controls.
            if (pendingGroupSeparator && lineCells.Count == 0)
            {
                if (output.Length > 0 && output[^1] != '\n')
                    output.Append('\n');
                output.Append('\n');
                pendingGroupSeparator = false;
            }

            if (part.IsControl)
            {
                FlushLine();
                if (part.MeterUnits > 0)
                {
                    if (measureUnits > 0)
                    {
                        completeMeasureCount++;
                        measureNumber++;
                        groupLabelWritten = (measureNumber - 1) % 4 != 0;
                    }
                    measureUnits = 0;
                    currentMeasureUnits = part.MeterUnits;
                    if (part.MeterUnits != MeasureUnits)
                        output.Append(part.Text.Trim()).Append('\n');
                }
                else
                    output.Append(part.Text.Trim()).Append('\n');
                continue;
            }

            lineCells.Add(part.Cell);
            measureUnits += part.Cell.Units;
            while (measureUnits >= currentMeasureUnits)
            {
                FlushLine();
                measureUnits -= currentMeasureUnits;
                completeMeasureCount++;
                measureNumber++;
                groupLabelWritten = (measureNumber - 1) % 4 != 0;
                if (completeMeasureCount % 4 == 0)
                    pendingGroupSeparator = true;
            }
        }
        FlushLine();

        if (!string.IsNullOrWhiteSpace(trailing))
            output.Append(trailing.Trim()).Append('\n');
        return output.ToString().TrimEnd('\n');
    }

    public static int GetMeasureNumberAt(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset <= 0)
            return 1;

        var prefix = text[..Math.Clamp(offset, 0, text.Length)];
        var initialBeat = FindFirstBeat(text);
        if (initialBeat <= 0)
            initialBeat = 4;
        var parts = Tokenize(prefix, initialBeat, out _, includeTrailingCell: false);
        var measureNumber = 1;
        var measureUnits = 0;
        var currentMeasureUnits = MeasureUnits;

        foreach (var part in parts)
        {
            if (part.IsControl)
            {
                if (part.MeterUnits <= 0)
                    continue;
                if (measureUnits > 0)
                    measureNumber++;
                measureUnits = 0;
                currentMeasureUnits = part.MeterUnits;
                continue;
            }

            measureUnits += part.Cell.Units;
            while (measureUnits >= currentMeasureUnits)
            {
                measureUnits -= currentMeasureUnits;
                measureNumber++;
            }
        }
        return measureNumber;
    }

    private static List<ChartPart> Tokenize(
        string text,
        int initialBeat,
        out string trailing,
        bool includeTrailingCell = true)
    {
        var parts = new List<ChartPart>();
        var cellStart = 0;
        var beat = initialBeat;
        var squareDepth = 0;
        var roundDepth = 0;

        void AddPendingControlPrefix(int end)
        {
            var prefix = CleanNote(text.Substring(cellStart, end - cellStart));
            if (!string.IsNullOrEmpty(prefix))
                parts.Add(ChartPart.Control(prefix));
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (squareDepth == 0 && roundDepth == 0 && text[index] == '{')
            {
                var close = text.IndexOf('}', index + 1);
                if (close > index && int.TryParse(text.Substring(index + 1, close - index - 1), out var parsed) && parsed > 0)
                {
                    beat = parsed;
                    index = close;
                    continue;
                }
            }

            if (squareDepth == 0 && roundDepth == 0 && text[index] is '@' or '&')
            {
                var end = FindLineEnd(text, index);
                AddPendingControlPrefix(index);
                var control = text.Substring(index, end - index);
                parts.Add(ChartPart.Control(control, ParseMeterUnits(control)));
                cellStart = end;
                index = end - 1;
                continue;
            }

            if (squareDepth == 0 && roundDepth == 0 && text[index] == '|' && index + 1 < text.Length && text[index + 1] == '|')
            {
                var end = FindLineEnd(text, index);
                AddPendingControlPrefix(index);
                var comment = text.Substring(index, end - index);
                if (!MeasureComment.IsMatch(comment.Trim()))
                    parts.Add(ChartPart.Control(comment));
                cellStart = end;
                index = end - 1;
                continue;
            }

            if (squareDepth == 0 && roundDepth == 0 && IsAlphaCommandStart(text, index, out var alphaEnd))
            {
                AddPendingControlPrefix(index);
                parts.Add(ChartPart.Control(text.Substring(index, alphaEnd - index + 1)));
                cellStart = alphaEnd + 1;
                index = alphaEnd;
                continue;
            }

            switch (text[index])
            {
                case '[': squareDepth++; break;
                case ']': squareDepth = Math.Max(0, squareDepth - 1); break;
                case '(': roundDepth++; break;
                case ')': roundDepth = Math.Max(0, roundDepth - 1); break;
            }

            if (text[index] != ',' || squareDepth != 0 || roundDepth != 0)
                continue;

            parts.Add(ChartPart.Timing(new Cell(CleanNote(text.Substring(cellStart, index - cellStart)), MeasureUnits / beat)));
            cellStart = index + 1;
        }

        trailing = CleanNote(text.Substring(cellStart));
        if (includeTrailingCell && !string.IsNullOrEmpty(trailing) &&
            !string.Equals(trailing, "E", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(ChartPart.Timing(new Cell(trailing, MeasureUnits / beat)));
            trailing = string.Empty;
        }
        return parts;
    }

    private static int ParseMeterUnits(string control)
    {
        var match = Regex.Match(control.Trim(), @"^[@&](?<numerator>\d+)\s*/\s*(?<denominator>\d+)$");
        if (!match.Success ||
            !int.TryParse(match.Groups["numerator"].Value, out var numerator) ||
            !int.TryParse(match.Groups["denominator"].Value, out var denominator) ||
            numerator <= 0 || denominator <= 0)
            return 0;
        return Math.Max(1, MeasureUnits * numerator / denominator);
    }

    private static bool IsAlphaCommandStart(string text, int start, out int end)
    {
        return AlphaCommandBoundary.TryGetCommand(text, start, out end);
    }

    private static int FindLineEnd(string text, int start)
    {
        var end = text.IndexOfAny(new[] { '\r', '\n' }, start);
        return end < 0 ? text.Length : end;
    }

    private static void AppendMeasure(StringBuilder output, IReadOnlyList<Cell> cells)
    {
        var gcd = 0;
        foreach (var cell in cells)
            gcd = Gcd(gcd, cell.Units);

        var requiredBeat = gcd > 0 ? MeasureUnits / gcd : 16;
        var beat = Lcm(16, requiredBeat);
        if (beat <= 0 || MeasureUnits % beat != 0)
            beat = requiredBeat > 16 && MeasureUnits % requiredBeat == 0 ? requiredBeat : 16;
        var slotUnits = MeasureUnits / beat;
        var usedUnits = 0;

        output.Append('{').Append(beat).Append('}');
        foreach (var cell in cells)
        {
            output.Append(cell.Note);
            var commas = Math.Max(1, cell.Units / slotUnits);
            for (var index = 0; index < commas; index++)
            {
                output.Append(',');
                usedUnits += slotUnits;
                if (usedUnits % QuarterUnits == 0 && usedUnits < MeasureUnits)
                    output.Append(' ');
            }
        }
    }

    private static string CleanNote(string text)
    {
        var withoutBeat = BeatMarker.Replace(text, string.Empty);
        var result = new StringBuilder(withoutBeat.Length);
        foreach (var character in withoutBeat)
            if (!char.IsWhiteSpace(character))
                result.Append(character);
        return result.ToString();
    }

    private static int FindFirstBeat(string text)
    {
        var match = BeatMarker.Match(text);
        return match.Success && int.TryParse(match.Groups["beat"].Value, out var beat) ? beat : -1;
    }

    private static int Gcd(int left, int right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
            (left, right) = (right, left % right);
        return left;
    }

    private static int Lcm(int left, int right)
    {
        if (left == 0 || right == 0)
            return Math.Max(left, right);
        return left / Gcd(left, right) * right;
    }

    private readonly record struct Cell(string Note, int Units);

    private readonly record struct ChartPart(bool IsControl, string Text, Cell Cell, int MeterUnits)
    {
        public static ChartPart Control(string text, int meterUnits = 0) => new(true, text, default, meterUnits);
        public static ChartPart Timing(Cell cell) => new(false, string.Empty, cell, 0);
    }
}
