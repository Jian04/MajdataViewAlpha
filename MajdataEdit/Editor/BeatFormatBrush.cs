using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace MajdataEdit.Editor;

internal static class BeatFormatBrush
{
    private static readonly Regex BeatMarker = new(@"\{(?<beat>\d+)\}", RegexOptions.Compiled);

    public static string Transform(string text, int? requestedBeat)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sourceBeat = FindFirstBeat(text);
        if (sourceBeat <= 0)
            return text;

        var targetBeat = requestedBeat is > 0
            ? new BigInteger(requestedBeat.Value)
            : FindCommonBeat(text, sourceBeat);
        return TransformWithContext(text, sourceBeat, targetBeat);
    }

    public static string TransformSelection(string fullText, int selectionStart, int selectionLength, int? requestedBeat)
    {
        if (string.IsNullOrEmpty(fullText) || selectionLength <= 0)
            return string.Empty;

        selectionStart = Math.Clamp(selectionStart, 0, fullText.Length);
        selectionLength = Math.Clamp(selectionLength, 0, fullText.Length - selectionStart);
        var selected = fullText.Substring(selectionStart, selectionLength);
        var activeBeat = FindActiveBeatBefore(fullText, selectionStart);
        var sourceBeat = activeBeat > 0 ? activeBeat : FindFirstBeat(selected);
        if (sourceBeat <= 0)
            return selected;

        var targetBeat = requestedBeat is > 0
            ? new BigInteger(requestedBeat.Value)
            : FindCommonBeat(selected, sourceBeat);
        return TransformWithContext(selected, sourceBeat, targetBeat);
    }

    private static string TransformWithContext(string text, BigInteger defaultBeat, BigInteger targetBeat)
    {
        if (defaultBeat <= 0 || targetBeat <= 0)
            return text;

        var tokens = Tokenize(text, defaultBeat);
        if (tokens.Count == 0)
            return StripBeatMarkers(text);

        var output = new StringBuilder(text.Length);
        var currentBeat = BigInteger.Zero;
        var run = new List<CommaToken>();
        var leadText = string.Empty;
        var layout = new StringBuilder();

        void SwitchBeat(BigInteger beat)
        {
            if (beat <= 0 || currentBeat == beat)
                return;
            output.Append('{');
            output.Append(beat);
            output.Append('}');
            currentBeat = beat;
        }

        void FlushRun()
        {
            var duration = Fraction.Zero;
            foreach (var slot in run)
                duration += Fraction.Unit(slot.Beat);

            var targetSlots = duration * targetBeat;
            if (duration.Numerator > 0 && targetSlots.IsInteger)
            {
                SwitchBeat(targetBeat);
                output.Append(leadText);
                AppendRepeated(output, ',', targetSlots.Numerator);
            }
            else
            {
                for (var index = 0; index < run.Count; index++)
                {
                    SwitchBeat(run[index].Beat);
                    if (index == 0)
                        output.Append(leadText);
                    output.Append(',');
                }
                if (run.Count == 0)
                    output.Append(leadText);
            }

            output.Append(layout);
            layout.Clear();
            run.Clear();
            leadText = string.Empty;
        }

        foreach (var token in tokens)
        {
            var textPart = StripBeatMarkers(token.Text);
            if (string.IsNullOrWhiteSpace(textPart))
            {
                if (token.HasComma)
                {
                    run.Add(token);
                    layout.Append(textPart);
                }
                else
                {
                    FlushRun();
                    output.Append(textPart);
                }
                continue;
            }

            FlushRun();
            if (!token.HasComma)
            {
                output.Append(textPart);
                continue;
            }

            leadText = textPart;
            run.Add(token);
        }

        FlushRun();
        return output.ToString();
    }

    private static List<CommaToken> Tokenize(string text, BigInteger initialBeat)
    {
        var result = new List<CommaToken>();
        var start = 0;
        var beat = initialBeat;
        var tracker = new MajdataCore.ChartBracketTracker();

        for (var i = 0; i < text.Length; i++)
        {
            if (tracker.IsTopLevel && text[i] == '{')
            {
                var close = text.IndexOf('}', i + 1);
                if (close > i &&
                    BigInteger.TryParse(text.Substring(i + 1, close - i - 1), out var parsedBeat) &&
                    parsedBeat > 0)
                {
                    beat = parsedBeat;
                    i = close;
                    continue;
                }
                continue;
            }

            tracker.Advance(text, i);
            if (text[i] == ',' && tracker.IsTopLevel)
            {
                result.Add(new CommaToken(text.Substring(start, i - start), true, beat));
                start = i + 1;
            }
        }

        result.Add(new CommaToken(text.Substring(start), false, beat));
        return result;
    }

    private static void AppendRepeated(StringBuilder output, char value, BigInteger count)
    {
        const int chunkSize = 1_000_000;
        while (count > 0)
        {
            var chunk = count > chunkSize ? chunkSize : (int)count;
            output.Append(value, chunk);
            count -= chunk;
        }
    }

    private static string StripBeatMarkers(string text) => BeatMarker.Replace(text, "");

    private static BigInteger FindFirstBeat(string text)
    {
        var match = BeatMarker.Match(text);
        return match.Success &&
               BigInteger.TryParse(match.Groups["beat"].Value, out var beat) &&
               beat > 0
            ? beat
            : BigInteger.MinusOne;
    }

    private static BigInteger FindCommonBeat(string text, BigInteger inheritedBeat)
    {
        var common = inheritedBeat > 0 ? inheritedBeat : BigInteger.One;
        foreach (Match match in BeatMarker.Matches(text))
        {
            if (!BigInteger.TryParse(match.Groups["beat"].Value, out var beat) || beat <= 0)
                continue;
            common = LeastCommonMultiple(common, beat);
        }
        return common;
    }

    private static BigInteger LeastCommonMultiple(BigInteger left, BigInteger right) =>
        BigInteger.Abs(left / BigInteger.GreatestCommonDivisor(left, right) * right);

    private static BigInteger FindActiveBeatBefore(string text, int position)
    {
        var source = text.Substring(0, Math.Clamp(position, 0, text.Length));
        var matches = BeatMarker.Matches(source);
        if (matches.Count == 0)
            return BigInteger.MinusOne;

        return BigInteger.TryParse(matches[^1].Groups["beat"].Value, out var beat) && beat > 0
            ? beat
            : BigInteger.MinusOne;
    }

    private readonly record struct CommaToken(string Text, bool HasComma, BigInteger Beat);

    private readonly record struct Fraction(BigInteger Numerator, BigInteger Denominator)
    {
        public static Fraction Zero => new(BigInteger.Zero, BigInteger.One);
        public bool IsInteger => Denominator == BigInteger.One;

        public static Fraction Unit(BigInteger denominator) =>
            denominator > 0 ? new Fraction(BigInteger.One, denominator) : Zero;

        public static Fraction operator +(Fraction left, Fraction right) =>
            Normalize(
                left.Numerator * right.Denominator + right.Numerator * left.Denominator,
                left.Denominator * right.Denominator);

        public static Fraction operator *(Fraction value, BigInteger multiplier) =>
            Normalize(value.Numerator * multiplier, value.Denominator);

        private static Fraction Normalize(BigInteger numerator, BigInteger denominator)
        {
            if (numerator.IsZero)
                return Zero;
            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            return new Fraction(numerator / divisor, denominator / divisor);
        }
    }
}
