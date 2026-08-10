using System.Text;

namespace MajdataEdit;

internal static class AlphaCommandBoundary
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
            return text[i] is ',' or '}' or '>' or ')';
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
