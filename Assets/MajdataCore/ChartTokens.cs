using System.Collections.Generic;

namespace MajdataCore
{
    public enum ChartTokenKind
    {
        Position,
        Shape,
        Modifier,
        Duration
    }

    public readonly struct ChartToken
    {
        public ChartToken(int start, int length, ChartTokenKind kind)
        {
            this.start = start;
            this.length = length;
            this.kind = kind;
        }

        public readonly int start;
        public readonly int length;
        public readonly ChartTokenKind kind;

        public int End => start + length;
    }

    /// <summary>
    /// Where each piece of a note sits in the text it was parsed from.
    ///
    /// The offsets are deliberately not fields on <see cref="SlidePathSegmentData"/>
    /// or <see cref="SlidePositionData"/>: those classes are serialized into the
    /// majson that ships to the View on every preview refresh, and three of them
    /// live in each segment, so storing offsets there would grow every chart and
    /// slow down the very preview path that has to stay responsive. Only the
    /// editor ever wants offsets, so it passes a collector in and everyone else
    /// passes nothing and pays nothing.
    ///
    /// The parser reports these during the scan it already performs, so the
    /// offsets cannot drift from what was parsed the way a second scan would.
    ///
    /// Nothing in the editor or the player consumes this yet: it is an entry
    /// point for a caller that needs offsets, not a live path. Syntax colouring
    /// deliberately does not use it, because that runs on every repaint of every
    /// visible line and the cheap character loop it already has is faster than
    /// any parse. The regression suite checks the spans tile every path in the
    /// chart corpus, so an unused entry point still cannot rot unnoticed.
    /// </summary>
    public sealed class ChartTokenList
    {
        public readonly List<ChartToken> tokens = new List<ChartToken>();

        public void Add(int start, int length, ChartTokenKind kind)
        {
            // Empty runs are the normal case for an absent modifier or duration,
            // and a caller colouring text has nothing to do with them.
            if (length > 0)
                tokens.Add(new ChartToken(start, length, kind));
        }

        public void Clear() => tokens.Clear();
    }
}
