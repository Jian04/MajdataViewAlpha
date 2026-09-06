using System;

namespace MajdataCore
{
    public enum ChartBracket
    {
        None,
        Round,
        Curly,
        Square,
        Command
    }

    /// <summary>
    /// Chart level bracket nesting, walked one character at a time.
    ///
    /// Several editor layers each kept their own copy of this loop, and they
    /// disagreed about '&lt;'. A bare '&lt;' is a slide shape, and only
    /// <see cref="AlphaCommandBoundary"/> can tell that apart from the start of a
    /// command, so a copy that counted every '&lt;' as nesting would decide it was
    /// inside a bracket for the whole rest of the line. That is what made the
    /// error squiggle on a '&lt;' slide run past its own beat.
    /// </summary>
    public struct ChartBracketTracker
    {
        private int round;
        private int curly;
        private int square;
        private int command;

        public bool IsTopLevel =>
            round == 0 && curly == 0 && square == 0 && command == 0;

        /// <summary>
        /// The innermost group, for callers that pick a colour rather than just
        /// asking whether they are nested. Charts do not nest these in practice,
        /// so the order only decides what a malformed chart reports.
        /// </summary>
        public ChartBracket Innermost =>
            command > 0 ? ChartBracket.Command :
            square > 0 ? ChartBracket.Square :
            curly > 0 ? ChartBracket.Curly :
            round > 0 ? ChartBracket.Round :
            ChartBracket.None;

        /// <summary>
        /// Consumes text[index]. An opener takes effect immediately, so asking
        /// <see cref="IsTopLevel"/> right after advancing over '[' reports that we
        /// are inside it. A closer with nothing open is ignored rather than
        /// driving the depth negative, so one stray ']' cannot make the rest of
        /// the line look nested.
        /// </summary>
        public void Advance(string text, int index)
        {
            switch (text[index])
            {
                case '(':
                    round++;
                    break;
                case ')':
                    round = Math.Max(0, round - 1);
                    break;
                case '{':
                    curly++;
                    break;
                case '}':
                    curly = Math.Max(0, curly - 1);
                    break;
                case '[':
                    square++;
                    break;
                case ']':
                    square = Math.Max(0, square - 1);
                    break;
                // Only a token that a command actually answers to opens a group;
                // '1<5[8:1]' is a slide and must stay at top level.
                case '<' when AlphaCommandBoundary.TryGetCommand(text, index, out _):
                    command++;
                    break;
                case '>' when command > 0:
                    command--;
                    break;
            }
        }
    }
}
