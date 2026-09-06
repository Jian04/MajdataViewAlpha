using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataCore
{
    [Flags]
    public enum NoteModifierFlags
    {
        None = 0,
        Break = 1 << 0,
        Ex = 1 << 1,
        Firework = 1 << 2,
        Mine = 1 << 3,
        NoHeadWithFade = 1 << 4,
        NoHeadWithoutFade = 1 << 5,
        NoHead = NoHeadWithFade | NoHeadWithoutFade,
        ForceStar = 1 << 6,
        FakeRotate = 1 << 7
    }

    public readonly struct ParsedNoteModifiers
    {
        public ParsedNoteModifiers(
            NoteModifierFlags head,
            NoteModifierFlags slide)
        {
            Head = head;
            Slide = slide;
        }

        public NoteModifierFlags Head { get; }
        public NoteModifierFlags Slide { get; }
        public bool HasHead(NoteModifierFlags value) => (Head & value) != 0;
        public bool HasSlide(NoteModifierFlags value) => (Slide & value) != 0;
        public bool HasAny(NoteModifierFlags value) =>
            ((Head | Slide) & value) != 0;
    }

    public static class NoteModifierParser
    {
        private const string ModifierCharacters = "bxfm!?$";

        public static bool TryParse(
            string expression,
            IReadOnlyList<SlidePathSegmentData> slidePath,
            out ParsedNoteModifiers modifiers)
        {
            modifiers = default;
            if (!SlidePathParser.TryReadPosition(
                    expression, 0, out var position, out var offset))
                return false;

            var head = NoteModifierFlags.None;
            var slide = NoteModifierFlags.None;
            if (slidePath.Count == 0)
            {
                // "~[...]" belongs to the position, not to the modifiers, so it has
                // to be stepped over first or its '~' reads as an unknown modifier.
                switch (SlidePathParser.TryReadRadiusOverride(
                            expression, offset, position, out var afterRadius))
                {
                    case RadiusOverrideResult.Invalid:
                    case RadiusOverrideResult.InvalidSkin:
                        return false;
                    case RadiusOverrideResult.Applied:
                        offset = afterRadius;
                        break;
                }

                if (!TryReadNonSlideModifierRun(expression, offset, out var header, out var hasDuration))
                    return false;
                // Only modifier placement is decided here. Whether a duration is
                // allowed at all is a note-kind rule reported by the caller.
                if (header.IndexOfAny(new[] { '!', '?' }) >= 0 ||
                    header.IndexOf('$') >= 0 &&
                    (position.IsTouch ||
                     header.IndexOf('h') >= 0 ||
                     hasDuration))
                    return false;
                if (!TryParseRun(header, isSlide: false, ref head, ref slide))
                    return false;
                modifiers = new ParsedNoteModifiers(head, slide);
                return true;
            }

            var headStart = offset;
            while (offset < expression.Length &&
                   ModifierCharacters.IndexOf(expression[offset]) >= 0)
                offset++;
            var headRun = expression.Substring(
                headStart, offset - headStart);
            if (headRun.IndexOf('$') >= 0)
                return false;
            if (!TryParseRun(
                    headRun,
                    isSlide: false,
                    ref head,
                    ref slide))
                return false;

            for (var index = 0; index < slidePath.Count; index++)
            {
                var run = slidePath[index].modifiers;
                if (run.Length == 0)
                    continue;
                if (!IsBodyRunPositionAllowed(slidePath, index) ||
                    !IsSlideBodyRun(run) ||
                    !TryParseRun(run, isSlide: true, ref head, ref slide))
                    return false;
            }

            modifiers = new ParsedNoteModifiers(head, slide);
            return true;
        }

        /// <summary>
        /// A 'b'/'m' on the last joint applies to the whole slide, which is the
        /// usual way to write it. On an inner joint it only has a meaning when
        /// that segment carries its own duration ("1-3b[8:1]-5[8:1]"): with a
        /// single total duration ("1-3b-5[8:1]") there is no segment to attach
        /// it to, so it is rejected rather than silently applied to everything.
        /// </summary>
        private static bool IsBodyRunPositionAllowed(
            IReadOnlyList<SlidePathSegmentData> slidePath,
            int index) =>
            index == slidePath.Count - 1 ||
            !string.IsNullOrEmpty(slidePath[index].duration);

        public static bool TryParseRuns(
            string headRun,
            IReadOnlyList<string> slideRuns,
            out ParsedNoteModifiers modifiers)
        {
            var head = NoteModifierFlags.None;
            var slide = NoteModifierFlags.None;
            if (!TryParseRun(headRun, isSlide: false, ref head, ref slide))
            {
                modifiers = default;
                return false;
            }
            for (var index = 0; index < slideRuns.Count; index++)
            {
                if (slideRuns[index].Length == 0)
                    continue;
                // Any joint of a connected slide may carry 'b'/'m', not just the
                // last one: "1-3b[8:1]-5[8:1]" breaks only the first segment.
                if (!IsSlideBodyRun(slideRuns[index]) ||
                    !TryParseRun(slideRuns[index], isSlide: true, ref head, ref slide))
                {
                    modifiers = default;
                    return false;
                }
            }
            modifiers = new ParsedNoteModifiers(head, slide);
            return true;
        }

        public static bool TryParseSlideRuns(
            string headRun,
            IReadOnlyList<string> slideRuns,
            out ParsedNoteModifiers modifiers)
        {
            if (headRun.IndexOfAny(new[] { 'h', '$' }) >= 0)
            {
                modifiers = default;
                return false;
            }
            return TryParseRuns(
                headRun, slideRuns, out modifiers);
        }

        /// <summary>
        /// Same as <see cref="TryParseSlideRuns"/> but keeps the segments, so the
        /// inner-joint rule can see which segment owns a duration.
        /// </summary>
        public static bool TryParseSlideSegments(
            string headRun,
            IReadOnlyList<SlidePathSegmentData> segments,
            out ParsedNoteModifiers modifiers)
        {
            modifiers = default;
            if (headRun.IndexOfAny(new[] { 'h', '$' }) >= 0)
                return false;

            var head = NoteModifierFlags.None;
            var slide = NoteModifierFlags.None;
            if (!TryParseRun(headRun, isSlide: false, ref head, ref slide))
                return false;

            for (var index = 0; index < segments.Count; index++)
            {
                var run = segments[index].modifiers ?? string.Empty;
                if (run.Length == 0)
                    continue;
                if (!IsBodyRunPositionAllowed(segments, index) ||
                    !IsSlideBodyRun(run) ||
                    !TryParseRun(run, isSlide: true, ref head, ref slide))
                    return false;
            }

            modifiers = new ParsedNoteModifiers(head, slide);
            return true;
        }

        public static bool TryParseTouchHeader(
            string expression,
            out ParsedNoteModifiers modifiers)
        {
            modifiers = default;
            if (string.IsNullOrEmpty(expression))
                return false;

            if (!SlidePathParser.TryReadPosition(
                    expression, 0, out var position, out var modifierStart) ||
                !position.IsTouch)
                return false;

            // "E1~[4.8]" carries its distance in a bracket that belongs to the
            // position, so it has to be stepped over before the modifiers are read.
            switch (SlidePathParser.TryReadRadiusOverride(
                        expression, modifierStart, position, out var afterRadius))
            {
                case RadiusOverrideResult.Invalid:
                case RadiusOverrideResult.InvalidSkin:
                    return false;
                case RadiusOverrideResult.Applied:
                    modifierStart = afterRadius;
                    break;
            }

            if (!TryReadNonSlideModifierRun(expression, modifierStart, out var header, out _))
                return false;
            if (header.IndexOfAny(new[] { '!', '?', '$' }) >= 0)
                return false;
            return TryParseRuns(
                header,
                Array.Empty<string>(),
                out modifiers);
        }

        private static bool TryReadNonSlideModifierRun(
            string expression, int offset, out string run, out bool hasDuration)
        {
            var open = expression.IndexOf('[', offset);
            hasDuration = open >= 0;
            if (!hasDuration)
            {
                run = expression.Substring(offset);
                return true;
            }

            run = string.Empty;
            var close = expression.IndexOf(']', open + 1);
            if (close < 0)
                return false;
            // Duration content is validated separately; suffixes may only be modifiers.
            var suffix = expression.Substring(close + 1);
            foreach (var character in suffix)
                if (ModifierCharacters.IndexOf(character) < 0)
                    return false;
            run = expression.Substring(offset, open - offset) + suffix;
            return true;
        }

        public static string RemoveModifiers(string expression)
        {
            var result = new StringBuilder(expression.Length);
            foreach (var character in expression)
                if (ModifierCharacters.IndexOf(character) < 0)
                    result.Append(character);
            return result.ToString();
        }

        private static bool TryParseRun(
            string run,
            bool isSlide,
            ref NoteModifierFlags head,
            ref NoteModifierFlags slide)
        {
            var hasHoldMarker = false;
            foreach (var character in run)
            {
                var targetIsSlide = isSlide &&
                                    character is 'b' or 'm';
                var flag = character switch
                {
                    'b' => NoteModifierFlags.Break,
                    'x' => NoteModifierFlags.Ex,
                    'f' => NoteModifierFlags.Firework,
                    'm' => NoteModifierFlags.Mine,
                    '?' => NoteModifierFlags.NoHeadWithFade,
                    '!' => NoteModifierFlags.NoHeadWithoutFade,
                    '$' => NoteModifierFlags.ForceStar,
                    'h' => NoteModifierFlags.None,
                    _ => (NoteModifierFlags)(-1)
                };
                if ((int)flag < 0)
                    return false;
                if (character == 'h')
                {
                    if (isSlide || hasHoldMarker)
                        return false;
                    hasHoldMarker = true;
                    continue;
                }
                if (character == '$')
                {
                    if ((head & NoteModifierFlags.ForceStar) == 0)
                        head |= NoteModifierFlags.ForceStar;
                    else if ((head & NoteModifierFlags.FakeRotate) == 0)
                        head |= NoteModifierFlags.FakeRotate;
                    else
                        return false;
                    continue;
                }
                if ((flag & NoteModifierFlags.NoHead) != 0 &&
                    ((head | slide) & NoteModifierFlags.NoHead) != 0)
                    return false;

                if (targetIsSlide)
                {
                    if ((slide & flag) != 0)
                        return false;
                    slide |= flag;
                }
                else
                {
                    if ((head & flag) != 0)
                        return false;
                    head |= flag;
                }
            }
            return true;
        }

        private static bool IsSlideBodyRun(string run)
        {
            foreach (var character in run)
                if (character is not ('b' or 'm'))
                    return false;
            return true;
        }

    }
}
