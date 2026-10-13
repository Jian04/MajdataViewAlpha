using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MajdataEdit.Editor;

internal static class NoteStreamMerger
{
    private static readonly Regex DivisionPattern = new(
        @"\{\s*(\d+)\s*\}", RegexOptions.Compiled);

    public static bool CanMerge(string text, int caret) =>
        TryBuildMerge(text, caret, out _, out _, out _, out _);
    public static bool IsInStream(string text,int caret) => TryFindOverlay(text,caret,out _);

    public static bool TryGetMergeCaret(string text, int caret, int selectionStart, int selectionLength, out int mergeCaret)
    {
        mergeCaret = selectionLength > 0 ? selectionStart : caret;
        if (!TryFindOverlay(text, mergeCaret, out var range)) return false;
        return selectionLength == 0 || selectionStart + selectionLength <= range.End;
    }

    public static bool TryFlattenAll(
        string text, out string result, out string error)
    {
        result = text ?? string.Empty;
        error = string.Empty;
        for (var iteration = 0; iteration < 1024; iteration++)
        {
            if (!TryFindLastOverlayCaret(result, out var caret))
                return true;
            if (!TryBuildMerge(
                    result, caret, out var start, out var length,
                    out var replacement, out error))
                return false;
            result = result.Remove(start, length).Insert(start, replacement);
        }

        error = "音符流层数超过 1024，无法安全导出。";
        return false;
    }

    public static bool TryBuildMerge(
        string text,
        int caret,
        out int replacementStart,
        out int replacementLength,
        out string replacement,
        out string error)
    {
        replacementStart = 0;
        replacementLength = 0;
        replacement = string.Empty;
        error = string.Empty;

        if (!TryFindOverlay(text, caret, out var overlay))
        {
            error = "光标不在音符流中。";
            return false;
        }

        var inheritedDivision=GetMainDivision(text,overlay.Start);
        if (!TryParseStream(text, overlay.ContentStart, overlay.ContentEnd,
                null, out var overlayStream, out _, out error,inheritedDivision))
            return false;

        var targetStart = overlay.End;
        while (targetStart < text.Length && char.IsWhiteSpace(text[targetStart]))
            targetStart++;
        var interveningStart=targetStart;
        while(targetStart<text.Length && text[targetStart]=='@' && TryFindOverlay(text,Math.Min(text.Length,targetStart+2),out var next))
        {
            targetStart=next.End;
            while(targetStart<text.Length && char.IsWhiteSpace(text[targetStart])) targetStart++;
        }
        var intervening=text.Substring(interveningStart,targetStart-interveningStart);
        if (targetStart >= text.Length)
        {
            error = "音符流后没有可合并的主音符流。";
            return false;
        }

        if (!TryParseStream(text, targetStart, text.Length,
                overlayStream.Duration, out var targetStream, out var targetEnd,
                out error,inheritedDivision))
            return false;
        // The overlay can end between main-stream slots. Include the whole
        // final main slot and leave the remainder of that interval empty.
        var duration=targetStream.Duration>overlayStream.Duration ? targetStream.Duration : overlayStream.Duration;

        var grid = 1L;
        foreach (var denominator in overlayStream.Denominators.Concat(targetStream.Denominators))
        {
            grid = Lcm(grid, denominator);
            if (grid > 3072)
            {
                error = "合并后的分拍超过 3072，已取消以避免生成不可读谱面。";
                return false;
            }
        }

        var slotCountNumerator = duration.Numerator * grid;
        if (slotCountNumerator % duration.Denominator != 0)
        {
            error = "音符流时间无法映射到同一分拍。";
            return false;
        }
        var slotCount = slotCountNumerator / duration.Denominator;
        if (slotCount <= 0 || slotCount > 200000)
        {
            error = "音符流长度超出可合并范围。";
            return false;
        }

        var cells = new SortedDictionary<long, MergedCell>();
        AddEvents(cells, overlayStream.Events, grid);
        AddEvents(cells, targetStream.Events, grid);

        var result = new StringBuilder();
        result.Append('{').Append(grid).Append('}');
        for (var slot = 0L; slot < slotCount; slot++)
        {
            if (cells.TryGetValue(slot, out var cell))
                result.Append(cell.Build());
            result.Append(',');
        }

        replacementStart = overlay.Start;
        replacementLength = targetEnd - overlay.Start;
        if(targetEnd<text.Length && !text.Substring(targetEnd).TrimStart().StartsWith("{"))
            result.Append('{').Append(targetStream.LastDivision).Append('}');
        replacement = intervening+result;
        return true;
    }

