using System;

namespace MajdataCore
{
    public enum EditorDirectiveKind
    {
        None,

        /// <summary>"@{4}1," - an overlay chart line laid over the beat above it.</summary>
        Overlay,

        /// <summary>"@start" - where the exported media clip begins.</summary>
        ClipStart,

        /// <summary>"@end" - where the exported media clip ends.</summary>
        ClipEnd,

        /// <summary>"@4/4" or "&amp;4/4" - the editor grid's time signature.</summary>
        Meter,

        /// <summary>"@NULL" or "&amp;NULL" - clears the section tint.</summary>
        SectionReset,

        /// <summary>"@FF0000" or "&amp;FF0000" - tints the section that follows.</summary>
        SectionColor
    }

    public readonly struct EditorDirective
    {
        public EditorDirective(
            EditorDirectiveKind kind,
            int start,
            int length,
            int numerator = 0,
            int denominator = 0,
            string color = "")
        {
            this.kind = kind;
            this.start = start;
            this.length = length;
            this.numerator = numerator;
            this.denominator = denominator;
            this.color = color;
        }

        public readonly EditorDirectiveKind kind;
        public readonly int start;

        /// <summary>
        /// How much text the directive owns. Meter and the clip marks own the rest
        /// of their line, because that is what the chart parser consumes; a section
        /// tint owns only its own characters, because notes may follow it.
        /// </summary>
        public readonly int length;

        public readonly int numerator;
        public readonly int denominator;
        public readonly string color;
    }

    /// <summary>
    /// Reads the '@' and '&amp;' directives that steer the editor rather than the
    /// playfield. Every one of them starts a line and owns it, so the scan never
    /// looks past the newline. The chart parser, the syntax colorizer and the
    /// section tint renderer each used to carry their own version of these rules,
    /// which disagreed: the colorizer searched the whole document for the '/' of a
    /// meter and would paint several lines as one marker, and it accepted prefixes
    /// like "@started" that the chart parser reads as note text.
    /// </summary>
    public static class EditorDirectiveScanner
    {
        public static bool TryRead(string text, int index, out EditorDirective directive)
        {
            directive = default;
            if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length ||
                text[index] is not ('@' or '&'))
                return false;

            var lineEnd = text.IndexOfAny(new[] { '\r', '\n' }, index);
            if (lineEnd < 0)
                lineEnd = text.Length;
            var lineLength = lineEnd - index;
            var body = text.Substring(index + 1, lineEnd - index - 1).Trim();

            if (text[index] == '@' &&
                TryReadOverlay(text, index, lineEnd, out var headLength))
            {
                directive = new EditorDirective(
                    EditorDirectiveKind.Overlay, index, headLength);
                return true;
            }

            if (text[index] == '@' &&
                string.Equals(body, "start", StringComparison.OrdinalIgnoreCase))
            {
                directive = new EditorDirective(
                    EditorDirectiveKind.ClipStart, index, lineLength);
                return true;
            }

            if (text[index] == '@' &&
                string.Equals(body, "end", StringComparison.OrdinalIgnoreCase))
            {
                directive = new EditorDirective(
                    EditorDirectiveKind.ClipEnd, index, lineLength);
                return true;
            }

            var slash = body.IndexOf('/');
            if (slash > 0 &&
                int.TryParse(body.Substring(0, slash).Trim(), out var numerator) &&
                int.TryParse(body.Substring(slash + 1).Trim(), out var denominator) &&
                numerator > 0 && denominator > 0)
            {
                directive = new EditorDirective(
                    EditorDirectiveKind.Meter, index, lineLength,
                    numerator, denominator);
                return true;
            }

            // A tint marks the section that follows it, so notes may share its line
            // and it owns only its own characters.
            if (index + 5 <= lineEnd &&
                string.Equals(
                    text.Substring(index + 1, 4), "NULL",
                    StringComparison.OrdinalIgnoreCase))
            {
                directive = new EditorDirective(
                    EditorDirectiveKind.SectionReset, index, 5);
                return true;
            }

            if (index + 7 <= lineEnd)
            {
                var candidate = text.Substring(index + 1, 6);
                if (IsHex(candidate))
                {
                    directive = new EditorDirective(
                        EditorDirectiveKind.SectionColor, index, 7,
                        color: candidate);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// An overlay line is "@{division}" followed by the notes to lay over the
        /// beat. The directive is only the head; the chart parser owns the notes,
        /// which is why the reported length stops at the closing brace.
        /// </summary>
        private static bool TryReadOverlay(
            string text, int index, int lineEnd, out int headLength)
        {
            headLength = 0;
            if (index + 1 < text.Length && text[index + 1] == '*')
            {
                var blockEnd = text.IndexOf("*@", index + 2,
                    StringComparison.Ordinal);
                if (blockEnd < 0)
                    return false;
                headLength = blockEnd + 2 - index;
                return true;
            }
            if (index + 2 >= text.Length || text[index + 1] != '{')
                return false;

            var closeBrace = text.IndexOf('}', index + 2);
            if (closeBrace < 0 || closeBrace >= lineEnd ||
                !int.TryParse(
                    text.Substring(index + 2, closeBrace - index - 2),
                    out var division) ||
                division <= 0)
                return false;

            headLength = closeBrace - index + 1;
            return true;
        }

        private static bool IsHex(string value)
        {
            foreach (var character in value)
                if (!Uri.IsHexDigit(character))
                    return false;
            return true;
        }
    }
}
