namespace MajdataCore
{
    /// <summary>
    /// When a beat counts as an "each": the pairing that draws its notes yellow and
    /// picks the each variants of size, scroll type and spawn radius.
    /// </summary>
    /// <remarks>
    /// A beat can be an each for its trails and not for its heads. "1-5[8:1]*-3[8:1]"
    /// is one struck head and two slides travelling together, which is also how its
    /// note count reads: one head, two slides. So the head is drawn plain and both
    /// trails yellow, and the two questions need two rules.
    ///
    /// Shared because the editor's timeline and the view both decide this and used to
    /// disagree: the editor left headless slides out of the head count, the view
    /// counted every note, so "A8/8?-C-5[8:1]" was drawn plain while writing it and
    /// yellow while playing it in every build since 0.4.2. The two sides hold notes in
    /// unrelated types, so what is shared is the rule, not the loop.
    /// </remarks>
    public static class EachRule
    {
        /// <summary>
        /// A headless slide cannot pair into an each head: nothing is struck at its
        /// start, so there is no hit for another note to be simultaneous with.
        /// </summary>
        public static bool CountsTowardEach(bool isHeadlessSlide) => !isHeadlessSlide;

        /// <param name="beatMarkedEach">
        /// Set for notes grouped across a stream, which forces an each regardless.
        /// </param>
        public static bool IsEach(bool beatMarkedEach, int notesCountingTowardEach) =>
            beatMarkedEach || notesCountingTowardEach > 1;

        /// <summary>
        /// Whether this beat's slide trails are drawn yellow. Only other slides count:
        /// a slide beside a tap keeps a plain trail, because nothing else is moving
        /// alongside it, and a headless slide still counts because the trail itself is
        /// what travels.
        /// </summary>
        public static bool TrailsAreEach(int slidesOnBeat) => slidesOnBeat > 1;
    }
}
