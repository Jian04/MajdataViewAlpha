using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MajdataCore;

namespace MajdataEdit.Editor;

// Merges a note produced by the visual playfield into the timing slot the caret sits
// in. Every decision here (what kind of note is this, where does a path start and
// end, does it already carry a break) comes from the shared parser: this module used
// to answer those questions with character offsets, which wrote text the parser then
// rejected — a D-zone head turned into "4d-5[8:1]*d-8[8:1]", chaining onto a D-zone
// end produced "1-5d[8:1]d-8[8:1]", and splitting "4d-5d-8d[8:1]" dropped a 'd'.
internal static class VisualNoteEditor
{
    public const string ActionNote = "note";
    public const string ActionSlideHead = "slideHead";
    public const string ActionSlidePath = "slidePath";

    private const string DefaultDuration = "[8:1]";

    public static string Merge(
        string current,
        string incoming,
        string action = ActionNote,
        int slideStart = 0)
    {
        current = (current ?? string.Empty).Trim().Trim('/');
        incoming = (incoming ?? string.Empty).Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(current))
            return incoming;

        var notes = NoteSlotParser
            .SplitTopLevel(current)
            .Select(part => part.Trim())
            .Where(part => part.Length != 0)
            .ToList();
        if (notes.Count == 0)
            return incoming;

        var original = notes.ToList();
        var merged = Apply(notes, incoming, action, slideStart);
        if (IsSlotValid(merged))
            return merged;

