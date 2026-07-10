namespace BestInScript.API.Models
{
    /// <summary>
    /// A named group of scripts that can be toggled on/off as a unit with a
    /// single trigger key. Activating a preset starts every member script as
    /// if the user had pressed each of their individual trigger keys;
    /// deactivating releases this preset's claim on each member.
    ///
    /// Multiple presets may share members. The engine reference-counts
    /// ownership per script, so a member only stops when the LAST owner
    /// (preset or direct keypress) releases it.
    /// </summary>
    public class Preset
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Friendly display name — also the "save name" of this preset.</summary>
        public string Name { get; set; } = "New Preset";

        /// <summary>
        /// Keyboard key that toggles this preset on/off. Must be unique
        /// across all scripts and presets (validated at the API layer).
        /// </summary>
        public string TriggerKey { get; set; } = "F1";

        /// <summary>Ids of the scripts this preset controls.</summary>
        public List<Guid> ScriptIds { get; set; } = [];

        /// <summary>Surface this preset in the on-screen overlay while active.</summary>
        public bool ShowInOverlay { get; set; } = false;

        /// <summary>
        /// Optional overlay accent color as [R,G,B] (each 0–255). Tints this
        /// preset's label text and icon in the on-screen overlay. Null (the
        /// default) = plain white. The status dot color is unaffected.
        /// </summary>
        public int[]? OverlayColor { get; set; }

        /// <summary>
        /// Optional short emoji/glyph shown before the name in the overlay row.
        /// Null/empty (the default) = no icon.
        /// </summary>
        public string? OverlayIcon { get; set; }
    }
}
