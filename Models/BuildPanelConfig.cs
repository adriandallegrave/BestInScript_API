using System.Text.Json.Serialization;

namespace BestInScript.API.Models
{
    /// <summary>
    /// Placement and activation config for the overlay's build-guide panel.
    /// Part of <see cref="OverlaySettings"/>, persisted to overlay-settings.json.
    ///
    /// Deliberately global rather than profile-scoped: the cards themselves are per
    /// character/build (see <see cref="BuildCard"/>), but where the panel sits on screen
    /// and which key summons it are properties of the setup, like
    /// <see cref="OverlaySettings.StopAllHotkey"/>.
    /// </summary>
    public sealed class BuildPanelConfig
    {
        /// <summary>Show the build panel at all. When false the cycle key is ignored.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Key that cycles the panel: press → first card, press → next card, … → hidden.
        /// Keyboard-only (mouse buttons rejected). Null/empty (the default) disables it.
        /// The emergency-stop hotkey takes precedence if both are bound to the same key;
        /// otherwise this key wins over any script/preset bound to it.
        /// The key is never suppressed — the game still receives it — so prefer a key
        /// you have not bound in-game.
        /// </summary>
        public string? CycleHotkey { get; set; }

        /// <summary>
        /// Index into <see cref="System.Windows.Forms.Screen.AllScreens"/>.
        /// -1 means "use the primary screen" (default).
        /// </summary>
        public int ScreenIndex { get; set; } = -1;

        /// <summary>Corner / edge of the chosen screen to dock to.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OverlayAnchor Anchor { get; set; } = OverlayAnchor.MiddleCenter;

        /// <summary>Distance from the chosen edge in DIPs.</summary>
        public double Margin { get; set; } = 12;

        /// <summary>Custom top-left X as a DIP offset from the target screen's top-left.
        /// Only used when <see cref="Anchor"/> is <see cref="OverlayAnchor.Custom"/>.</summary>
        public double PositionX { get; set; }

        /// <summary>Custom top-left Y as a DIP offset from the target screen's top-left.
        /// Only used when <see cref="Anchor"/> is <see cref="OverlayAnchor.Custom"/>.</summary>
        public double PositionY { get; set; }

        /// <summary>
        /// Largest image width in DIPs. Nothing else clamps the panel — it sizes to its
        /// content — so this is what stops a 2000px paragon board from covering the screen.
        /// Also drives decode-time downscaling, so a big card costs little to render.
        /// </summary>
        public double MaxWidth { get; set; } = 900;

        /// <summary>Largest image height in DIPs. See <see cref="MaxWidth"/>.</summary>
        public double MaxHeight { get; set; } = 700;

        /// <summary>Background alpha 0–1 (the card, not the image).</summary>
        public double Opacity { get; set; } = 0.92;

        /// <summary>Text size for the card-name label and notes.</summary>
        public double FontSize { get; set; } = 13;
    }
}
