using System.Text.Json.Serialization;

namespace BestInScript.API.Models
{
    /// <summary>
    /// 9-point anchor for the on-screen overlay box.
    /// </summary>
    public enum OverlayAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight,

        /// <summary>
        /// Free position: ignore the anchor/margin and place the pill at
        /// <see cref="OverlaySettings.PositionX"/>/<see cref="OverlaySettings.PositionY"/>.
        /// Set by the drag-to-position edit mode.
        /// </summary>
        Custom
    }

    /// <summary>
    /// User-tunable settings for the desktop status overlay.
    /// Serialized to overlay-settings.json next to scripts.json.
    /// </summary>
    public class OverlaySettings
    {
        /// <summary>Show the overlay window at all.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Index into <see cref="System.Windows.Forms.Screen.AllScreens"/>.
        /// -1 means "use the primary screen" (default).
        /// </summary>
        public int ScreenIndex { get; set; } = -1;

        /// <summary>Corner / edge of the chosen screen to dock to.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OverlayAnchor Anchor { get; set; } = OverlayAnchor.TopRight;

        /// <summary>Distance from the chosen edge in DIPs.</summary>
        public double Margin { get; set; } = 12;

        /// <summary>
        /// Custom top-left X, as a DIP offset from the target screen's top-left.
        /// Only used when <see cref="Anchor"/> is <see cref="OverlayAnchor.Custom"/>.
        /// </summary>
        public double PositionX { get; set; }

        /// <summary>
        /// Custom top-left Y, as a DIP offset from the target screen's top-left.
        /// Only used when <see cref="Anchor"/> is <see cref="OverlayAnchor.Custom"/>.
        /// </summary>
        public double PositionY { get; set; }

        /// <summary>Background alpha 0–1 (the card, not the text).</summary>
        public double Opacity { get; set; } = 0.80;

        /// <summary>Text size for the script-name label.</summary>
        public double FontSize { get; set; } = 12;

        /// <summary>Hide the overlay entirely when no script is running.</summary>
        public bool HideWhenIdle { get; set; } = false;

        // ── Diablo 4 event timers ───────────────────────────────────────────

        /// <summary>Master switch for the world-boss / helltide / legion rows.</summary>
        public bool EventsEnabled { get; set; } = true;

        /// <summary>World-boss row config. Alarm defaults ON (5 min before).</summary>
        public EventOverlayConfig WorldBoss { get; set; } = new() { AlarmEnabled = true };

        /// <summary>Helltide row config. Alarm defaults OFF.</summary>
        public EventOverlayConfig Helltide { get; set; } = new();

        /// <summary>Legion row config. Alarm defaults OFF.</summary>
        public EventOverlayConfig Legion { get; set; } = new();
    }
}
