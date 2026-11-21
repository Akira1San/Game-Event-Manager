namespace Game
{
    /// <summary>
    /// <c>set root motion on</c> / <c>off</c> (Task 33 Part D): whether the clip's extracted motion
    /// moves the actor, and the honest limits of a text word there. Engine-free, so the rules can be
    /// tested without Flax: the bridge only carries out what this class reports.
    /// </summary>
    public static class RootMotion
    {
        /// <summary>Clips never move the actor unless a sheet asks, so every existing sheet is unchanged.</summary>
        public const bool Default = false;

        /// <summary>What a <c>set root motion</c> line can and cannot change on the actor's model.</summary>
        public enum Outcome
        {
            /// <summary>No animation graph on the model, so there is nothing to aim the motion at.</summary>
            NoGraph,

            /// <summary>
            /// A single-clip fallback graph built by the bridge. Those are always built with root motion
            /// ignored: a whole-body clip that also moved the actor would double the displacement, since
            /// the sheet's own <c>move</c> / <c>goto</c> already owns the transform. So the word cannot
            /// take effect here.
            /// </summary>
            FallbackIgnores,

            /// <summary>
            /// An authored base graph. The bridge points its <c>RootMotionTarget</c> at the sheet's actor
            /// once — without a target Flax applies the motion to the model itself — but whether that
            /// graph extracts motion at all is authored content: Flax's C# API exposes the root-motion
            /// mode only as an <c>InitAsAnimation</c> build flag, so the word cannot switch it on or off
            /// at runtime.
            /// </summary>
            AuthoredGraph,
        }

        /// <summary>
        /// Classifies what the word can do for the graph the model currently has.
        /// </summary>
        /// <param name="hasGraph">Whether the model has an animation graph at all.</param>
        /// <param name="bridgeBuiltGraph">True when that graph is one of the bridge's own single-clip
        /// graphs (it was created with <c>InitAsAnimation</c>), false for an authored base graph.</param>
        public static Outcome OutcomeFor(bool hasGraph, bool bridgeBuiltGraph)
        {
            if (!hasGraph)
                return Outcome.NoGraph;
            return bridgeBuiltGraph ? Outcome.FallbackIgnores : Outcome.AuthoredGraph;
        }

        /// <summary>
        /// Whether a layered (slot) clip may move the actor. Never: root motion belongs to the
        /// locomotion clip, so an upper-body clip cannot double-apply displacement or slide the feet.
        /// </summary>
        public const bool SlotsMove = false;
    }
}