    private static int GetMainDivision(string text,int end)
    {
        var division=0;
        for(var index=0;index<end;index++)
        {
            if(text[index]=='@' && index+1<end && text[index+1] is '{' or '*')
            {
                if(TryFindOverlay(text,index+2,out var overlay)) { index=overlay.End-1;continue; }
            }
            if(text[index]=='<' && IsCommand(text,index)) { index=CommandEnd(text,index);continue; }
            if(text[index]=='|' && index+1<end && text[index+1]=='|') { while(index<end && text[index]!='\n') index++;continue; }
            if(text[index]!='{') continue;
            var match=DivisionPattern.Match(text,index);
            if(match.Success && match.Index==index && int.TryParse(match.Groups[1].Value,out var value)) division=value;
        }
        return division;
    }
    private static bool IsCommand(string text,int at)
    {
        var end=at+1;
        while(end<text.Length && char.IsLetterOrDigit(text[end])) end++;
        return end>at+1 && end<text.Length && text[end]=='*';
    }
    private static int CommandEnd(string text,int start)
    {
        char quote='\0';
        for(var index=start+1;index<text.Length;index++)
        {
            var value=text[index];
            if(quote!='\0') { if(value=='\\') index++;else if(value==quote) quote='\0'; }
            else if(value is '\"' or '\'') quote=value;
            else if(value=='>') return index;
        }
        return start;
    }

    private static void AddEvents(
        IDictionary<long, MergedCell> cells,
        IEnumerable<StreamEvent> events,
        long grid)
    {
        foreach (var item in events)
        {
            var numerator = item.Time.Numerator * grid;
            if (numerator % item.Time.Denominator != 0)
                continue;
            var slot = numerator / item.Time.Denominator;
            if (!cells.TryGetValue(slot, out var cell))
            {
                cell = new MergedCell();
                cells.Add(slot, cell);
            }
            cell.Add(item);
        }
    }

    private static bool TryFindLastOverlayCaret(string text, out int caret)
    {
        caret = -1;
        var lineOverlay = Regex.Matches(text, @"(?m)^[\t ]*@\{");
        if (lineOverlay.Count > 0)
        {
            var match = lineOverlay[^1];
            caret = match.Index + match.Value.LastIndexOf('@') + 1;
        }

        var blockOverlay = Regex.Matches(text, @"(?m)^[\t ]*@\*");
        if (blockOverlay.Count > 0)
        {
            var match = blockOverlay[^1];
            var blockCaret = match.Index + match.Value.LastIndexOf('@') + 1;
            if (blockCaret > caret)
                caret = blockCaret;
        }
        return caret >= 0;
    }

    private static bool TryFindOverlay(string text, int caret, out OverlayRange range)
    {
        range = default;
        if (text.Length == 0) return false;
        caret = Math.Clamp(caret, 0, text.Length);

        var blockStart = text.LastIndexOf("@*", Math.Min(caret + 1, text.Length - 1),
            StringComparison.Ordinal);
        if (blockStart >= 0)
        {
            var blockEndMarker = text.IndexOf("*@", blockStart + 2,
                StringComparison.Ordinal);
            if (blockEndMarker >= 0 && caret >= blockStart &&
                caret < blockEndMarker + 2)
            {
                range = new OverlayRange(
                    blockStart, blockEndMarker + 2,
                    blockStart + 2, blockEndMarker);
                return true;
            }
        }

        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var lineEnd = text.IndexOfAny(new[] { '\r', '\n' }, caret);
        if (lineEnd < 0)
            lineEnd = text.Length;
        var marker = lineStart;
        while (marker < lineEnd && char.IsWhiteSpace(text[marker]))
            marker++;
        if (marker + 1 < lineEnd && caret >= marker && text[marker] == '@' && text[marker + 1] == '{')
        {
            range = new OverlayRange(marker, lineEnd, marker + 1, lineEnd);
            return true;
        }
        return false;
    }

    private static bool TryParseStream(
        string text,
        int start,
        int end,
        Fraction? requiredDuration,
        out ParsedStream stream,
        out int consumedEnd,
        out string error,int initialDivision=0)
    {
        stream = new ParsedStream();
        consumedEnd = start;
        error = string.Empty;
        var cursor = start;
        while (cursor < end && char.IsWhiteSpace(text[cursor]))
            cursor++;
        if (cursor >= end || text[cursor] != '{' && initialDivision<=0)
        {
            error = "音符流必须以 {分拍} 开始。";
            return false;
        }

        var division = initialDivision;
        if(division>0) stream.Denominators.Add(division);
        var cellStart = cursor;
        var squareDepth = 0;
        var roundDepth = 0;
        var angleDepth = 0;
        for (; cursor < end; cursor++)
        {
            if(text[cursor]=='<' && squareDepth==0 && IsCommand(text,cursor))
            { cursor=CommandEnd(text,cursor);continue; }
            var character = text[cursor];
            switch (character)
            {
                case '[': squareDepth++; break;
                case ']': squareDepth = Math.Max(0, squareDepth - 1); break;
                case '(' when squareDepth == 0: roundDepth++; break;
                case ')' when squareDepth == 0: roundDepth = Math.Max(0, roundDepth - 1); break;
            }

            if (character != ',' || squareDepth != 0 || roundDepth != 0 || angleDepth != 0)
                continue;

            var rawCell = text.Substring(cellStart, cursor - cellStart);
            rawCell=ReadDivisions(rawCell,ref division,stream.Denominators);
            if (division <= 0)
            {
                error = "音符流缺少有效的 {分拍}。";
                return false;
            }

            SplitCell(rawCell, out var controls, out var notes);
            if (controls.Length != 0 || notes.Length != 0)
                stream.Events.Add(new StreamEvent(stream.Duration, controls, notes));
            stream.Duration += new Fraction(1, division);
            stream.LastDivision=division;
            consumedEnd = cursor + 1;
            cellStart = cursor + 1;

            if (requiredDuration.HasValue)
            {
                if (stream.Duration == requiredDuration.Value)
                    return true;
                if (stream.Duration > requiredDuration.Value)
                    return true;
            }
        }

        if (requiredDuration.HasValue)
        {
            error = "主音符流短于待合并音符流。";
            return false;
        }
        return stream.Duration.Numerator > 0;
    }

