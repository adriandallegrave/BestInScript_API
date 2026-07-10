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

        /// <summary>Blink the countdown when within <see cref="AlarmLeadMinutes"/> of the event (no sound).</summary>
        public bool AlarmEnabled { get; set; }

        /// <summary>Minutes before the event at which the alarm blink starts.</summary>
        public int AlarmLeadMinutes { get; set; } = 5;

        /// <summary>Optional row accent [R,G,B]; null = default white label.</summary>
        public int[]? Color { get; set; }
    }
}
