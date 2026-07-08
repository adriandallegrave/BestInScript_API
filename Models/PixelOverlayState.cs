namespace BestInScript.API.Models
{
    /// <summary>
    /// Live state of a pixel-triggered script, surfaced to the on-screen overlay.
    /// Always <see cref="NotApplicable"/> for blind-loop scripts.
    /// </summary>
    public enum PixelOverlayState
    {
        /// <summary>Script has no pixel trigger — no live state to report.</summary>
        NotApplicable = 0,

        /// <summary>Pixel matched "ready" on the most recent sample; the script is firing (or just fired in one-shot mode).</summary>
        Ready = 1,

        /// <summary>Pixel did not match "ready" — the script is watching and waiting.</summary>
        Waiting = 2,

        /// <summary>Screen could not be read at the watched coordinate (e.g. fullscreen-exclusive game).</summary>
        Unreadable = 3
    }
}