        // Placing the note beside the others is always expressible; a merge that
        // would need invalid text is not worth writing. This has to start from the
        // untouched slot, since Apply edits the list in place.
        var appended = string.Join('/', original.Append(incoming));
        return IsSlotValid(appended) ? appended : merged;
    }

    private static string Apply(
        List<string> notes,
        string incoming,
        string action,
        int slideStart)
    {
        var incomingNote = Parse(incoming);

        if (IsPlainKey(incomingNote, out var key))
        {
            for (var index = 0; index < notes.Count; index++)
            {
                if (!TrySplitChain(notes[index], key, out var first, out var second))
                    continue;
                notes[index] = first;
                notes.Insert(index + 1, second);
                return string.Join('/', notes);
            }

            if (action == ActionSlideHead)
            {
                if (TryCycleChain(notes, key))
                    return string.Join('/', notes);

                var slideIndex = notes.FindIndex(
                    item => HeadPositionOf(item) == key);
                if (slideIndex >= 0)
                {
                    notes[slideIndex] = ToggleHeadBreak(notes[slideIndex]);
                    return string.Join('/', notes);
                }
            }
        }

        if (action == ActionSlidePath &&
            incomingNote != null &&
            incomingNote.kind == NoteExpressionKind.Touch &&
            TryToggleSlidePathBreak(notes, incoming, slideStart))
            return string.Join('/', notes);

        if (IsKeySlide(incomingNote))
        {
            var head = incomingNote!.path.head;
            var branch = RenderBody(incomingNote.path, withHeadModifiers: true);

            var sameHead = notes.FindIndex(item => HeadMatches(item, head));
            if (sameHead >= 0)
            {
                if (!HasBranch(notes[sameHead], branch))
                    notes[sameHead] += "*" + branch;
                return string.Join('/', notes);
            }

            var chainable = notes.FindIndex(item => EndMatches(item, head));
            if (chainable >= 0)
            {
                notes[chainable] +=
                    RenderBody(incomingNote.path, withHeadModifiers: false);
                return string.Join('/', notes);
            }
        }

        var variant = notes.FindIndex(item => IsVariantOf(item, incomingNote));
        if (variant >= 0)
            notes[variant] = NextVariant(notes[variant], incomingNote!);
        else
            notes.Add(incoming);
        return string.Join('/', notes);
    }

    private static bool TrySplitChain(
        string part,
        int key,
        out string first,
        out string second)
    {
        first = string.Empty;
        second = string.Empty;
        if (IsSameHeadGroup(part))
            return false;
        var note = Parse(part);
        if (!IsKeySlide(note) || note!.path.segments.Count < 2)
            return false;

        var segments = note.path.segments;
        // The last end is where the slide stops, not a joint.
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (segments[index].endPosition != key)
                continue;
            var total = segments
                .Select(segment => segment.duration)
                .FirstOrDefault(duration => !string.IsNullOrEmpty(duration));
            first = WithDuration(
                RenderPath(
                    note.path.head,
                    note.path.headModifiers,
                    segments.Take(index + 1)),
                total);
            second = WithDuration(
                RenderPath(
                    segments[index].end,
                    string.Empty,
                    segments.Skip(index + 1)),
                total);
            return true;
        }

        return false;
    }

    // First click marks the following slide's head as a break, second click joins the
    // two into one connected slide.
    private static bool TryCycleChain(List<string> notes, int key)
    {
        var previous = notes.FindIndex(
            item => EndPositionOf(item) == key && HeadPositionOf(item) != key);
        if (previous < 0)
            return false;

        var next = notes.FindIndex(item => HeadPositionOf(item) == key);
        if (next < 0 || next == previous)
            return false;

        var nextNote = ParseSlidePart(notes[next], out var nextRest);
        if (nextNote == null)
            return false;

        if (nextNote.path.headModifiers.IndexOf('b') >= 0)
        {
            // Joining swallows the whole following note, which only has one body
            // when it is not a group.
            if (nextRest.Length != 0 || IsSameHeadGroup(notes[previous]))
                return false;
            notes[previous] +=
                RenderBody(nextNote.path, withHeadModifiers: false);
            notes.RemoveAt(next);
            return true;
        }

        notes[next] = SetHeadModifiers(
            nextNote, nextNote.path.headModifiers + "b") + nextRest;
        return true;
    }

    private static string ToggleHeadBreak(string part)
    {
        var note = ParseSlidePart(part, out var rest);
        if (note == null)
            return part;
        var modifiers = note.path.headModifiers;
        return SetHeadModifiers(
            note,
            modifiers.IndexOf('b') >= 0
                ? modifiers.Replace("b", string.Empty)
                : modifiers + "b") + rest;
    }

    private static bool TryToggleSlidePathBreak(
        List<string> notes,
        string touch,
        int slideStart)
    {
        var slideIndex = notes.FindIndex(item =>
            ParseSlidePart(item, out _) != null &&
            (slideStart == 0 || HeadPositionOf(item) == slideStart));
        if (slideIndex < 0)
            return false;

        var touchIndex = notes.FindIndex(
            item => string.Equals(item, touch, StringComparison.OrdinalIgnoreCase));
        var slide = notes[slideIndex];
        var hasBodyBreak = HasSlideBodyBreak(slide);

        if (!hasBodyBreak)
        {
            notes[slideIndex] = slide + "b";
            return true;
        }

        notes[slideIndex] = RemoveSlideBodyBreak(slide);
        if (touchIndex < 0)
            notes.Add(touch);
        else
            notes.RemoveAt(touchIndex);
        return true;
    }

    private static bool HasSlideBodyBreak(string part)
    {
        var note = Parse(part);
        return note != null &&
               note.kind == NoteExpressionKind.Slide &&
               note.modifiers.HasSlide(NoteModifierFlags.Break);
    }

    private static string RemoveSlideBodyBreak(string part)
    {
        if (part.Length != 0 && part[^1] == 'b')
            return part[..^1];

        var note = Parse(part);
        if (note == null || note.kind != NoteExpressionKind.Slide)
            return part;
        foreach (var segment in note.path.segments)
            segment.modifiers = segment.modifiers.Replace("b", string.Empty);
        return RenderPath(
            note.path.head, note.path.headModifiers, note.path.segments);
    }

    private static bool HasBranch(string part, string body)
    {
        // Branches are compared without their duration: the same route written with
        // a different length is still the same branch.
        static string PathOnly(string text)
        {
            var bracket = text.IndexOf('[');
            return bracket < 0 ? text : text[..bracket];
        }

        var branches = part.Split('*').Skip(1).Select(PathOnly);
        return branches.Contains(PathOnly(body), StringComparer.Ordinal);
    }

    private static bool IsVariantOf(string existing, NoteExpressionData? incoming)
    {
        if (incoming == null)
            return false;
        var note = Parse(existing);
        if (note == null || note.kind == NoteExpressionKind.Slide)
            return false;
        if (incoming.kind == NoteExpressionKind.Slide)
            return false;
        return SamePosition(note.position, incoming.position);
    }

    // Clicking a key that already has a note steps through Tap, Hold, break Tap and
    // break Hold. Modifiers the click does not own (Ex, firework, mine, star) and an
    // authored Hold length are carried over; a step that cannot be written for this
    // note (a Hold cannot be a forced star, for instance) is skipped.
    private static string NextVariant(string existing, NoteExpressionData incoming)
    {
        var note = Parse(existing);
        var position = incoming.position.ToExpression();
        if (note == null)
            return position;

        var duration = string.IsNullOrEmpty(note.duration)
            ? DefaultDuration
            : note.duration;
        var extra = PreservedModifiers(note);
        var states = incoming.position.IsTouch
            ? new[] { (Break: false, Hold: false), (Break: false, Hold: true) }
            : new[]
            {
                (Break: false, Hold: false), (Break: false, Hold: true),
                (Break: true, Hold: false), (Break: true, Hold: true)
            };

        var current = Array.FindIndex(
            states,
            state => state.Break == note.modifiers.HasHead(NoteModifierFlags.Break) &&
                     state.Hold == note.IsHold);
        if (current < 0)
            current = 0;

        for (var step = 1; step <= states.Length; step++)
        {
            var state = states[(current + step) % states.Length];
            // The help spells a break Hold "1hb[8:1]", so the break marker stays
            // behind the 'h'.
            var candidate = position + extra +
                            (state.Hold ? "h" : string.Empty) +
                            (state.Break ? "b" : string.Empty) +
                            (state.Hold ? duration : string.Empty);
            if (Parse(candidate) != null)
                return candidate;
        }

        return existing;
    }

    private static string PreservedModifiers(NoteExpressionData note)
    {
        var text = new StringBuilder();
        if (note.modifiers.HasHead(NoteModifierFlags.Ex))
            text.Append('x');
        if (note.modifiers.HasHead(NoteModifierFlags.Firework))
            text.Append('f');
        if (note.modifiers.HasHead(NoteModifierFlags.Mine))
            text.Append('m');
        if (note.modifiers.HasHead(NoteModifierFlags.FakeRotate))
            text.Append("$$");
        else if (note.modifiers.HasHead(NoteModifierFlags.ForceStar))
            text.Append('$');
        return text.ToString();
    }

    private static bool IsPlainKey(NoteExpressionData? note, out int key)
    {
        key = 0;
        if (note == null ||
            note.kind != NoteExpressionKind.Tap ||
            !note.position.IsKey ||
            note.position.HasCustomRadius ||
            note.modifiers.Head != NoteModifierFlags.None ||
            note.modifiers.Slide != NoteModifierFlags.None)
            return false;
        key = note.position.position;
        return true;
    }

    private static bool IsKeySlide(NoteExpressionData? note) =>
        note != null &&
        note.kind == NoteExpressionKind.Slide &&
        !note.isTouchPath &&
        note.path.head.IsKey;

    private static bool HeadMatches(string part, SlidePositionData head)
    {
        var note = ParseSlidePart(part, out _);
        return note != null && SamePosition(note.path.head, head);
    }

    private static bool EndMatches(string part, SlidePositionData head)
    {
        // Only a single route has one end to chain onto.
        if (IsSameHeadGroup(part))
            return false;
        var note = Parse(part);
        if (!IsKeySlide(note))
            return false;
        var last = note!.path.segments[^1];
        return last.endPosition == head.position &&
               last.endIsDZone == head.isDZone;
    }

    private static int HeadPositionOf(string part)
    {
        var note = ParseSlidePart(part, out _);
        return note?.path.head.position ?? 0;
    }

    private static int EndPositionOf(string part)
    {
        if (IsSameHeadGroup(part))
            return 0;
        var note = Parse(part);
        return IsKeySlide(note) ? note!.path.segments[^1].endPosition : 0;
    }

    private static bool IsSameHeadGroup(string part)
        => part.IndexOf('*') >= 0;

    // A same-head group is edited through its first branch; the remaining branches
    // are carried along untouched.
    private static NoteExpressionData? ParseSlidePart(string part, out string rest)
    {
        rest = string.Empty;
        var group = part.IndexOf('*');
        if (group >= 0)
        {
            rest = part[group..];
            part = part[..group];
        }

        var note = Parse(part);
        if (IsKeySlide(note))
            return note;
        rest = string.Empty;
        return null;
    }

    private static bool SamePosition(SlidePositionData left, SlidePositionData right)
        => left.area == right.area &&
           left.position == right.position &&
           left.isDZone == right.isDZone;

    private static string SetHeadModifiers(
        NoteExpressionData note,
        string modifiers)
        => RenderPath(note.path.head, modifiers, note.path.segments);

    // A path is written as head + modifiers + every segment after its start, because
    // each segment starts where the previous one ended.
    private static string RenderPath(
        SlidePositionData head,
        string headModifiers,
        IEnumerable<SlidePathSegmentData> segments)
    {
        var text = new StringBuilder();
        text.Append(RenderPosition(head, head.position, head.isDZone));
        text.Append(headModifiers);
        foreach (var segment in segments)
            text.Append(RenderSegment(segment));
        return text.ToString();
    }

    // Head modifiers travel with a `*` branch, because the branch keeps its own
    // head. Chaining instead continues an existing route, where a head marker would
    // land between two segments and stop parsing.
    private static string RenderBody(SlidePathData path, bool withHeadModifiers)
    {
        var text = new StringBuilder();
        if (withHeadModifiers)
            text.Append(path.headModifiers);
        foreach (var segment in path.segments)
            text.Append(RenderSegment(segment));
        return text.ToString();
    }

    private static string RenderSegment(SlidePathSegmentData segment)
    {
        var text = new StringBuilder();
        text.Append(segment.shape);
        if (segment.hasMiddle)
            text.Append(
                RenderPosition(
                    segment.middle,
                    segment.middlePosition,
                    segment.middleIsDZone));
        text.Append(
            RenderPosition(segment.end, segment.endPosition, segment.endIsDZone));
        text.Append(segment.modifiers);
        text.Append(segment.duration);
        return text.ToString();
    }

    private static string RenderPosition(
        SlidePositionData parsed,
        int position,
        bool isDZone)
    {
        if (parsed != null && parsed.position != 0)
        {
            if (parsed.area == 'C')
                return "C";
            if (parsed.area != 'K')
                return $"{parsed.area}{parsed.position}";
            position = parsed.position;
            isDZone = parsed.isDZone;
        }
        return isDZone ? $"{position}d" : position.ToString();
    }

    private static string WithDuration(string path, string? duration)
    {
        if (path.IndexOf('[') >= 0)
            return path;
        return path + (string.IsNullOrEmpty(duration) ? DefaultDuration : duration);
    }

    private static NoteExpressionData? Parse(string text)
        => NoteExpressionParser.TryParse(text, out var note, out _) ? note : null;

    private static bool IsSlotValid(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot))
            return false;
        if (!NoteSlotParser.TrySplit(slot, out var entries, out _))
            return false;
        if (entries.Count == 0)
            return false;
        foreach (var entry in entries)
        {
            if (!NoteExpressionParser.TryParse(entry.text, out var note, out _))
                return false;
            if (entry.fromSameHead && note.kind != NoteExpressionKind.Slide)
                return false;
        }
        return true;
    }
}
