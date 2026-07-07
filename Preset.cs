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
    }

    /// <summary>Runtime status of a preset, surfaced to the UI and overlay.</summary>
    public class PresetStatus
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string TriggerKey { get; set; } = "";
        public bool IsActive { get; set; }
        public bool ShowInOverlay { get; set; }
        public List<Guid> ScriptIds { get; set; } = [];
    }
}
