namespace BestInScript.API.Models
{
    /// <summary>Runtime status of a script, surfaced to the UI and overlay.</summary>
    public class ScriptStatus
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string TriggerKey { get; set; } = "";
        public bool IsRunning { get; set; }

        /// <summary>True if the script's config has ShowInOverlay enabled.</summary>
        public bool ShowInOverlay { get; set; }

        /// <summary>True if the script has a PixelTrigger configured (display hint for the overlay).</summary>
        public bool HasPixelTrigger { get; set; }

        /// <summary>Live pixel verdict for pixel-triggered scripts; NotApplicable otherwise.</summary>
        public PixelOverlayState PixelState { get; set; }
    }
}
