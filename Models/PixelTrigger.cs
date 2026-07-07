namespace BestInScript.API.Models
{
    /// <summary>
    /// Optional screen-color gate for a script. When a <see cref="ScriptConfig"/>
    /// has a PixelTrigger, the engine does not loop the steps blindly — instead
    /// it watches one screen pixel and only runs the step sequence when that
    /// pixel matches the "ready" reference color more closely than the
    /// "cooldown" reference color.
    ///
    /// Typical use: autocast a skill the moment it comes off cooldown. Sample
    /// the <see cref="ReadyColor"/> from the skill's action-bar icon while it is
    /// available, and the <see cref="CooldownColor"/> while it is greyed out.
    ///
    /// Reading screen pixels is passive (see ScreenColorService) — it does not
    /// touch the game process.
    /// </summary>
    public class PixelTrigger
    {
        /// <summary>Screen X coordinate of the watched pixel.</summary>
        public int X { get; set; }

        /// <summary>Screen Y coordinate of the watched pixel.</summary>
        public int Y { get; set; }

        /// <summary>RGB color sampled while the skill is READY (off cooldown). [R,G,B]</summary>
        public int[] ReadyColor { get; set; } = [255, 255, 255];

        /// <summary>RGB color sampled while the skill is ON COOLDOWN. [R,G,B]</summary>
        public int[] CooldownColor { get; set; } = [60, 60, 60];

        /// <summary>
        /// Maximum Euclidean color distance from <see cref="ReadyColor"/> still
        /// considered "ready". Also guards against firing when the pixel matches
        /// neither reference (e.g. a tooltip or effect is covering the icon).
        /// </summary>
        public int Tolerance { get; set; } = 40;

        /// <summary>
        /// Minimum gap (ms) after firing the step sequence before the pixel can
        /// trigger it again. Prevents double-firing while the icon is still lit
        /// on the frame after activation.
        /// </summary>
        public int ReArmDelayMs { get; set; } = 250;

        /// <summary>How often (ms) to sample the pixel while waiting for "ready".</summary>
        public int PollIntervalMs { get; set; } = 75;

        /// <summary>
        /// Averaging radius for the sample. 0 = single pixel, 1 = 3x3, 2 = 5x5.
        /// A small average steadies the reading against icon-edge highlights
        /// and single-frame animation artifacts.
        /// </summary>
        public int SampleRadius { get; set; } = 1;

        /// <summary>
        /// One-shot / edge-triggered mode.
        ///
        /// false (default — continuous autocast): fire the step sequence
        ///   whenever the pixel reads "ready". If the pixel stays ready, fires
        ///   repeatedly every <see cref="ReArmDelayMs"/>. Ideal for autocasting
        ///   a cooldown skill the instant its icon comes back.
        ///
        /// true (one-shot per cycle): fire once when the pixel becomes ready,
        ///   then refuse to fire again until at least one non-ready frame has
        ///   been observed. Ideal for actions that should run exactly once per
        ///   cooldown cycle (e.g., trigger an item the moment a buff icon
        ///   appears and not again until it expires and reappears).
        /// </summary>
        public bool RequireReset { get; set; } = false;
    }
}