    private static void SplitCell(string raw, out string controls, out string notes)
    {
        var controlBuilder = new StringBuilder();
        var noteBuilder = new StringBuilder();
        var squareDepth=0;
        for (var index = 0; index < raw.Length; index++)
        {
            if(raw[index]=='[') squareDepth++;
            if(raw[index]==']') squareDepth=Math.Max(0,squareDepth-1);
            if (squareDepth==0 && raw[index] == '<' && IsCommand(raw,index))
            {
                var end = CommandEnd(raw,index);
                if (end > index)
                {
                    controlBuilder.Append(raw, index, end - index + 1);
                    index = end;
                    continue;
                }
            }
            if (squareDepth==0 && raw[index] == '(')
            {
                var end = raw.IndexOf(')', index + 1);
                if (end >= 0)
                {
                    controlBuilder.Append(raw, index, end - index + 1);
                    index = end;
                    continue;
                }
            }
            if (!char.IsWhiteSpace(raw[index]))
                noteBuilder.Append(raw[index]);
        }
        controls = controlBuilder.ToString();
        notes = noteBuilder.ToString().Trim('/');
    }
    private static string ReadDivisions(string raw,ref int division,List<long> denominators)
    {
        var result=new StringBuilder();var square=0;
        for(var index=0;index<raw.Length;index++)
        {
            if(square==0 && raw[index]=='<' && IsCommand(raw,index))
            {
                var end=CommandEnd(raw,index);result.Append(raw,index,end-index+1);index=end;continue;
            }
            if(raw[index]=='[') square++;
            else if(raw[index]==']') square=Math.Max(0,square-1);
            if(square==0 && raw[index]=='{')
            {
                var match=DivisionPattern.Match(raw,index);
                if(match.Success && match.Index==index && int.TryParse(match.Groups[1].Value,out var value))
                {
                    division=value;denominators.Add(value);index+=match.Length-1;continue;
                }
            }
            result.Append(raw[index]);
        }
        return result.ToString();
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
            (left, right) = (right, left % right);
        return Math.Max(1, left);
    }

    private static long Lcm(long left, long right) =>
        checked(left / GreatestCommonDivisor(left, right) * right);

    private readonly record struct OverlayRange(
        int Start, int End, int ContentStart, int ContentEnd);

    private sealed class ParsedStream
    {
        public int LastDivision;
        public Fraction Duration = new(0, 1);
        public readonly List<long> Denominators = new();
        public readonly List<StreamEvent> Events = new();
    }

    private readonly record struct StreamEvent(
        Fraction Time, string Controls, string Notes);

    private sealed class MergedCell
    {
        private readonly StringBuilder controls = new();
        private readonly List<string> notes = new();

        public void Add(StreamEvent item)
        {
            controls.Append(item.Controls);
            if (item.Notes.Length != 0)
                notes.Add(item.Notes);
        }

        public string Build() => controls + string.Join('/', notes);
    }

    private readonly record struct Fraction
    {
        public long Numerator { get; }
        public long Denominator { get; }

        public Fraction(long numerator, long denominator)
        {
            if (denominator == 0)
                throw new DivideByZeroException();
            var divisor = GreatestCommonDivisor(numerator, denominator);
            Numerator = numerator / divisor;
            Denominator = denominator / divisor;
        }

        public static Fraction operator +(Fraction left, Fraction right) =>
            new(checked(left.Numerator * right.Denominator +
                        right.Numerator * left.Denominator),
                checked(left.Denominator * right.Denominator));

        public static bool operator >(Fraction left, Fraction right) =>
            checked(left.Numerator * right.Denominator) >
            checked(right.Numerator * left.Denominator);

        public static bool operator <(Fraction left, Fraction right) => right > left;
    }
}
