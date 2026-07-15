namespace BestInScript.API.Models
{
    /// <summary>
    /// One page of build-guide reference art shown in the overlay's build panel —
    /// typically a crop of a build guide (paragon board, target gear, skill tree)
    /// taken with the Windows snipping tool and pasted into the web UI.
    ///
    /// Profile-scoped: the row lives in <c>profiles/&lt;name&gt;/build-cards.json</c> and the
    /// image bytes in <c>profiles/&lt;name&gt;/cards/&lt;ImageFileName&gt;</c>, so cards follow the
    /// active profile and are carried by the copy-current flow at season rollover.
    ///
    /// Purely informational — the panel is click-through and sends no input. This is a
    /// picture pinned over the screen, nothing more.
    /// </summary>
    public class BuildCard
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Friendly display name, shown above the image in the panel (e.g. "Endgame Paragon").</summary>
        public string Name { get; set; } = "New Card";

        /// <summary>
        /// Position in the cycle order. The panel's cycle key walks cards by ascending
        /// Order, then by <see cref="Name"/> to keep ties stable.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Bare file name of the image inside the profile's card directory (e.g.
        /// "3f2a....png"). Always server-generated from <see cref="Id"/> — never a
        /// client-supplied name, so it can't escape the profile directory.
        /// Null/empty means the row has no image yet.
        /// </summary>
        public string? ImageFileName { get; set; }

        /// <summary>Optional free text rendered under the image (e.g. a stat priority reminder).</summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Per-card size multiplier applied on top of the panel's max width/height,
        /// so a dense gear table can render larger than a big paragon board.
        /// Clamped to 0.25–2.0 at the API layer.
        /// </summary>
        public double Scale { get; set; } = 1.0;
    }
}
