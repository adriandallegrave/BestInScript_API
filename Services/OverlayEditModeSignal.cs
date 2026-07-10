namespace BestInScript.API.Services
{
    /// <summary>
    /// One-way signal from the web layer to the STA-thread overlay window: "enter
    /// drag-to-position edit mode". The overlay window lives on its own dispatcher
    /// thread and is only reachable via events, so <see cref="OverlayController"/>
    /// raises this and <c>OverlayHostedService</c> marshals it onto the UI thread.
    ///
    /// Committing / cancelling the drag is handled inside the overlay itself (the
    /// pill's ✓/✕ buttons), so this signal only needs the "enter" direction.
    /// </summary>
    public sealed class OverlayEditModeSignal
    {
        /// <summary>Raised when the UI asks the overlay to enter edit mode.</summary>
        public event Action? EnterRequested;

        /// <summary>Ask the live overlay to enter drag-to-position edit mode.</summary>
        public void RequestEnter() => EnterRequested?.Invoke();
    }
}
