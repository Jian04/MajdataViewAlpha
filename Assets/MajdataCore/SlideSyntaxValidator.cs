using System;
using System.Collections.Generic;
using System.Globalization;

namespace MajdataCore
{
    [Serializable]
    public sealed class SlideDurationData
    {
        public string source = string.Empty;
        // A written "[0##8:1]" means no lead-in at all, which is different from a
        // duration that never mentioned a delay, so presence is tracked separately.
        public bool hasDelay;
        public double delay;
        public double? bpm;
        public int? division;
        public int? count;
        public double? seconds;
    }

    public static class SlideSyntaxValidator
    {
        // Every diagnostic in the project uses one shape: a Chinese line, the same
        // sentence in uppercase English, and the offending text in both halves so the
        // editor can say which note failed instead of only what rule broke.
        internal static string Diagnose(string chinese, string english, string token)
        {
            var message = ParserMessageLocale.Pick(chinese, english);
            if (string.IsNullOrEmpty(token))
                return message;
            var separator = ParserMessageLocale.PreferChinese ? "：" : ": ";
            return message + separator + token;
        }

        private static string Describe(SlidePathSegmentData segment)
        {
            if (segment == null)
                return string.Empty;
            try
            {
                return segment.ToExpression(true);
            }
            catch (Exception)
            {
                return segment.shape ?? string.Empty;
            }
        }

        public static bool TryValidate(
            SlidePathData path,
            out string error)
            => TryValidateInternal(path, requireDuration: true, out error);

        public static bool TryValidateForPreview(
            SlidePathData path,
            out string error)
            => TryValidateInternal(path, requireDuration: false, out error);

