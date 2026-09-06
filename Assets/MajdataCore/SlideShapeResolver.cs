using System;

namespace MajdataCore
{
    public enum SlideShapeIssue
    {
        None,
        UnknownShape,
        StraightTooClose,
        SameOrOppositeKey,
        MustEndOpposite,
        MissingTurn,
        TurnNotTwoKeys,
        TurnEndTooClose
    }

    // Which prefab a Slide segment draws with, and which way its guide star faces.
    // View used to answer this twice (once from parsed segments, once by scanning the
    // note text again) and the editor kept a third copy so it could warn before
    // playback. The three grammars drifted: the text versions read fixed substrings,
    // so a D-zone head or a two-digit turn silently threw a format error.
    public static class SlideShapeResolver
    {
        // Mirrors View's slide prefab table. The editor keeps no separate copy, so a
        // shape it accepts is always a shape View can draw.
        public static readonly string[] SupportedPrefabKeys =
        {
            "line3", "line4", "line5", "line6", "line7",
            "circle1", "circle2", "circle3", "circle4",
            "circle5", "circle6", "circle7", "circle8",
            "v1", "v2", "v3", "v4", "v6", "v7", "v8",
            "ppqq1", "ppqq2", "ppqq3", "ppqq4",
            "ppqq5", "ppqq6", "ppqq7", "ppqq8",
            "pq1", "pq2", "pq3", "pq4", "pq5", "pq6", "pq7", "pq8",
            "s", "wifi", "L2", "L3", "L4", "L5"
        };

        // Strips the mirror ('-') and reverse ('r') prefixes the prefab key carries.
        public static string NormalizePrefabKey(string prefabKey)
        {
            if (string.IsNullOrEmpty(prefabKey))
                return string.Empty;
            var key = prefabKey;
            if (key[0] == '-')
                key = key.Substring(1);
            if (key.Length != 0 && key[0] == 'r')
                key = key.Substring(1);
            return key;
        }

        public static bool IsPrefabKeySupported(string prefabKey)
        {
            var key = NormalizePrefabKey(prefabKey);
            foreach (var supported in SupportedPrefabKeys)
                if (string.Equals(supported, key, StringComparison.Ordinal))
                    return true;
            return false;
        }

