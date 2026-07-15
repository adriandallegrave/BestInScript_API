namespace BestInScript.API.Engine
{
    /// <summary>
    /// Pure state machine for the build panel's cycle key: hidden → first card →
    /// next card → … → past the last card → hidden again. Kept WPF-free so it is
    /// unit-testable (mirrors <see cref="OverlayPositionCalculator"/>).
    ///
    /// The selection is an index into the card list in cycle order, or
    /// <see cref="Hidden"/>.
    /// </summary>
    public static class BuildCardCycleCalculator
    {
        /// <summary>Nothing selected — the panel is not on screen.</summary>
        public const int Hidden = -1;

        /// <summary>
        /// Selection after one press of the cycle key. Wraps past the last card back to
        /// <see cref="Hidden"/> rather than round-robining, so one key both pages through
        /// the cards and dismisses the panel. With no cards there is nothing to show.
        /// </summary>
        public static int Next(int current, int count)
        {
            if (count <= 0) return Hidden;
            if (current < 0) return 0;

            var next = current + 1;
            return next >= count ? Hidden : next;
        }

        /// <summary>
        /// Selection re-validated against the current card count — used after cards are
        /// added or deleted in the web UI while the panel is open, so a stale index can
        /// never index past the end.
        /// </summary>
        public static int Clamp(int current, int count)
            => (count <= 0 || current < 0 || current >= count) ? Hidden : current;
    }
}