        private static bool TryValidateInternal(
            SlidePathData path,
            bool requireDuration,
            out string error)
        {
            error = string.Empty;
            if (path == null ||
                path.segments == null ||
                path.segments.Count == 0)
            {
                error = Diagnose(
                    "星星没有任何路径段",
                    "SLIDE PATH IS EMPTY",
                    path?.source ?? string.Empty);
                return false;
            }

            var durationCount = 0;
            var lastDurationIndex = -1;
            var hasTouchSegment = false;
            var modifierRuns = new List<string>();
            for (var index = 0; index < path.segments.Count; index++)
            {
                var segment = path.segments[index];
                if (segment == null ||
                    !TryResolvePosition(
                        segment.start,
                        segment.startPosition,
                        segment.startIsDZone,
                        out var start) ||
                    !TryResolvePosition(
                        segment.end,
                        segment.endPosition,
                        segment.endIsDZone,
                        out var end))
                {
                    error = Diagnose(
                        "星星的起点或终点不是合法位置",
                        "SLIDE START OR END IS NOT A VALID POSITION",
                        Describe(segment!));
                    return false;
                }
                if (!PositionsAgree(
                        segment.start,
                        segment.startPosition,
                        segment.startIsDZone) ||
                    !PositionsAgree(
                        segment.end,
                        segment.endPosition,
                        segment.endIsDZone))
                {
                    error = Diagnose(
                        "星星位置数据与文本不一致（谱面文件可能被改坏）",
                        "SERIALIZED SLIDE POSITION DISAGREES WITH ITS TEXT",
                        Describe(segment));
                    return false;
                }
                if (index > 0 &&
                    (!TryResolvePosition(
                         path.segments[index - 1].end,
                         path.segments[index - 1].endPosition,
                         path.segments[index - 1].endIsDZone,
                         out var previousEnd) ||
                     !SamePosition(previousEnd, start)))
                {
                    error = Diagnose(
                        "组合星星的这一段没有接在上一段的终点上",
                        "CONNECTED SLIDE SEGMENT DOES NOT START AT THE PREVIOUS END",
                        Describe(segment));
                    return false;
                }
                if (!ValidateShape(
                        segment,
                        start,
                        end,
                        path.segments.Count,
                        out error))
                    return false;
                hasTouchSegment |=
                    start.IsTouch ||
                    end.IsTouch ||
                    segment.hasMiddle && segment.middle.IsTouch;
                modifierRuns.Add(segment.modifiers ?? string.Empty);
                if (string.IsNullOrEmpty(segment.duration))
                    continue;
                if (!TryParseDuration(segment.duration, out _))
                {
                    error = Diagnose(
                        "星星时长写法错误",
                        "INVALID SLIDE DURATION",
                        segment.duration);
                    return false;
                }
                durationCount++;
                lastDurationIndex = index;
            }

            if (!NoteModifierParser.TryParseSlideSegments(
                    path.headModifiers ?? string.Empty,
                    path.segments,
                    out _))
            {
                error = Diagnose(
                    "修饰符位置错误：「b」「m」要写在末段，或写在自带时长的那一段；其余修饰符只能写在头部",
                    "SLIDE MODIFIER IS IN THE WRONG PLACE: PUT 'b' OR 'm' ON THE LAST " +
                    "SEGMENT OR ON ONE THAT CARRIES ITS OWN DURATION, AND EVERY " +
                    "OTHER MODIFIER ON THE HEAD",
                    string.IsNullOrEmpty(path.source)
                        ? path.headModifiers ?? string.Empty
                        : path.source);
                return false;
            }

            if ((requireDuration || durationCount > 0) &&
                durationCount != 1 &&
                durationCount != path.segments.Count)
            {
                // A single-segment slide is simply missing its duration; only talk
                // about totals and per-segment durations when there is more than
                // one segment to spread them over.
                error = path.segments.Count <= 1
                    ? Diagnose(
                        "星星要写时长，例如 [8:1]",
                        "SLIDE NEEDS A DURATION, FOR EXAMPLE '[8:1]'",
                        path.source)
                    : Diagnose(
                        $"组合星星只能写一个总时长，或每段各写一个时长（现在写了 {durationCount} 个，共 {path.segments.Count} 段）",
                        "CONNECTED SLIDE NEEDS ONE TOTAL DURATION OR ONE PER SEGMENT",
                        path.source);
                return false;
            }
            // One duration on a multi-segment path is the total for the whole slide,
            // so it belongs at the end. Written on an earlier joint it says nothing
            // about how long the rest of the path takes, and used to be accepted in
            // silence - the remaining segments just inherited a time nobody wrote.
            // While the author is still typing, a missing trailing duration is the
            // normal state and the editor is about to offer one, so this only
            // applies once the path is expected to be complete.
            if (requireDuration &&
                durationCount == 1 &&
                path.segments.Count > 1 &&
                lastDurationIndex != path.segments.Count - 1)
            {
                error = Diagnose(
                    "组合星星的总时长要写在最后一段，不能只写在中间",
                    "A CONNECTED SLIDE'S TOTAL DURATION BELONGS ON ITS LAST SEGMENT",
                    path.source);
                return false;
            }
            return true;
        }

        public static bool TryValidateSegments(
            IReadOnlyList<SlidePathSegmentData> segments,
            out string error)
        {
            if (segments == null || segments.Count == 0)
            {
                error = Diagnose(
                    "星星没有任何路径段",
                    "SLIDE PATH IS EMPTY",
                    string.Empty);
                return false;
            }
            var path = new SlidePathData();
            for (var index = 0; index < segments.Count; index++)
                path.segments.Add(segments[index]);
            return TryValidate(path, out error);
        }

