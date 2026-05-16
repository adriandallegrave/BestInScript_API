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
    }
}