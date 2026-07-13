namespace BestInScript.API.Models
{
    /// <summary>
    /// Per-event overlay configuration (one each for World Boss / Helltide /
    /// Legion). Part of <see cref="OverlaySettings"/>, persisted to
    /// overlay-settings.json.
    /// </summary>
    public sealed class EventOverlayConfig
    {
        /// <summary>Show this event's row in the overlay.</summary>
        public bool Show { get; set; } = true;

        /// <summary>
        /// Master switch for the staged, text-only alarm (no sound, no background).
        /// When on, the row text passes through three stages as the event nears:
        /// main color → <see cref="WarningColor"/> (solid) inside
        /// <see cref="WarningLeadMinutes"/> → blinking (warning ↔ main) inside the
        /// closer <see cref="AlarmLeadMinutes"/>.
        /// </summary>
        public bool AlarmEnabled { get; set; }

        /// <summary>Minutes before the event at which the text switches to the warning color.</summary>
        public int WarningLeadMinutes { get; set; } = 30;

        /// <summary>Minutes before the event at which the text starts blinking (warning ↔ main).</summary>
        public int AlarmLeadMinutes { get; set; } = 5;

        /// <summary>Main row color [R,G,B]; null = default white label.</summary>
        public int[]? Color { get; set; }

        /// <summary>Warning/blink color [R,G,B]; null = default amber (#FFAA33).</summary>
        public int[]? WarningColor { get; set; }
    }
}
