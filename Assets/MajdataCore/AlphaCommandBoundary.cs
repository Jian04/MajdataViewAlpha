using System;
using System.Text;

namespace MajdataCore
{
    /// <summary>
    /// Where an alpha command token starts and ends. This used to live in the
    /// editor with three looser copies next to it (the colorizer, the beat
    /// formatter and the preview each had their own '&lt;' scan), so the same text
    /// could be treated as a command by one layer and as note text by another.
    /// The scan is line bounded: a command never spans a newline, which is what
    /// kept the old copies from swallowing later lines while looking for '&gt;'.
    /// </summary>
    public static class AlphaCommandBoundary
    {
        public static bool IsPotentialStart(string text, int openIndex)
        {
            if (string.IsNullOrEmpty(text) || openIndex < 0 || openIndex + 1 >= text.Length ||
                text[openIndex] != '<' || !char.IsLetter(text[openIndex + 1]))
                return false;

            for (var i = openIndex - 1; i >= 0; i--)
            {
                if (text[i] is '\r' or '\n')
                    return true;
                if (char.IsWhiteSpace(text[i]))
                    continue;
                if (text[i] is ',' or '}' or '>' or ')')
                    return true;
                return false;
            }
            return true;
        }

        public static bool TryGetCommand(string text, int openIndex, out int closeIndex)
        {
            closeIndex = -1;
            if (!IsPotentialStart(text, openIndex))
                return false;

            var close = text.IndexOf('>', openIndex + 1);
            var newline = text.IndexOfAny(new[] { '\r', '\n' }, openIndex + 1);
            if (close < 0 || newline >= 0 && newline < close)
                return false;

            var star = text.IndexOf('*', openIndex + 1, close - openIndex - 1);
            if (star <= openIndex + 1)
                return false;
            for (var i = openIndex + 1; i < star; i++)
                if (!char.IsLetterOrDigit(text[i]) && text[i] != '_')
                    return false;

            closeIndex = close;
            return true;
        }

        /// <summary>
        /// The token plus what it names, for callers that decide how to present it
        /// rather than how to parse it. A well formed token whose name no command
        /// answers to is reported with isKnown false instead of being rejected, so
        /// the editor can show it as a typo rather than as note text.
        /// </summary>
        public static bool TryGetToken(
            string text, int openIndex, out AlphaCommandToken token)
        {
            token = default;
            if (!TryGetCommand(text, openIndex, out var close))
                return false;

            var star = text.IndexOf('*', openIndex + 1, close - openIndex - 1);
            var name = text.Substring(openIndex + 1, star - openIndex - 1);
            token = new AlphaCommandToken(
                openIndex,
                close - openIndex + 1,
                name,
                AlphaCommandGrammar.TryFind(name, out _));
            return true;
        }

        public static string RemoveCommands(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var result = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '<' && TryGetCommand(text, i, out var close))
                {
                    i = close;
                    continue;
                }
                result.Append(text[i]);
            }
            return result.ToString();
        }
    }

    public readonly struct AlphaCommandToken
    {
        public AlphaCommandToken(int start, int length, string name, bool isKnown)
        {
            this.start = start;
            this.length = length;
            this.name = name;
            this.isKnown = isKnown;
        }

        public readonly int start;
        public readonly int length;
        public readonly string name;
        public readonly bool isKnown;
    }
}
