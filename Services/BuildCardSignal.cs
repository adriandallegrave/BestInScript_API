namespace BestInScript.API.Services
{
    /// <summary>
    /// One-way signal from the engine to the STA-thread build panel. The panel window
    /// lives on its own dispatcher thread and is only reachable via events, so
    /// <c>ScriptCoordinator</c> (on the hook thread) raises these and
    /// <c>OverlayHostedService</c> marshals them onto the UI thread.
    ///
    /// Keeping this seam means the coordinator never touches WPF and stays testable.
    /// </summary>
    public sealed class BuildCardSignal
    {
        /// <summary>Raised when the cycle hotkey is pressed: advance to the next card, or hide past the last.</summary>
        public event Action? CycleRequested;

        /// <summary>Raised when the panel must come down regardless of state (emergency stop).</summary>
        public event Action? HideRequested;

        /// <summary>Raised when cards change in the web UI, so an open panel reloads instead of showing a stale card.</summary>
        public event Action? CardsChanged;

        /// <summary>Raised when the web UI asks the panel to enter drag-to-position edit mode.
        /// Separate from <c>OverlayEditModeSignal</c>, which arms the status pill.</summary>
        public event Action? EnterEditRequested;

        public void RequestCycle() => CycleRequested?.Invoke();

        public void RequestHide() => HideRequested?.Invoke();

        public void NotifyCardsChanged() => CardsChanged?.Invoke();

        public void RequestEnterEdit() => EnterEditRequested?.Invoke();
    }
}