        /// <summary>
        /// A hold may legally have zero length ("1h[1:0]"), which v0.4.2 accepted.
        /// A slide may not: a zero-length path has no travel time to draw.
        /// </summary>
        public static bool TryParseDuration(
            string token,
            out SlideDurationData duration,
            bool allowZeroLength = false)
        {
            duration = new SlideDurationData { source = token ?? string.Empty };
            if (string.IsNullOrEmpty(token) ||
                token.Length < 3 ||
                token[0] != '[' ||
                token[token.Length - 1] != ']')
                return false;

            var values = token.Substring(1, token.Length - 2).Split('#');
            switch (values.Length)
            {
                case 1:
                    return TryParseRatio(
                        values[0], allowZeroLength,
                        out duration.division, out duration.count);
                case 2:
                {
                    // "[#2]" is a plain duration in seconds with no BPM of its own.
                    if (values[0].Length == 0)
                    {
                        if (!TryPositiveDouble(values[1], out var seconds))
                            return false;
                        duration.seconds = seconds;
                        return true;
                    }
                    if (!TryPositiveDouble(values[0], out var bpm))
                        return false;
                    duration.bpm = bpm;
                    return TryParseLength(values[1], duration, allowZeroLength);
                }
                case 3:
                {
                    if (!TryNonNegativeDouble(values[0], out var delay) ||
                        values[1].Length != 0)
                        return false;
                    duration.hasDelay = true;
                    duration.delay = delay;
                    return TryParseLength(values[2], duration, allowZeroLength);
                }
                case 4:
                {
                    if (!TryNonNegativeDouble(values[0], out var delay) ||
                        values[1].Length != 0 ||
                        !TryPositiveDouble(values[2], out var bpm) ||
                        !TryParseRatio(
                            values[3],
                            allowZeroLength,
                            out duration.division,
                            out duration.count))
                        return false;
                    duration.hasDelay = true;
                    duration.delay = delay;
                    duration.bpm = bpm;
                    return true;
                }
                default:
                    return false;
            }
        }

        public static bool TryGetLengthSeconds(
            string token,
            double currentBpm,
            out double seconds)
        {
            seconds = 0d;
            // Length is only being measured here, so a zero-length hold must
            // report 0 rather than fail; kind rules are enforced by the parser.
            if (!TryParseDuration(token, out var duration, allowZeroLength: true))
                return false;
            if (duration.seconds.HasValue)
            {
                seconds = duration.seconds.Value;
                return true;
            }
            if (!duration.division.HasValue || !duration.count.HasValue)
                return false;

            var bpm = duration.bpm ?? currentBpm;
            if (bpm <= 0d)
                return false;
            seconds =
                60d / bpm * 4d /
                duration.division.Value * duration.count.Value;
            return true;
        }

