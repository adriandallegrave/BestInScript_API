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
        /// Signal when within <see cref="AlarmLeadMinutes"/> of the event (no sound):
        /// Helltide/Legion blink a red countdown background; World Boss switches its
        /// row text to the amber warning color.
        /// </summary>
        public bool AlarmEnabled { get; set; }

        /// <summary>Minutes before the event at which the warning/blink starts.</summary>
        public int AlarmLeadMinutes { get; set; } = 5;

        /// <summary>Optional row accent [R,G,B]; null = default white label.</summary>
        public int[]? Color { get; set; }
    }
}
