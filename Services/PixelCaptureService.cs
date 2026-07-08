namespace BestInScript.API.Services
{
    /// <summary>Where the guided in-game capture currently stands.</summary>
    public enum CaptureStage
    {
        /// <summary>Not armed.</summary>
        Idle,
        /// <summary>Armed, waiting for the first hotkey press (over the READY icon).</summary>
        AwaitingReady,
        /// <summary>Ready color captured, waiting for the second press (icon ON cooldown).</summary>
        AwaitingCooldown,
        /// <summary>Both colors captured — results (and any nudge suggestion) are ready.</summary>
        Done
    }

    /// <summary>A recommended nearby coordinate where the two colors separate more than at the chosen pixel.</summary>
    public sealed record CaptureSuggestion(
        int Dx, int Dy,
        double SeparationAtBest, double SeparationAtCenter,
        int SuggestedX, int SuggestedY,
        int[] SuggestedReady, int[] SuggestedCool);

    /// <summary>Immutable snapshot of the capture state, polled by the web UI.</summary>
    public sealed record CaptureSnapshot(
        string Stage, string Hotkey,
        int X, int Y,
        int[]? ReadyColor, int[]? CoolColor,
        bool Unreadable, int Seq,
        CaptureSuggestion? Suggestion);

    /// <summary>
    /// Backs the guided in-game pixel capture (BACKLOG 2.1).
    ///
    /// While armed, a configured hotkey pressed <em>in the game</em> grabs the pixel
    /// color at the cursor — first press = ready color, second press = cooldown color —
    /// so the user never alt-tabs back to the browser to line up each sample. Each pass
    /// also samples a small neighborhood grid; when the ready/cooldown colors at the
    /// chosen pixel are too close to tell apart, it recommends the nearby coordinate
    /// where they separate most.
    ///
    /// FAIR PLAY: purely a passive setup convenience. It reuses the passive GDI reads
    /// (<see cref="IScreenSampler"/>) and the already-passive keyboard hook — no synthetic
    /// input, no gameplay capability, and no timing advantage.
    ///
    /// THREADING: <see cref="TryConsumeKey"/> runs on the WH_KEYBOARD_LL hook thread and
    /// must stay fast, so it only reads the cursor position and offloads the screen
    /// sampling to a <see cref="Task"/>. GDI reads in <see cref="PerformCapture"/> happen
    /// OUTSIDE the lock, so neither the hook thread nor the UI poll ever blocks on them.
    /// </summary>
    public sealed class PixelCaptureService
    {
        /// <summary>Neighborhood scanned around the chosen pixel: offsets [-GridRadius, +GridRadius].</summary>
        public const int GridRadius = 3;

        // Averaging radius per sampled cell — matches the default PixelTrigger.SampleRadius (3x3)
        // so captured colors are as stable as the live-evaluated pixel.
        private const int CellRadius = 1;

        private readonly IScreenSampler _screen;
        private readonly ILogger<PixelCaptureService> _logger;
        private readonly object _lock = new();

        private CaptureStage _stage = CaptureStage.Idle;
        private string _hotkey = "";
        private ushort _hotkeyVk;
        private int _x, _y;
        private int[]? _readyColor;
        private int[]? _coolColor;
        private int[]?[]? _readyGrid;   // row-major, length (2*GridRadius+1)^2; each cell = RGB or null
        private int[]?[]? _coolGrid;
        private bool _unreadable;
        private bool _capturing;        // a PerformCapture task is in flight for the current press
        private int _seq;               // bumped on every state change so the UI poll can detect updates

        public PixelCaptureService(IScreenSampler screen, ILogger<PixelCaptureService> logger)
        {
            _screen = screen;
            _logger = logger;
        }

        /// <summary>
        /// Arm capture on the given hotkey and reset any prior result.
        /// Returns null on success, or a human-readable error message.
        /// </summary>
        public string? Arm(string hotkey)
        {
            if (!InputSimulatorService.IsValidTriggerKey(hotkey))
                return "Capture hotkey must be a valid keyboard key (mouse buttons are not allowed).";

            lock (_lock)
            {
                _hotkey = hotkey.Trim().ToUpperInvariant();
                _hotkeyVk = InputSimulatorService.ResolveVk(_hotkey);
                _stage = CaptureStage.AwaitingReady;
                _x = _y = 0;
                _readyColor = _coolColor = null;
                _readyGrid = _coolGrid = null;
                _unreadable = false;
                _capturing = false;
                _seq++;
            }
            _logger.LogInformation("Pixel capture armed on '{Key}'", _hotkey);
            return null;
        }

        /// <summary>Cancel capture and return to idle.</summary>
        public void Disarm()
        {
            lock (_lock)
            {
                _stage = CaptureStage.Idle;
                _capturing = false;
                _seq++;
            }
        }

        /// <summary>
        /// Called on the hook thread for every key press. If capture is armed and waiting
        /// and the VK matches the capture hotkey, kicks off a sample and returns true — the
        /// press is <em>consumed</em> and must NOT toggle a script bound to the same key.
        /// Fast: no GDI beyond a cursor-position read; the sample runs on a background Task.
        /// </summary>
        public bool TryConsumeKey(ushort vk)
        {
            int x, y;
            lock (_lock)
            {
                if (vk == 0 || vk != _hotkeyVk) return false;
                if (_stage != CaptureStage.AwaitingReady && _stage != CaptureStage.AwaitingCooldown)
                    return false;
                if (_capturing) return true; // already sampling this press — swallow repeats

                if (_stage == CaptureStage.AwaitingReady)
                {
                    var pos = _screen.CursorPosition();
                    if (pos is null)
                    {
                        _unreadable = true;
                        _seq++;
                        return true; // consumed; the user re-presses to retry
                    }
                    x = pos.Value.X;
                    y = pos.Value.Y;
                }
                else
                {
                    // Cooldown pass reuses the coordinate chosen in the ready pass,
                    // so the user need not hold the mouse perfectly still between passes.
                    x = _x;
                    y = _y;
                }
                _capturing = true;
            }

            _ = Task.Run(() => PerformCapture(x, y));
            return true;
        }

        /// <summary>
        /// Samples the point + neighborhood grid at (x,y) and folds the result into the
        /// current stage. Runs on a background Task in production (GDI is done outside the
        /// lock); public and synchronous so unit tests can drive it deterministically.
        /// </summary>
        public void PerformCapture(int x, int y)
        {
            var grid = SampleGrid(x, y);
            int span = 2 * GridRadius + 1;
            var center = grid[GridRadius * span + GridRadius];

            lock (_lock)
            {
                try
                {
                    if (_stage != CaptureStage.AwaitingReady && _stage != CaptureStage.AwaitingCooldown)
                        return;

                    if (center is null)
                    {
                        // Nothing readable at the chosen pixel — keep the stage so the user re-presses.
                        _unreadable = true;
                        _seq++;
                        return;
                    }
                    _unreadable = false;

                    if (_stage == CaptureStage.AwaitingReady)
                    {
                        _x = x;
                        _y = y;
                        _readyColor = center;
                        _readyGrid = grid;
                        _stage = CaptureStage.AwaitingCooldown;
                    }
                    else
                    {
                        _coolColor = center;
                        _coolGrid = grid;
                        _stage = CaptureStage.Done;
                    }
                    _seq++;
                }
                finally
                {
                    _capturing = false;
                }
            }
        }

        /// <summary>Current state snapshot for the UI poll.</summary>
        public CaptureSnapshot GetState()
        {
            lock (_lock)
            {
                var suggestion = _stage == CaptureStage.Done ? ComputeSuggestion() : null;
                return new CaptureSnapshot(
                    _stage.ToString(), _hotkey, _x, _y,
                    _readyColor, _coolColor, _unreadable, _seq, suggestion);
            }
        }

        // Samples a (2*GridRadius+1)^2 grid around (x,y), row-major. Called outside the lock.
        private int[]?[] SampleGrid(int x, int y)
        {
            int span = 2 * GridRadius + 1;
            var grid = new int[span * span][];
            for (int gy = 0; gy < span; gy++)
            {
                for (int gx = 0; gx < span; gx++)
                {
                    var c = _screen.ColorAtAveraged(x + gx - GridRadius, y + gy - GridRadius, CellRadius);
                    grid[gy * span + gx] = c is null ? null : new[] { c.Value.R, c.Value.G, c.Value.B };
                }
            }
            return grid;
        }

        // Finds the grid offset where ready/cooldown separate most. Caller MUST hold _lock.
        private CaptureSuggestion? ComputeSuggestion()
        {
            if (_readyGrid is null || _coolGrid is null) return null;

            int span = 2 * GridRadius + 1;
            int centerIdx = GridRadius * span + GridRadius;
            var rcCenter = _readyGrid[centerIdx];
            var ccCenter = _coolGrid[centerIdx];
            double centerSep = (rcCenter is null || ccCenter is null)
                ? 0
                : ScreenColorService.Distance(rcCenter, ccCenter);

            double bestSep = centerSep;
            int bestDx = 0, bestDy = 0;
            for (int gy = 0; gy < span; gy++)
            {
                for (int gx = 0; gx < span; gx++)
                {
                    var r = _readyGrid[gy * span + gx];
                    var c = _coolGrid[gy * span + gx];
                    if (r is null || c is null) continue;
                    double sep = ScreenColorService.Distance(r, c);
                    if (sep > bestSep)
                    {
                        bestSep = sep;
                        bestDx = gx - GridRadius;
                        bestDy = gy - GridRadius;
                    }
                }
            }

            int bestIdx = (bestDy + GridRadius) * span + (bestDx + GridRadius);
            var br = _readyGrid[bestIdx];
            var bc = _coolGrid[bestIdx];
            if (br is null || bc is null) return null;

            return new CaptureSuggestion(
                bestDx, bestDy, bestSep, centerSep,
                _x + bestDx, _y + bestDy, br, bc);
        }
    }
}
