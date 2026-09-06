using MajdataCore;

namespace MajdataEdit;

// Whether the text being typed is still waiting for its duration bracket. The
// note grammar answers that question, so the completion popup follows the same
// rules as the parser. This used to be one long pattern repeating the positions,
// modifiers and slide shapes, which fell behind the grammar whenever a form was
// added.
internal static class NoteDurationTarget
{
    public static bool TryFromTypedText(string tail, out bool slideTarget)
    {
        slideTarget = false;
        if (string.IsNullOrEmpty(tail) || tail[^1] == ']')
            return false;

        // The longest tail the parser still recognizes is the note being typed.
        for (var start = 0; start < tail.Length; start++)
            if (WantsDuration(tail.Substring(start), out slideTarget))
                return true;
        return false;
    }

    public static bool TryFromSelection(string selection, out bool slideTarget)
    {
        slideTarget = false;
        if (string.IsNullOrWhiteSpace(selection) ||
            selection.IndexOfAny(new[] { '\r', '\n', ',', '/' }) >= 0)
            return false;

        return WantsDuration(selection.Trim(), out slideTarget);
    }

    private static bool WantsDuration(string candidate, out bool slideTarget)
    {
        slideTarget = false;
        if (string.IsNullOrEmpty(candidate))
            return false;

        // A same-head branch is written as a bare shape ("1-5[8:1]*-7"), which
        // only becomes a note once the shared head is put back in front of it.
        if (SlidePathParser.ContainsSlideShape(candidate.Substring(0, 1)))
            return WantsDuration("1" + candidate, out slideTarget);

        if (!NoteExpressionParser.TryParse(
                candidate, out var note, out _, forPreview: true))
            return false;

        switch (note.kind)
        {
            case NoteExpressionKind.Slide:
                slideTarget = true;
                return note.path.segments.Count > 0 &&
                       note.path.segments[^1].duration.Length == 0;
            case NoteExpressionKind.Hold:
            case NoteExpressionKind.TouchHold:
                return note.isZeroLengthHold;
            default:
                return false;
        }
    }
}
