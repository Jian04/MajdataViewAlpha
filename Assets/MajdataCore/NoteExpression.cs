using System;
using System.Collections.Generic;

namespace MajdataCore
{
    public enum NoteExpressionKind
    {
        Tap,
        Hold,
        Touch,
        TouchHold,
        Slide
    }

    [Serializable]
    public sealed class NoteExpressionData
    {
        public string source = string.Empty;
        public NoteExpressionKind kind;
        public SlidePositionData position = new();
        public ParsedNoteModifiers modifiers;
        // Hold duration token including its brackets, empty for every other kind.
        public string duration = string.Empty;
        // "1h" with no bracket: a Hold that lasts until the next judgement frame.
        public bool isZeroLengthHold;
        public bool isTouchPath;
        // Set only when kind is Slide; the path keeps its own segment durations.
        public SlidePathData path = null!;
        // "1~[5-7[8:1]]": the star trajectory this note borrows. The note keeps its
        // own kind and modifiers - they say what the travelling note looks like -
        // and the borrowed path says where it goes. Null for every ordinary note.
        public SlidePathData? trajectory;
        public string trajectorySource = string.Empty;

        public bool IsHold =>
            kind == NoteExpressionKind.Hold ||
            kind == NoteExpressionKind.TouchHold;
    }

    public struct NoteSlotEntry
    {
        public string text;
        // Branches of a `*` group: every one of them must be a Slide, and they
        // inherit the head's appearance.
        public bool fromSameHead;
        // Branches of one `*` group share this index. A group stands or falls
        // together, otherwise a bad head would leave its tail on the playfield.
        public int groupIndex;
    }

    // A timing slot holds one or more notes. Splitting it used to be repeated by the
    // runtime, the syntax check and the preview, and they did not agree: the runtime
    // read "12" as two taps while the editor marked it as an error.
    public static class NoteSlotParser
    {
        // How a slot divides into authored notes, with `*` groups left intact for
        // callers that render them as a group (the preview does).
        public static List<string> SplitTopLevel(string slot)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(slot))
                return parts;

            // Legacy two-key shorthand: "12" means key 1 and key 2 together.
            if (slot.Length == 2 && int.TryParse(slot, out _))
            {
                parts.Add(slot.Substring(0, 1));
                parts.Add(slot.Substring(1, 1));
                return parts;
            }