        private static bool ValidateShape(
            SlidePathSegmentData segment,
            SlidePositionData startPosition,
            SlidePositionData endPosition,
            int segmentCount,
            out string error)
        {
            error = string.Empty;
            if (segment.shape == "SC")
            {
                if (!SlideCodeParser.TryParse(
                        segment.slideCode, out _, out error))
                    return false;
                return true;
            }
            if (!IsKnownShape(segment.shape))
            {
                error = Diagnose(
                    $"不存在的星星形状「{segment.shape}」",
                    $"UNKNOWN SLIDE SHAPE '{segment.shape}'",
                    Describe(segment));
                return false;
            }

            SlidePositionData? middlePosition = null;
            if (segment.shape is "V" or "P" or "Q")
            {
                if (!segment.hasMiddle ||
                    !TryResolvePosition(
                        segment.middle,
                        segment.middlePosition,
                        segment.middleIsDZone,
                        out middlePosition) ||
                    !PositionsAgree(
                        segment.middle,
                        segment.middlePosition,
                        segment.middleIsDZone))
                {
                    error = Diagnose(
                        segment.shape == "V"
                            ? "V 星星缺少拐点，或拐点写法错误（例 1V35）"
                            : $"{segment.shape} 星星缺少绕行中心（0 对应中央圈，1-8 对应侧边圈，9 对应最外圈）",
                        segment.shape == "V"
                            ? "V SLIDE TURN IS MISSING OR INVALID"
                            : $"{segment.shape} SLIDE ORBIT CENTER IS MISSING OR INVALID",
                        Describe(segment));
                    return false;
                }
            }
            else if (segment.hasMiddle)
            {
                error = Diagnose(
                    "只有 V、P、Q 星星可以写中间位置",
                    "ONLY V, P, AND Q SLIDES MAY CONTAIN A MIDDLE POSITION",
                    Describe(segment));
                return false;
            }

            var isTouchSegment =
                startPosition.IsTouch ||
                endPosition.IsTouch ||
                middlePosition?.IsTouch == true;
            if (isTouchSegment)
            {
                // rp and rq are drawn by TouchSlideDrop from the same inherited route
                // as pp and qq, so they belong here no more than those two do. What
                // stays excluded genuinely has no touch geometry: the wifi fan and
                // the fixed thunder shapes are tied to key positions.
                if (segment.shape is "w" or "r" or "s" or "z")
                {
                    error = Diagnose(
                        $"TouchSlide 不支持「{segment.shape}」形状",
                        $"TOUCH SLIDE DOES NOT SUPPORT SHAPE '{segment.shape}'",
                        Describe(segment));
                    return false;
                }

                // Touch geometry is sampled from the two ends rather than picked from a
                // prefab, so a segment whose ends are one point samples that point over
                // and over: the chart is accepted, the trail is zero length and nothing
                // is drawn or reported. Key segments already refuse this; these are the
                // same refusals for the shapes that only touch slides can reach.
                if (SamePoint(startPosition, endPosition))
                {
                    if (segment.shape is "-" or "^")
                    {
                        error = Diagnose(
                            $"「{segment.shape}」星星的起终点不能是同一个位置，" +
                            "绕整圈要写成 < 或 >（例 B3<B3）",
                            $"'{segment.shape}' SLIDE MAY NOT START AND END AT ONE " +
                            "POSITION, WRITE '<' OR '>' FOR A FULL CIRCLE",
                            Describe(segment));
                        return false;
                    }
                    // Every other shape circles or detours, which needs somewhere to
                    // circle around. C is a single point at the centre, so it has none,
                    // unless a V names a turn outside it.
                    if (startPosition.area == 'C' &&
                        !(segment.shape is "V" or "P" or "Q" &&
                          middlePosition != null &&
                          !SamePoint(middlePosition, startPosition)))
                    {
                        error = Diagnose(
                            "C 区只有一个点，绕不出轨迹，这一段没有长度",
                            "THE C AREA IS ONE POINT, SO THIS SEGMENT HAS NO LENGTH",
                            Describe(segment));
                        return false;
                    }
                }
                return true;
            }

            if (segment.shape.Length > 1 &&
                segment.shape[0] is '<' or '>')
            {
                error = Diagnose(
                    "连写的 < 或 > 只能用在 TouchSlide 上",
                    "REPEATED < OR > IS TOUCH-SLIDE-ONLY",
                    Describe(segment));
                return false;
            }
            if (segment.shape == "w" && segmentCount > 1)
            {
                error = Diagnose(
                    "Wifi 星星不能作为组合星星的一段",
                    "WIFI SLIDE CANNOT BE PART OF A CONNECTED SLIDE",
                    Describe(segment));
                return false;
            }

            var start = startPosition.position;
            var end = endPosition.position;
            switch (segment.shape)
            {
                case "^":
                case "v":
                    if (GetInterval(start, end) is 0 or 4)
                    {
                        error = Diagnose(
                            $"「{segment.shape}」星星的起终点不能是同键或对键",
                            $"'{segment.shape}' SLIDE ENDPOINTS MAY NOT BE THE SAME OR OPPOSITE KEY",
                            Describe(segment));
                        return false;
                    }
                    break;
                case "-":
                    if (GetInterval(start, end) < 2)
                    {
                        error = Diagnose(
                            "「-」星星的起终点至少要隔开一键",
                            "STRAIGHT SLIDE NEEDS AT LEAST ONE KEY BETWEEN ITS ENDS",
                            Describe(segment));
                        return false;
                    }
                    break;
                case "V":
                    if (GetInterval(start, middlePosition!.position) != 2 ||
                        GetInterval(middlePosition.position, end) < 2 ||
                        start == end)
                    {
                        error = Diagnose(
                            "V 星星的拐点必须隔起点两键，终点必须离拐点至少两键且不能回到起点",
                            "V SLIDE TURN MUST BE TWO KEYS FROM THE START AND MAY NOT RETURN TO IT",
                            Describe(segment));
                        return false;
                    }
                    break;
                case "s":
                case "z":
                case "w":
                    if (GetInterval(start, end) != 4)
                    {
                        error = Diagnose(
                            $"「{segment.shape}」星星必须结束在对键",
                            $"'{segment.shape}' SLIDE MUST END AT THE OPPOSITE KEY",
                            Describe(segment));
                        return false;
                    }
                    break;
            }
            return true;
        }

