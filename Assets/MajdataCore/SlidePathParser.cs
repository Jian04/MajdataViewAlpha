using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MajdataCore
{
    [Serializable]
    public sealed class SlidePositionData
    {
        public char area = 'K';
        public int position;
        public bool isDZone;
        public string source = string.Empty;
        // 0 keeps the area's authored distance; a positive value places the Note at
        // that distance along the same direction, in the radius unit shared with
        // SPAWN/DESTROY (see AlphaVisualTiming: 4.8 is the judgement ring).
        public float radius;
        // An image file, relative to the chart's own folder, that replaces this one
        // Note's skin. Shares the "~[...]" suffix with the radius above because the
        // two can never be confused: a radius is a bare number, a skin has a
        // recognised image extension.
        public string skin = string.Empty;

        public bool IsKey => area == 'K';
        public bool IsTouch => area != 'K';
        public bool HasCustomRadius => radius > 0f;
        public bool HasSkin => !string.IsNullOrEmpty(skin);

        public string ToExpression()
        {
            if (!string.IsNullOrEmpty(source))
                return source;
            var text = area switch
            {
                'C' => "C",
                'K' => $"{position}{(isDZone ? "d" : string.Empty)}",
                _ => $"{area}{position}"
            };
            return HasCustomRadius
                ? text + SlidePathParser.FormatRadiusOverride(radius)
                : text;
        }
    }

    [Serializable]
    public sealed class SlidePathData
    {
        public string source = string.Empty;
        public SlidePositionData head = new();
        public string headModifiers = string.Empty;
        public List<SlidePathSegmentData> segments = new();
        public bool isTouchPath;
    }

    [Serializable]
    public sealed class SlidePathSegmentData
    {
        public SlidePositionData start = new();
        public SlidePositionData middle = new();
        public SlidePositionData end = new();
        public int startPosition;
        public bool startIsDZone;
        public string shape = string.Empty;
        public bool hasMiddle;
        public int middlePosition;
        public bool middleIsDZone;
        public int endPosition;
        public bool endIsDZone;
        public string modifiers = string.Empty;
        public string duration = string.Empty;
        public string slideCode = string.Empty;

        public string ToExpression(bool includeDZone)
        {
            if (!string.IsNullOrEmpty(slideCode))
                return slideCode + modifiers + duration;
            var result = new StringBuilder();
            AppendPosition(
                result, start, startPosition, startIsDZone, includeDZone);
            result.Append(shape);
            if (hasMiddle)
            {
                if (shape is "P" or "Q" &&
                    TryFormatOrbitSelector(middle, out var selector))
                    result.Append(selector);
                else
                    AppendPosition(
                        result, middle, middlePosition, middleIsDZone, includeDZone);
            }
            AppendPosition(
                result, end, endPosition, endIsDZone, includeDZone);
            result.Append(modifiers);
            result.Append(duration);
            return result.ToString();
        }

        private static bool TryFormatOrbitSelector(
            SlidePositionData position, out char selector)
        {
            selector = '\0';
            if (position == null)
                return false;
            if (position.source == "0" && position.area == 'C')
            {
                selector = '0';
                return true;
            }
            if (position.source == "9" && position.area == 'O')
            {
                selector = '9';
                return true;
            }
            if (position.source == null || position.source.Length != 1 ||
                position.source[0] is < '1' or > '8' ||
                position.area is not ('B' or 'E') ||
                position.position != position.source[0] - '0')
                return false;
            selector = position.source[0];
            return true;
        }

        private static void AppendPosition(
            StringBuilder result,
            SlidePositionData parsed,
            int position,
            bool isDZone,
            bool includeDZone)
        {
            if (parsed != null && parsed.position != 0)
            {
                if (parsed.area == 'C')
                {
                    result.Append('C');
                    return;
                }
                if (parsed.area != 'K')
                {
                    result.Append(parsed.area);
                    result.Append(parsed.position);
                    return;
                }
                position = parsed.position;
                isDZone = parsed.isDZone;
            }
            result.Append(position);
            if (includeDZone && isDZone)
                result.Append('d');
        }
    }

    public enum RadiusOverrideResult
    {
        None,
        Applied,
        Invalid,
        InvalidSkin
    }

    public static class SlidePathParser
    {
        private const string Modifiers = "bxfm!?$";
        // A typo like ~[48] must not silently throw the Note off screen; the ring
        // itself is at 4.8, so anything past this is a mistake, not intent.
        public const float MaxRadiusOverride = 10f;

        public static bool TryReadPosition(
            string text,
            int offset,
            out SlidePositionData position,
            out int nextOffset)
        {
            position = new SlidePositionData();
            nextOffset = offset;
            if (string.IsNullOrEmpty(text) ||
                offset < 0 ||
                offset >= text.Length)
                return false;

            var start = offset;
            if (text[offset] is >= '1' and <= '8')
            {
                position.area = 'K';
                position.position = text[offset++] - '0';
                position.isDZone = offset < text.Length && text[offset] == 'd';
                if (position.isDZone)
                    offset++;
            }
            else if (text[offset] == 'C')
            {
                position.area = 'C';
                position.position = 8;
                offset++;
                if (offset < text.Length &&
                    (text[offset] == '1' || text[offset] == '2'))
                    offset++;
            }
            else if (text[offset] is 'A' or 'B' or 'D' or 'E' &&
                     offset + 1 < text.Length &&
                     text[offset + 1] is >= '1' and <= '8')
            {
                position.area = text[offset++];
                position.position = text[offset++] - '0';
            }
            else
            {
                return false;
            }

            nextOffset = offset;
            position.source = text.Substring(start, offset - start);
            return true;
        }

        public static string FormatRadiusOverride(float radius) =>
            "~[" + radius.ToString("0.####", CultureInfo.InvariantCulture) + "]";

        private static readonly string[] SkinExtensions = { ".png", ".jpg", ".jpeg" };

        // Tells a skin apart from a radius by its extension alone, so an unreadable
        // "~[star.bmp]" is still reported as a bad skin rather than a bad number.
        public static bool LooksLikeSkinPath(string body)
        {
            if (string.IsNullOrEmpty(body))
                return false;
            var dot = body.LastIndexOf('.');
            if (dot <= 0 || dot >= body.Length - 1)
                return false;
            // The extension has to be letters only, or a decimal distance like "4.8"
            // reads as a file named "4" with extension "8".
            for (var i = dot + 1; i < body.Length; i++)
                if (!char.IsLetter(body[i]))
                    return false;
            return true;
        }

        // The path is resolved against the chart's own folder, so it may name a
        // subfolder but must not climb out of it or reach for an absolute location.
        public static bool IsSkinPathUsable(string body, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(body))
                return false;

            var cleaned = body.Trim().Replace('\\', '/');
            if (cleaned.StartsWith("/", StringComparison.Ordinal) ||
                cleaned.Contains(":") ||
                cleaned.Contains("//"))
                return false;

            var recognised = false;
            foreach (var extension in SkinExtensions)
            {
                if (cleaned.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    recognised = true;
                    break;
                }
            }
            if (!recognised)
                return false;

            foreach (var part in cleaned.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                    return false;
            }

            normalized = cleaned;
            return true;
        }

        /// <summary>
        /// Reads the "~[5-7[8:1]]" suffix that borrows another slide's star
        /// trajectory.
        /// </summary>
        /// <remarks>
        /// The same "~[...]" spelling already carries a Touch's distance and its
        /// picture, so the three are told apart by what is written inside: a slide
        /// path is a trajectory, a file name is a picture, a plain number is a
        /// distance. The brackets nest here, because the borrowed path brings its
        /// own [8:1] along, so the closing bracket is matched rather than searched
        /// for - the first ']' in "~[5-7[8:1]]" is not the end of anything.
        /// </remarks>
        public static bool TryReadTrajectoryBorrow(
            string text,
            int offset,
            out string expression,
            out int nextOffset)
        {
            expression = string.Empty;
            nextOffset = offset;
            if (string.IsNullOrEmpty(text) ||
                offset < 0 ||
                offset + 1 >= text.Length ||
                text[offset] != '~' ||
                text[offset + 1] != '[')
                return false;

            var depth = 0;
            for (var index = offset + 1; index < text.Length; index++)
            {
                if (text[index] == '[')
                {
                    depth++;
                    continue;
                }
                if (text[index] != ']')
                    continue;
                depth--;
                if (depth > 0)
                    continue;

                var body = text.Substring(offset + 2, index - offset - 2);
                // A picture may well be called "my-star.png", and that dash is a
                // slide shape to anything that only looks for shape characters.
                if (LooksLikeSkinPath(body) || !ContainsSlideShape(body))
                    return false;
                expression = body;
                nextOffset = index + 1;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Lifts a borrowed trajectory out of a note, leaving the note behind.
        /// </summary>
        /// <remarks>
        /// Taking it out in one place means every reader after this one - the
        /// position, the modifiers, the duration - goes on seeing the plain note it
        /// already knows how to read, and the borrow may be written before or after
        /// the modifiers without any of them caring.
        /// </remarks>
        public static bool TryTakeTrajectoryBorrow(
            string expression, out string borrowed, out string remainder)
        {
            borrowed = string.Empty;
            remainder = expression ?? string.Empty;
            if (string.IsNullOrEmpty(expression))
                return false;
            for (var index = 0; index < expression.Length; index++)
            {
                if (expression[index] != '~' ||
                    !TryReadTrajectoryBorrow(
                        expression, index, out var body, out var next))
                    continue;
                borrowed = body;
                remainder = expression.Substring(0, index) + expression.Substring(next);
                return true;
            }
            return false;
        }

        // Reads the "~[distance]" suffix that overrides where a position is drawn.
        // The bracket belongs to the position, so callers must consume it here
        // before looking for a Hold duration, or the two brackets get confused.
        public static RadiusOverrideResult TryReadRadiusOverride(
            string text,
            int offset,
            SlidePositionData position,
            out int nextOffset)
        {
            nextOffset = offset;
            if (string.IsNullOrEmpty(text) ||
                offset < 0 ||
                offset >= text.Length ||
                text[offset] != '~')
                return RadiusOverrideResult.None;

            var open = offset + 1;
            if (open >= text.Length || text[open] != '[')
                return RadiusOverrideResult.Invalid;
            var close = text.IndexOf(']', open + 1);
            if (close < 0)
                return RadiusOverrideResult.Invalid;

            var body = text.Substring(open + 1, close - open - 1);

            // A body carrying a file extension is a skin, not a distance. Deciding
            // here keeps the two apart for every caller and means a misspelt file
            // name is never reported as a malformed number.
            if (LooksLikeSkinPath(body))
            {
                if (!IsSkinPathUsable(body, out var skin))
                    return RadiusOverrideResult.InvalidSkin;
                position.skin = skin;
                position.source += text.Substring(offset, close - offset + 1);
                nextOffset = close + 1;
                return RadiusOverrideResult.Applied;
            }

            if (!float.TryParse(
                    body,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var radius) ||
                float.IsNaN(radius) ||
                float.IsInfinity(radius) ||
                radius <= 0f ||
                radius > MaxRadiusOverride)
                return RadiusOverrideResult.Invalid;

            position.radius = radius;
            position.source += text.Substring(offset, close - offset + 1);
            nextOffset = close + 1;
            return RadiusOverrideResult.Applied;
        }

        public static bool TryParse(
            string expression,
            out List<SlidePathSegmentData> segments)
        {
            if (TryParsePath(expression, out var path))
            {
                segments = path.segments;
                return true;
            }
            segments = new List<SlidePathSegmentData>();
            return false;
        }

        /// <param name="tokens">
        /// Optional. Collects where each piece of the path sits in
        /// <paramref name="expression"/>, for callers that need to highlight it.
        /// Left null by the runtime, which only wants the parsed result.
        /// </param>
        public static bool TryParsePath(
            string expression,
            out SlidePathData path,
            ChartTokenList? tokens = null)
        {
            path = new SlidePathData { source = expression ?? string.Empty };
            if (string.IsNullOrEmpty(expression))
                return false;
            if (TryParseSlideCodePath(expression, path, tokens))
                return true;
            if (!TryReadPosition(
                    expression, 0, out var current, out var offset))
                return false;
            path.head = current;
            if (tokens != null)
                tokens.Add(0, offset, ChartTokenKind.Position);

            var headModifierStart = offset;
            while (offset < expression.Length &&
                   Modifiers.IndexOf(expression[offset]) >= 0)
                offset++;
            path.headModifiers = expression.Substring(
                headModifierStart, offset - headModifierStart);
            if (tokens != null)
                tokens.Add(
                    headModifierStart,
                    offset - headModifierStart,
                    ChartTokenKind.Modifier);

            while (offset < expression.Length)
            {
                var shapeStart = offset;
                if (!TryReadShape(expression, ref offset, out var shape))
                    return false;
                if (tokens != null)
                    tokens.Add(
                        shapeStart, offset - shapeStart, ChartTokenKind.Shape);

                var segment = new SlidePathSegmentData
                {
                    start = current,
                    startPosition = current.position,
                    startIsDZone = current.isDZone,
                    shape = shape
                };
                if (shape is "V" or "P" or "Q")
                {
                    var middleStart = offset;
                    var readMiddle = shape is "P" or "Q"
                        ? TryReadOrbitSelector(
                            expression, offset, out var middle, out offset)
                        : TryReadPosition(
                            expression, offset, out middle, out offset);
                    if (!readMiddle)
                        return false;
                    if (tokens != null)
                        tokens.Add(
                            middleStart,
                            offset - middleStart,
                            ChartTokenKind.Position);
                    segment.hasMiddle = true;
                    segment.middle = middle;
                    segment.middlePosition = middle.position;
                    segment.middleIsDZone = middle.isDZone;
                }

                var endStart = offset;
                if (!TryReadPosition(
                        expression, offset, out var end, out offset))
                    return false;
                if (tokens != null)
                    tokens.Add(
                        endStart, offset - endStart, ChartTokenKind.Position);
                segment.end = end;
                segment.endPosition = end.position;
                segment.endIsDZone = end.isDZone;
                current = end;

                var modifierStart = offset;
                while (offset < expression.Length &&
                       Modifiers.IndexOf(expression[offset]) >= 0)
                    offset++;
                segment.modifiers = expression.Substring(
                    modifierStart, offset - modifierStart);
                if (tokens != null)
                    tokens.Add(
                        modifierStart,
                        offset - modifierStart,
                        ChartTokenKind.Modifier);

                if (offset < expression.Length && expression[offset] == '[')
                {
                    var durationStart = offset++;
                    while (offset < expression.Length && expression[offset] != ']')
                        offset++;
                    if (offset >= expression.Length)
                        return false;
                    offset++;
                    segment.duration = expression.Substring(
                        durationStart, offset - durationStart);
                    if (tokens != null)
                        tokens.Add(
                            durationStart,
                            offset - durationStart,
                            ChartTokenKind.Duration);

                    // 4.4-compatible slide notation may place body modifiers
                    // after the duration (1-5[8:1]b). Normalize that form into
                    // the same segment modifier run as 1-5b[8:1]; Mine follows
                    // the same rule.
                    var suffixStart = offset;
                    while (offset < expression.Length &&
                           Modifiers.IndexOf(expression[offset]) >= 0)
                        offset++;
                    if (offset > suffixStart)
                    {
                        segment.modifiers += expression.Substring(
                            suffixStart, offset - suffixStart);
                        if (tokens != null)
                            tokens.Add(
                                suffixStart,
                                offset - suffixStart,
                                ChartTokenKind.Modifier);
                    }
                }

                path.segments.Add(segment);
            }

            path.isTouchPath =
                path.head.IsTouch ||
                path.segments.Exists(segment =>
                    segment.start.IsTouch ||
                    segment.end.IsTouch ||
                    segment.hasMiddle && segment.middle.IsTouch);
            return path.segments.Count > 0;
        }

        public static bool ContainsSlideShape(string expression)
        {
            if (SlideCodeParser.LooksLikeSlideCode(expression))
                return true;
            var durationStart = expression.IndexOf('[');
            var end = durationStart < 0 ? expression.Length : durationStart;
            for (var index = 0; index < end; index++)
                if ("-<>^vVPQpqrszw".IndexOf(expression[index]) >= 0)
                    return true;
            return false;
        }

        public static string RemoveDZoneSuffixes(string expression)
        {
            if (string.IsNullOrEmpty(expression) ||
                expression.IndexOf('d') < 0)
                return expression;
            var result = new StringBuilder(expression.Length);
            for (var index = 0; index < expression.Length; index++)
            {
                if (expression[index] == 'd' &&
                    index > 0 &&
                    expression[index - 1] is >= '1' and <= '8')
                    continue;
                result.Append(expression[index]);
            }
            return result.ToString();
        }

        public static bool TryExpandSameHead(
            string expression,
            out List<string> branches)
        {
            branches = new List<string>();
            if (string.IsNullOrEmpty(expression))
                return false;

            var rawBranches = expression.Split('*');
            if (rawBranches.Length < 2 ||
                !TryReadPosition(
                    rawBranches[0], 0, out var head, out _))
                return false;

            branches.Add(rawBranches[0]);
            var headExpression = head.ToExpression();
            for (var index = 1; index < rawBranches.Length; index++)
            {
                if (string.IsNullOrEmpty(rawBranches[index]))
                {
                    branches.Clear();
                    return false;
                }
                branches.Add(headExpression + rawBranches[index]);
            }
            return true;
        }

        private static bool TryReadShape(
            string expression,
            ref int offset,
            out string shape)
        {
            shape = string.Empty;
            if (offset >= expression.Length ||
                "-<>^vVPQpqrszw".IndexOf(expression[offset]) < 0)
                return false;

            var first = expression[offset++];
            shape = first.ToString();
            if ((first == '<' || first == '>') &&
                offset < expression.Length &&
                expression[offset] == first)
            {
                while (offset < expression.Length &&
                       expression[offset] == first)
                    shape += expression[offset++];
            }
            else if ((first == 'p' || first == 'q') &&
                offset < expression.Length &&
                expression[offset] == first)
                shape += expression[offset++];
            else if (first == 'r' &&
                     offset < expression.Length &&
                     (expression[offset] == 'p' || expression[offset] == 'q'))
                shape += expression[offset++];
            else if (first == 'r')
                return false;
            return true;
        }

        private static bool TryReadOrbitSelector(
            string expression,
            int offset,
            out SlidePositionData position,
            out int nextOffset)
        {
            position = new SlidePositionData();
            nextOffset = offset;
            if (string.IsNullOrEmpty(expression) ||
                offset < 0 || offset >= expression.Length)
                return false;

            var selector = expression[offset];
            if (selector is >= '1' and <= '8')
            {
                position.position = selector - '0';
                position.area = position.position <= 4 ? 'B' : 'E';
            }
            else if (selector == '0')
            {
                position.area = 'C';
                position.position = 8;
            }
            else if (selector == '9')
            {
                position.area = 'O';
                position.position = 9;
            }
            else if (TryReadPosition(
                         expression, offset, out position, out nextOffset) &&
                     position.IsTouch)
            {
                return true;
            }
            else
            {
                return false;
            }

            position.source = selector.ToString();
            nextOffset = offset + 1;
            return true;
        }

        private static bool TryParseSlideCodePath(
            string expression,
            SlidePathData path,
            ChartTokenList? tokens)
        {
            if (!SlideCodeParser.LooksLikeSlideCode(expression) ||
                !TryReadPosition(expression, 0, out var head, out var offset) ||
                !head.IsKey || head.isDZone)
                return false;

            var headModifierStart = offset;
            while (offset < expression.Length &&
                   Modifiers.IndexOf(expression[offset]) >= 0)
                offset++;
            var headModifiers = expression.Substring(
                headModifierStart, offset - headModifierStart);

            var durationStart = expression.IndexOf('[', offset);
            var bodyEnd = durationStart < 0 ? expression.Length : durationStart;
            var tailModifierStart = bodyEnd;
            while (tailModifierStart > offset &&
                   Modifiers.IndexOf(expression[tailModifierStart - 1]) >= 0)
                tailModifierStart--;
            var codeText = expression.Substring(0, headModifierStart) +
                           expression.Substring(offset, tailModifierStart - offset);
            if (!SlideCodeParser.TryParse(codeText, out var code, out _))
                return false;

            var duration = string.Empty;
            var suffixModifiers = string.Empty;
            if (durationStart >= 0)
            {
                var close = expression.IndexOf(']', durationStart + 1);
                if (close < 0)
                    return false;
                duration = expression.Substring(durationStart, close - durationStart + 1);
                var suffixStart = close + 1;
                var suffixEnd = suffixStart;
                while (suffixEnd < expression.Length &&
                       Modifiers.IndexOf(expression[suffixEnd]) >= 0)
                    suffixEnd++;
                if (suffixEnd != expression.Length)
                    return false;
                suffixModifiers = expression.Substring(suffixStart, suffixEnd - suffixStart);
            }

            var tailModifiers = expression.Substring(
                tailModifierStart, bodyEnd - tailModifierStart) + suffixModifiers;
            var endInstruction = code.instructions[^1];
            var end = new SlidePositionData
            {
                area = 'K',
                position = endInstruction.parameter,
                source = endInstruction.parameter.ToString(CultureInfo.InvariantCulture)
            };
            path.head = head;
            path.headModifiers = headModifiers;
            path.isTouchPath = true;
            path.segments.Add(new SlidePathSegmentData
            {
                start = head,
                startPosition = head.position,
                end = end,
                endPosition = end.position,
                shape = "SC",
                modifiers = tailModifiers,
                duration = duration,
                slideCode = codeText
            });
            if (tokens != null)
            {
                tokens.Add(0, headModifierStart, ChartTokenKind.Position);
                tokens.Add(
                    headModifierStart,
                    headModifiers.Length,
                    ChartTokenKind.Modifier);
                tokens.Add(
                    offset,
                    tailModifierStart - offset,
                    ChartTokenKind.Shape);
                tokens.Add(
                    tailModifierStart,
                    bodyEnd - tailModifierStart,
                    ChartTokenKind.Modifier);
                if (durationStart >= 0)
                    tokens.Add(
                        durationStart,
                        duration.Length,
                        ChartTokenKind.Duration);
            }
            return true;
        }
    }
}