            parts.AddRange(slot.Split('/'));
            return parts;
        }

        public static bool TrySplit(
            string slot,
            out List<NoteSlotEntry> notes,
            out string error)
        {
            notes = new List<NoteSlotEntry>();
            error = string.Empty;
            if (string.IsNullOrEmpty(slot))
                return true;

            var group = 0;
            var ok = true;
            foreach (var authored in SplitTopLevel(slot))
            {
                // Must run before the '*' split, or "1<SV*1>" is torn into two
                // fragments and reported as a broken slide path.
                var part = NoteExpressionParser.StripInlinedCommands(authored);
                if (part.Length == 0)
                    continue;

                if (part.IndexOf('*') < 0)
                {
                    notes.Add(new NoteSlotEntry
                    {
                        text = part,
                        groupIndex = group++
                    });
                    continue;
                }

                if (!SlidePathParser.TryExpandSameHead(part, out var branches))
                {
                    error = SlideSyntaxValidator.Diagnose(
                        "同头星星无法拆分成多条星星",
                        "SAME-HEAD SLIDE CANNOT BE SPLIT",
                        part);
                    ok = false;
                    group++;
                    continue;
                }

                foreach (var branch in branches)
                    notes.Add(new NoteSlotEntry
                    {
                        text = branch,
                        fromSameHead = true,
                        groupIndex = group
                    });
                group++;
            }

            return ok;
        }
    }

    // One place decides what a Note is. The kind used to be inferred separately by
    // the editor, the syntax check, the preview and View, each with its own
    // Contains('h') / Contains('[') guesses, which is why the same text could parse
    // as a Hold in one layer and fail in another.
    public static class NoteExpressionParser
    {
        // forPreview keeps the half-typed cases the editor still wants to draw:
        // the leniency lives here instead of each caller inventing its own.
        public static bool TryParse(
            string text,
            out NoteExpressionData note,
            out string error,
            bool forPreview = false)
        {
            note = new NoteExpressionData { source = text ?? string.Empty };
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = SlideSyntaxValidator.Diagnose(
                    "音符内容为空",
                    "NOTE IS EMPTY",
                    string.Empty);
                return false;
            }

            text = StripInlinedCommands(text);

            if (SlidePathParser.TryTakeTrajectoryBorrow(
                    text, out var borrowed, out var carrier))
                return TryParseBorrowedTrajectory(
                    text, borrowed, carrier, note, forPreview, out error);

            if (SlidePathParser.ContainsSlideShape(text))
                return TryParseSlide(text, note, forPreview, out error);

            if (!SlidePathParser.TryReadPosition(
                    text, 0, out var position, out var offset))
            {
                error = SlideSyntaxValidator.Diagnose(
                    "音符开头不是合法位置（键位 1-8、1d-8d、A1-E8 或 C）",
                    "NOTE DOES NOT START AT A VALID POSITION",
                    text);
                return false;
            }

            note.position = position;
            if (!TryReadRadius(text, position, ref offset, out error))
                return false;
            if (!TryReadModifiers(text, position, note, out error))
                return false;
            return TryReadDuration(text, position, note, offset, out error);
        }

        /// <summary>
        /// A command written after a note on the same beat ("1&lt;SV*1&gt;") would otherwise
        /// reach the slide parser, because '&lt;' is also a slide shape character.
        /// </summary>
        internal static bool TryFindInlinedCommand(string text, out string commandName)
            => TryFindInlinedCommand(text, out commandName, out _, out _);

        internal static bool TryFindInlinedCommand(
            string text, out string commandName, out int open, out int close)
        {
            commandName = string.Empty;
            open = -1;
            close = -1;
            if (string.IsNullOrEmpty(text))
                return false;

            // A command at offset 0 is not inlined: nothing is written before it.
            for (var start = text.IndexOf('<', Math.Min(1, text.Length));
                 start > 0;
                 start = text.IndexOf('<', start + 1))
            {
                var end = text.IndexOf('>', start + 1);
                if (end < 0)
                    return false;
                var inner = text.Substring(start + 1, end - start - 1);
                var separator = inner.IndexOfAny(new[] { '*', '=' });
                var name = separator < 0 ? inner : inner.Substring(0, separator);
                if (!AlphaCommandGrammar.TryFind(name, out var descriptor) ||
                    descriptor == null)
                    continue;

                commandName = descriptor.name;
                open = start;
                close = end;
                return true;
            }
            return false;
        }

        internal static string InlinedCommandError(string commandName, string text) =>
            SlideSyntaxValidator.Diagnose(
                $"命令 <{commandName}> 要写在本拍音符之前",
                $"COMMAND <{commandName}> MUST COME BEFORE THE NOTES OF ITS BEAT",
                text);

        internal static string StripInlinedCommands(string text)
        {
            while (TryFindInlinedCommand(text, out _, out var open, out var close))
                text = text.Remove(open, close - open + 1);
            return text;
        }

        private static bool TryReadRadius(
            string text,
            SlidePositionData position,
            ref int offset,
            out string error)
        {
            error = string.Empty;
            switch (SlidePathParser.TryReadRadiusOverride(
                        text, offset, position, out var next))
            {
                case RadiusOverrideResult.None:
                    return true;
                case RadiusOverrideResult.Invalid:
                    error = SlideSyntaxValidator.Diagnose(
                        $"~ 的距离必须写成 ~[数字]，取值 0 到 {SlidePathParser.MaxRadiusOverride:0.#}（判定圈为 4.8）",
                        "RADIUS OVERRIDE MUST BE WRITTEN AS ~[NUMBER]",
                        text);
                    return false;
                case RadiusOverrideResult.InvalidSkin:
                    error = SlideSyntaxValidator.Diagnose(
                        "~ 的图片要写成 ~[文件名.png]，支持 png/jpg/jpeg，" +
                        "可以放在谱面目录的子文件夹里（如 ~[skins/star.png]），" +
                        "但不能用绝对路径或 ..",
                        "SKIN MUST BE WRITTEN AS ~[NAME.PNG], PNG/JPG/JPEG ONLY, " +
                        "RELATIVE TO THE CHART FOLDER",
                        text);
                    return false;
            }

            // A skin only swaps the Note's picture, so it is allowed anywhere, on keys
            // included. The next two rejections are about where a Note is drawn, which
            // a skin does not touch; everything after them applies to both and must
            // still run, or the suffix's own bracket reads as a Hold duration.
            var isSkinOnly = position.HasSkin && !position.HasCustomRadius;

            if (!isSkinOnly && position.IsKey)
            {
                error = SlideSyntaxValidator.Diagnose(
                    "~ 目前只支持 Touch（A/B/D/E 区），键位音符还不能改距离",
                    "RADIUS OVERRIDE IS SUPPORTED ONLY ON TOUCH NOTES",
                    text);
                return false;
            }

            if (!isSkinOnly && position.area == 'C')
            {
                error = SlideSyntaxValidator.Diagnose(
                    "C 区在正中心，没有方向可以延伸，不能用 ~",
                    "THE CENTER AREA HAS NO DIRECTION FOR A RADIUS OVERRIDE",
                    text);
                return false;
            }

            // "A1~[3]-A5[8:1]" hides its slide mark behind the radius bracket, where
            // the shape scan cannot see it, so it would otherwise be reported as a
            // missing Hold marker.
            if (SlidePathParser.ContainsSlideShape(text.Substring(next)))
            {
                error = SlideSyntaxValidator.Diagnose(
                    "~ 暂不支持写在星星里",
                    "RADIUS OVERRIDE IS NOT SUPPORTED INSIDE A SLIDE",
                    text);
                return false;
            }

            offset = next;
            return true;
        }

        /// <summary>
        /// Reads a note that borrows another slide's star trajectory, "1~[5-7[8:1]]".
        /// </summary>
        /// <remarks>
        /// What is left once the borrow is lifted out is an ordinary note, so it is
        /// read by the ordinary reader: the note decides what travels and how it
        /// looks, the borrowed path decides where it goes. A slide left over on the
        /// carrier is the one thing that cannot be meant, because the note is
        /// already following a path.
        /// </remarks>
        private static bool TryParseBorrowedTrajectory(
            string text,
            string borrowed,
            string carrier,
            NoteExpressionData note,
            bool forPreview,
            out string error)
        {
            if (SlidePathParser.ContainsSlideShape(carrier))
            {
                error = SlideSyntaxValidator.Diagnose(
                    "音符已经在跟着 ~[] 里的轨迹走了，后面不能再接 slide",
                    "A NOTE FOLLOWING A BORROWED TRAJECTORY CANNOT ALSO BE A SLIDE",
                    text);
                return false;
            }
            if (SlidePathParser.TryTakeTrajectoryBorrow(carrier, out _, out _))
            {
                error = SlideSyntaxValidator.Diagnose(
                    "一个音符只能借一条轨迹",
                    "A NOTE CAN BORROW ONLY ONE TRAJECTORY",
                    text);
                return false;
            }

            if (!SlidePathParser.TryParsePath(borrowed, out var path))
            {
                error = SlideSyntaxValidator.Diagnose(
                    "~[] 里要写一条完整的星星，比如 ~[5-7[8:1]]",
                    "A BORROWED TRAJECTORY MUST BE A COMPLETE SLIDE, LIKE ~[5-7[8:1]]",
                    borrowed);
                return false;
            }
            var validated = forPreview
                ? SlideSyntaxValidator.TryValidateForPreview(path, out error)
                : SlideSyntaxValidator.TryValidate(path, out error);
            if (!validated)
                return false;

            // A wifi star is three stars on three paths, so there is no single
            // trajectory to hand over. Saying so is the point: a chart that writes
            // one gets told, not ignored.
            foreach (var segment in path.segments)
            {
                if (segment.shape != "w")
                    continue;
                error = SlideSyntaxValidator.Diagnose(
                    "~[] 还不支持 wifi 星星，它的星星是三颗，走法也不一样",
                    "A BORROWED TRAJECTORY CANNOT BE A WIFI SLIDE YET",
                    borrowed);
                return false;
            }

            if (!TryParse(carrier, out var head, out error, forPreview))
                return false;
            if (head.kind == NoteExpressionKind.Slide)
            {
                error = SlideSyntaxValidator.Diagnose(
                    "音符已经在跟着 ~[] 里的轨迹走了，后面不能再接 slide",
                    "A NOTE FOLLOWING A BORROWED TRAJECTORY CANNOT ALSO BE A SLIDE",
                    text);
                return false;
            }

            note.source = text;
            note.kind = head.kind;
            note.position = head.position;
            note.modifiers = head.modifiers;
            note.duration = head.duration;
            note.isZeroLengthHold = head.isZeroLengthHold;
            // The path decides the route implementation; the carrier parsed above
            // still decides which note sprite travels that route.
            note.isTouchPath = path.isTouchPath;
            note.trajectory = path;
            note.trajectorySource = borrowed;
            return true;
        }

        private static bool TryParseSlide(
            string text,
            NoteExpressionData note,
            bool forPreview,
            out string error)
        {
            if (text.IndexOf('~') >= 0)
            {
                error = SlideSyntaxValidator.Diagnose(
                    "~ 暂不支持写在星星里",
                    "RADIUS OVERRIDE IS NOT SUPPORTED INSIDE A SLIDE",
                    text);
                return false;
            }

            if (!SlidePathParser.TryParsePath(text, out var path))
            {
                error = SlideSyntaxValidator.Diagnose(
                    "星星路径无法解析",
                    "SLIDE PATH CANNOT BE PARSED",
                    text);
                return false;
            }

            var valid = forPreview
                ? SlideSyntaxValidator.TryValidateForPreview(path, out error)
                : SlideSyntaxValidator.TryValidate(path, out error);
            if (!valid)
                return false;
            if (!NoteModifierParser.TryParse(
                    text, path.segments, out var modifiers))
            {
                error = ModifierPositionError(text);
                return false;
            }

            note.kind = NoteExpressionKind.Slide;
            note.path = path;
            note.isTouchPath = path.isTouchPath;
            note.position = path.head;
            note.modifiers = modifiers;
            return true;
        }

        private static bool TryReadModifiers(
            string text,
            SlidePositionData position,
            NoteExpressionData note,
            out string error)
        {
            error = string.Empty;
            var parsed = position.IsKey
                ? NoteModifierParser.TryParse(
                    text, Array.Empty<SlidePathSegmentData>(), out var modifiers)
                : NoteModifierParser.TryParseTouchHeader(text, out modifiers);
            if (!parsed)
            {
                error = ModifierPositionError(text);
                return false;
            }

            note.modifiers = modifiers;
            return true;
        }

        private static bool TryReadDuration(
            string text,
            SlidePositionData position,
            NoteExpressionData note,
            int offset,
            out string error)
        {
            error = string.Empty;
            // Only what follows the position is examined, so a "~[distance]"
            // bracket can never be mistaken for the Hold duration.
            var body = NoteModifierParser.RemoveModifiers(text.Substring(offset));
            var hold = body.IndexOf('h');
            var durationStart = body.IndexOf('[');
            if (hold < 0)
            {
                if (durationStart >= 0)
                {
                    error = SlideSyntaxValidator.Diagnose(
                        $"时长必须配合 h 写成 Hold（例 {Suggest(text, offset)}）",
                        $"DURATION NEEDS 'h' FOR A HOLD, TRY '{Suggest(text, offset)}'",
                        text);
                    return false;
                }

                note.kind = position.IsTouch
                    ? NoteExpressionKind.Touch
                    : NoteExpressionKind.Tap;
                return true;
            }

            note.kind = position.IsTouch
                ? NoteExpressionKind.TouchHold
                : NoteExpressionKind.Hold;
            if (position.HasCustomRadius)
            {
                // The TouchHold fans are drawn at the area's own distance; moving the
                // Note without redrawing them would render a broken ring.
                error = SlideSyntaxValidator.Diagnose(
                    "~ 暂不支持 TouchHold，只能用在 Touch 上",
                    "RADIUS OVERRIDE IS NOT SUPPORTED ON A TOUCH HOLD",
                    text);
                return false;
            }

            if (durationStart < 0)
            {
                note.isZeroLengthHold = true;
                return true;
            }

            if (durationStart < hold)
            {
                error = SlideSyntaxValidator.Diagnose(
                    "h 必须写在时长之前",
                    "'h' MUST COME BEFORE THE DURATION",
                    text);
                return false;
            }

            note.duration = body.Substring(durationStart);
            if (SlideSyntaxValidator.TryParseDuration(
                    note.duration, out _, allowZeroLength: true))
                return true;
            // "E1h[8:1]~[4.8]" hides the radius mark behind the duration, where the
            // position scan cannot see it. Naming the duration as the problem sent
            // authors looking in the wrong place, and the same note written
            // "E1~[4.8]h[8:1]" already says what is actually unsupported.
            error = note.duration.IndexOf('~') >= 0
                ? SlideSyntaxValidator.Diagnose(
                    "~ 暂不支持 Hold，只能用在 Touch 上",
                    "RADIUS OVERRIDE IS NOT SUPPORTED ON A HOLD",
                    text)
                : SlideSyntaxValidator.Diagnose(
                    "Hold 时长写法错误",
                    "INVALID HOLD DURATION",
                    note.duration);
            return false;
        }

        private static string Suggest(string text, int offset)
        {
            var durationStart = text.IndexOf('[', offset);
            return durationStart < 0
                ? text
                : text.Substring(0, durationStart) + "h" +
                  text.Substring(durationStart);
        }

        private static string ModifierPositionError(string text) =>
            SlideSyntaxValidator.Diagnose(
                "修饰符位置错误",
                "INVALID NOTE MODIFIER POSITION",
                text);
    }
}
