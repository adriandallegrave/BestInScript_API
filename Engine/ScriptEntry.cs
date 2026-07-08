using BestInScript.API.Models;

namespace BestInScript.API.Engine
{
    /// <summary>Runtime state of one registered script.</summary>
    public sealed class ScriptEntry(ScriptConfig config)
    {
        public ScriptConfig Config { get; } = config;
        public CancellationTokenSource? Cts { get; set; }

        /// <summary>
        /// Set of owners that have claimed this script. Sentinel
        /// <see cref="ScriptCoordinator.UserOwnerId"/> means the user pressed
        /// the script's own trigger key. Any other Guid is a preset id.
        /// Mutated only under the coordinator's toggle lock.
        /// </summary>
        public HashSet<Guid> Owners { get; } = new();

        // Pixel verdict for the overlay. Written on the script's task
        // thread (RunPixelGatedAsync), read on the WPF UI thread by the
        // overlay. Int + Volatile keeps the cross-thread read cheap and
        // visible; the worst risk is a one-tick stale value, which is
        // harmless for a 200 ms-refreshed UI indicator.
        private int _pixelState;
        public PixelOverlayState PixelState
        {
            get => (PixelOverlayState)Volatile.Read(ref _pixelState);
            set => Volatile.Write(ref _pixelState, (int)value);
        }
    }
}