        private static bool IsKnownShape(string shape)
        {
            if (string.IsNullOrEmpty(shape))
                return false;
            if (shape.Length > 1 &&
                shape[0] is '<' or '>')
            {
                for (var index = 1; index < shape.Length; index++)
                    if (shape[index] != shape[0])
                        return false;
                return true;
            }
            return shape is
                "-" or "<" or ">" or "^" or "v" or "V" or "P" or "Q" or "SC" or
                "p" or "q" or "pp" or "qq" or "rp" or "rq" or
                "s" or "z" or "w";
        }

        private static bool TryResolvePosition(
            SlidePositionData parsed,
            int legacyPosition,
            bool legacyDZone,
            out SlidePositionData position)
        {
            position =
                parsed != null && parsed.position != 0
                    ? parsed
                    : new SlidePositionData
                    {
                        area = 'K',
                        position = legacyPosition,
                        isDZone = legacyDZone
                    };
            if (position.isDZone && position.area != 'K')
                return false;
            return position.area switch
            {
                'K' => IsKey(position.position),
                'C' => position.position == 8,
                'O' => position.position == 9,
                'A' or 'B' or 'D' or 'E' => IsKey(position.position),
                _ => false
            };
        }

        private static bool PositionsAgree(
            SlidePositionData parsed,
            int legacyPosition,
            bool legacyDZone)
        {
            return parsed == null ||
                   parsed.position == 0 ||
                   legacyPosition == 0 ||
                   parsed.position == legacyPosition &&
                   parsed.isDZone == legacyDZone;
        }

        private static bool SamePosition(
            SlidePositionData left,
            SlidePositionData right) =>
            left.area == right.area &&
            left.position == right.position &&
            left.isDZone == right.isDZone;

        private static bool TryParseLength(
            string value,
            SlideDurationData duration,
            bool allowZeroLength = false)
        {
            if (value.IndexOf(':') >= 0)
                return TryParseRatio(
                    value, allowZeroLength,
                    out duration.division, out duration.count);
            if (!TryPositiveDouble(value, out var seconds))
                return false;
            duration.seconds = seconds;
            return true;
        }

        private static bool TryParseRatio(
            string value,
            bool allowZeroCount,
            out int? division,
            out int? count)
        {
            division = null;
            count = null;
            var values = value.Split(':');
            if (values.Length != 2 ||
                !int.TryParse(
                    values[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsedDivision) ||
                parsedDivision <= 0 ||
                !int.TryParse(
                    values[1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsedCount) ||
                parsedCount < 0 ||
                parsedCount == 0 && !allowZeroCount)
                return false;
            division = parsedDivision;
            count = parsedCount;
            return true;
        }

        private static bool TryPositiveDouble(
            string value,
            out double result) =>
            double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result) &&
            !double.IsNaN(result) &&
            !double.IsInfinity(result) &&
            result > 0d;

        private static bool TryNonNegativeDouble(
            string value,
            out double result) =>
            double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result) &&
            !double.IsNaN(result) &&
            !double.IsInfinity(result) &&
            result >= 0d;

        private static bool IsKey(int value) => value is >= 1 and <= 8;

        /// <summary>
        /// Whether two resolved positions are the same point on the screen. C1 and C2
        /// both read as the centre, so they are one point too.
        /// </summary>
        private static bool SamePoint(SlidePositionData a, SlidePositionData b) =>
            a.area == b.area && a.position == b.position && a.isDZone == b.isDZone;

        private static int GetInterval(int start, int end)
        {
            var difference = Math.Abs(start - end);
            return Math.Min(difference, 8 - difference);
        }
    }
}
