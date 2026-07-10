namespace BestInScript.API.Models
{
    /// <summary>
    /// A named macro script bound to a trigger key.
    /// Pressing the trigger key toggles the script on/off.
    /// The script loops through its steps indefinitely until toggled off.
    /// </summary>
    public class ScriptConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Friendly display name.</summary>
        public string Name { get; set; } = "New Script";

        /// <summary>
        /// Keyboard key that toggles this script on/off.
        /// Use key names like "3", "F1", "NumPad0", "Q", etc.
        /// Mouse buttons are NOT valid trigger keys (use them in steps instead).
        /// </summary>
        public string TriggerKey { get; set; } = "3";

        /// <summary>Ordered list of steps executed in a loop.</summary>
        public List<ScriptStep> Steps { get; set; } = [];

        /// <summary>Minimum random delay between steps (seconds). Min allowed: 0.1</summary>
        public double DelayMin { get; set; } = 0.4;

        /// <summary>Maximum random delay between steps (seconds). Max allowed: 5.0</summary>
        public double DelayMax { get; set; } = 0.6;

        /// <summary>
        /// Optional screen-color gate. When null (the default) the script loops
        /// its steps continuously while toggled on. When set, the engine watches
        /// the configured pixel and only runs the step sequence while that pixel
        /// matches the "ready" color — e.g. to autocast a skill the instant it
        /// comes off cooldown. See <see cref="PixelTrigger"/>.
        /// </summary>
        public PixelTrigger? PixelTrigger { get; set; }

        /// <summary>
        /// Surface this script in the on-screen overlay. When false (the
        /// default) the script never appears in the overlay regardless of
        /// whether it is toggled on. When true:
        ///   • Pixel-triggered scripts display their live state (READY,
        ///     waiting, unreadable) while toggled on — informational style.
        ///   • Blind-loop scripts display as ON while toggled on.
        /// In both cases the script disappears from the overlay when toggled
        /// off, so the overlay only shows what is actually active.
        /// </summary>
        public bool ShowInOverlay { get; set; } = false;

        /// <summary>
        /// Optional overlay accent color as [R,G,B] (each 0–255). Tints this
        /// entry's label text and icon in the on-screen overlay so multiple
        /// active rows are easy to tell apart. Null (the default) = plain white.
        /// The status dot color is unaffected — it always reflects run state.
        /// </summary>
        public int[]? OverlayColor { get; set; }

        /// <summary>
        /// Optional short emoji/glyph shown before the name in the overlay row
        /// (e.g. "⚔️"). Null/empty (the default) = no icon.
        /// </summary>
        public string? OverlayIcon { get; set; }
    }
}