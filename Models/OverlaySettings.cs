using System.Text.Json.Serialization;

namespace BestInScript.API.Overlay
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
        BottomRight
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

        /// <summary>Background alpha 0–1 (the card, not the text).</summary>
        public double Opacity { get; set; } = 0.80;

        /// <summary>Text size for the script-name label.</summary>
        public double FontSize { get; set; } = 12;

        /// <summary>Hide the overlay entirely when no script is running.</summary>
        public bool HideWhenIdle { get; set; } = false;
    }
}