        public static bool TryResolve(
            SlidePathSegmentData segment,
            out string prefabKey,
            out SlideShapeIssue issue,
            out string error)
        {
            prefabKey = string.Empty;
            issue = SlideShapeIssue.None;
            error = string.Empty;
            if (segment == null)
            {
                issue = SlideShapeIssue.UnknownShape;
                error = SlideSyntaxValidator.Diagnose(
                    "星星缺少路径段",
                    "SLIDE SEGMENT IS MISSING",
                    string.Empty);
                return false;
            }

            var start = segment.startPosition;
            var end = RelativeEnd(start, segment.endPosition);
            var token = segment.ToExpression(true);
            switch (segment.shape)
            {
                case "-":
                    if (end < 3 || end > 7)
                        return Fail(
                            SlideShapeIssue.StraightTooClose,
                            "「-」星星的起终点至少要隔开一键",
                            "STRAIGHT SLIDE NEEDS AT LEAST ONE KEY BETWEEN ITS ENDS",
                            token, out issue, out error);
                    prefabKey = "line" + end;
                    return true;
                case ">":
                    prefabKey = IsUpperHalf(start)
                        ? "circle" + end
                        : "-circle" + MirrorKey(end);
                    return true;
                case "<":
                    prefabKey = IsUpperHalf(start)
                        ? "-circle" + MirrorKey(end)
                        : "circle" + end;
                    return true;
                case "^":
                    if (end is 1 or 5)
                        return Fail(
                            SlideShapeIssue.SameOrOppositeKey,
                            "「^」星星的起终点不能是同键或对键",
                            "'^' SLIDE ENDPOINTS MAY NOT BE THE SAME OR OPPOSITE KEY",
                            token, out issue, out error);
                    prefabKey = end < 5
                        ? "circle" + end
                        : "-circle" + MirrorKey(end);
                    return true;
                case "v":
                    if (end == 5)
                        return Fail(
                            SlideShapeIssue.SameOrOppositeKey,
                            "「v」星星的起终点不能是同键或对键",
                            "'v' SLIDE ENDPOINTS MAY NOT BE THE SAME OR OPPOSITE KEY",
                            token, out issue, out error);
                    prefabKey = "v" + end;
                    return true;
                case "p":
                    prefabKey = "pq" + end;
                    return true;
                case "q":
                    prefabKey = "-pq" + MirrorKey(end);
                    return true;
                case "pp":
                    prefabKey = "ppqq" + end;
                    return true;
                case "qq":
                    prefabKey = "-ppqq" + MirrorKey(end);
                    return true;
                // rp/rq reuse the pp/qq arc of the opposite direction and are then
                // travelled backwards, so their key comes from the reversed span.
                case "rp":
                    prefabKey = "rppqq" +
                                RelativeEnd(segment.endPosition, start);
                    return true;
                case "rq":
                    prefabKey = "-rppqq" +
                                MirrorKey(RelativeEnd(segment.endPosition, start));
                    return true;
                case "s":
                    if (end != 5)
                        return Fail(
                            SlideShapeIssue.MustEndOpposite,
                            "「s」星星必须结束在对键",
                            "'s' SLIDE MUST END AT THE OPPOSITE KEY",
                            token, out issue, out error);
                    prefabKey = "s";
                    return true;
                case "z":
                    if (end != 5)
                        return Fail(
                            SlideShapeIssue.MustEndOpposite,
                            "「z」星星必须结束在对键",
                            "'z' SLIDE MUST END AT THE OPPOSITE KEY",
                            token, out issue, out error);
                    prefabKey = "-s";
                    return true;
                case "w":
                    if (end != 5)
                        return Fail(
                            SlideShapeIssue.MustEndOpposite,
                            "「w」星星必须结束在对键",
                            "'w' SLIDE MUST END AT THE OPPOSITE KEY",
                            token, out issue, out error);
                    prefabKey = "wifi";
                    return true;
                case "V":
                {
                    if (!segment.hasMiddle)
                        return Fail(
                            SlideShapeIssue.MissingTurn,
                            "V 星星缺少拐点",
                            "V SLIDE TURN IS MISSING",
                            token, out issue, out error);
                    var turn = RelativeEnd(start, segment.middlePosition);
                    if (turn == 7)
                    {
                        if (end < 2 || end > 5)
                            return Fail(
                                SlideShapeIssue.TurnEndTooClose,
                                "V 星星的终点必须离拐点至少两键，且不能回到起点",
                                "V SLIDE END MUST BE AT LEAST TWO KEYS FROM ITS TURN",
                                token, out issue, out error);
                        prefabKey = "L" + end;
                        return true;
                    }
                    if (turn == 3)
                    {
                        if (end < 5)
                            return Fail(
                                SlideShapeIssue.TurnEndTooClose,
                                "V 星星的终点必须离拐点至少两键，且不能回到起点",
                                "V SLIDE END MUST BE AT LEAST TWO KEYS FROM ITS TURN",
                                token, out issue, out error);
                        prefabKey = "-L" + MirrorKey(end);
                        return true;
                    }
                    return Fail(
                        SlideShapeIssue.TurnNotTwoKeys,
                        "V 星星的拐点必须隔起点两键",
                        "V SLIDE TURN MUST BE TWO KEYS FROM THE START",
                        token, out issue, out error);
                }
                default:
                    return Fail(
                        SlideShapeIssue.UnknownShape,
                        $"不存在的星星形状「{segment.shape}」",
                        $"UNKNOWN SLIDE SHAPE '{segment.shape}'",
                        token, out issue, out error);
            }
        }

        // The guide star spawns rotated towards one side of the playfield. The
        // reference key differs per shape: ring shapes look at where they started,
        // everything else at where they end.
        public static bool TryGetJustDirection(
            SlidePathSegmentData segment,
            out int endPosition,
            out bool isRight)
        {
            endPosition = 0;
            isRight = true;
            if (segment == null)
                return false;

            endPosition = segment.endPosition;
            switch (segment.shape)
            {
                case ">":
                    isRight = IsUpperHalf(segment.startPosition);
                    return true;
                case "<":
                    isRight = !IsUpperHalf(segment.startPosition);
                    return true;
                case "^":
                    // Exactly four keys apart is the opposite key, which '^' rejects
                    // before it ever reaches here.
                    isRight = RelativeEnd(
                        segment.startPosition, segment.endPosition) - 1 <= 4;
                    return true;
                case "w":
                    isRight = IsUpperHalf(segment.endPosition);
                    return true;
                default:
                    isRight = IsRightHalf(segment.endPosition);
                    return true;
            }
        }

        public static int RelativeEnd(int start, int end)
        {
            var relative = end - start;
            if (relative < 0)
                relative += 8;
            if (relative > 8)
                relative -= 8;
            return relative + 1;
        }

        public static bool IsUpperHalf(int key) => key is 7 or 8 or 1 or 2;

        public static bool IsRightHalf(int key) => key is 1 or 2 or 3 or 4;

        public static int MirrorKey(int key) => key switch
        {
            1 => 1,
            2 => 8,
            3 => 7,
            4 => 6,
            5 => 5,
            6 => 4,
            7 => 3,
            8 => 2,
            _ => key
        };

        private static bool Fail(
            SlideShapeIssue value,
            string chinese,
            string english,
            string token,
            out SlideShapeIssue issue,
            out string error)
        {
            issue = value;
            error = SlideSyntaxValidator.Diagnose(chinese, english, token);
            return false;
        }
    }
}
