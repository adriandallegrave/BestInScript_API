using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BestInScript.API.Models;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Manages a global low-level keyboard hook (WH_KEYBOARD_LL) on a dedicated
    /// background thread. Each registered script and each registered preset
    /// owns a trigger key; pressing the key toggles its claim.
    ///
    /// The trigger key is NOT suppressed – the game still receives it.
    ///
    /// EXECUTION MODES
    /// ---------------
    /// • Blind loop (default): the step sequence repeats continuously while the
    ///   script is toggled on.
    /// • Pixel-gated: if the script has a <see cref="PixelTrigger"/>, the engine
    ///   watches one screen pixel and runs the step sequence once each time that
    ///   pixel matches the "ready" color — e.g. autocasting a cooldown skill.
    ///
    /// OWNERSHIP / PRESETS
    /// -------------------
    /// A script runs iff at least one "owner" has it claimed. Owners are:
    ///   • <see cref="UserOwnerId"/> — added when the user presses the script's
    ///     own trigger key directly, removed on the next press.
    ///   • Any active preset's <c>Preset.Id</c> — added for every member when a
    ///     preset is toggled on, removed when it is toggled off.
    /// A script starts when its owner set goes from empty to non-empty and stops
    /// when it goes from non-empty to empty. Two presets sharing a script both
    /// own it, so turning one off does not stop the script while the other is
    /// still on.
    /// </summary>
    public class HotkeyEngine : IHostedService, IDisposable
    {
        // ── Win32 ──────────────────────────────────────────────────────────────
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const uint WM_QUIT = 0x0012;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public nint dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public nint hwnd;
            public uint message;
            public nuint wParam;
            public nint lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, nuint wParam, nint lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        // ── State ──────────────────────────────────────────────────────────────
        private readonly ILogger<HotkeyEngine> _logger;
        private readonly ScriptRepository _repo;
        private readonly PresetRepository _presetRepo;
        private readonly InputSimulatorService _inputSim;
        private readonly ScreenColorService _screen;

        /// <summary>Sentinel owner id meaning "the user pressed this script's own trigger key directly".</summary>
        public static readonly Guid UserOwnerId = Guid.Empty;

        // vkCode → script entry
        private readonly ConcurrentDictionary<ushort, ScriptEntry> _scriptRegistry = new();
        // vkCode → preset entry
        private readonly ConcurrentDictionary<ushort, PresetEntry> _presetRegistry = new();

        private Thread? _hookThread;
        private uint _hookThreadId;
        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc? _hookProc; // must keep reference to prevent GC

        // Guards all owner-set mutations + start/stop transitions.
        private readonly object _toggleLock = new();

        // ── Construction ───────────────────────────────────────────────────────
        public HotkeyEngine(
            ILogger<HotkeyEngine> logger,
            ScriptRepository repo,
            PresetRepository presetRepo,
            InputSimulatorService inputSim,
            ScreenColorService screen)
        {
            _logger = logger;
            _repo = repo;
            _presetRepo = presetRepo;
            _inputSim = inputSim;
            _screen = screen;
        }

        // ── IHostedService ─────────────────────────────────────────────────────
        public Task StartAsync(CancellationToken cancellationToken)
        {
            LoadFromRepository();

            _hookThread = new Thread(RunHookThread)
            {
                IsBackground = true,
                Name = "BIS_HookThread"
            };
            _hookThread.Start();

            _logger.LogInformation(
                "HotkeyEngine started. {Scripts} script(s), {Presets} preset(s) registered.",
                _scriptRegistry.Count, _presetRegistry.Count);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("HotkeyEngine stopping…");

            // Cancel all running scripts and release any held keys
            foreach (var entry in _scriptRegistry.Values)
                StopEntry(entry);

            // Signal the hook thread's message loop to exit
            if (_hookThreadId != 0)
                PostThreadMessage(_hookThreadId, WM_QUIT, 0, 0);

            _hookThread?.Join(TimeSpan.FromSeconds(3));
            return Task.CompletedTask;
        }

        public void Dispose() { /* resources freed in StopAsync / hook thread */ }

        // ── Public API: scripts ────────────────────────────────────────────────

        /// <summary>Add or replace a script. Stops any currently running version first.</summary>
        public void RegisterScript(ScriptConfig config)
        {
            var vk = InputSimulatorService.ResolveVk(config.TriggerKey);
            if (vk == 0)
            {
                _logger.LogWarning("RegisterScript: cannot resolve trigger key '{Key}'", config.TriggerKey);
                return;
            }

            lock (_toggleLock)
            {
                // Remove any existing entry mapped to this id (may be on a different VK if trigger changed).
                // We drop its run state entirely — preset ownership for this script id is re-applied below
                // so an active preset that owns this script keeps it running across edits.
                var old = _scriptRegistry.Values.FirstOrDefault(e => e.Config.Id == config.Id);
                if (old != null)
                {
                    StopEntry(old);
                    old.Owners.Clear();
                    _scriptRegistry.TryRemove(InputSimulatorService.ResolveVk(old.Config.TriggerKey), out _);
                }

                if (_presetRegistry.ContainsKey(vk))
                    _logger.LogWarning(
                        "RegisterScript: trigger key '{Key}' (VK=0x{VK:X2}) is also bound to a preset. " +
                        "The script will take precedence in the hook callback.",
                        config.TriggerKey, vk);

                var entry = new ScriptEntry(config);

                // Re-apply ownership from any currently-active preset that lists this script.
                foreach (var preset in _presetRegistry.Values)
                {
                    if (preset.IsActive && preset.Config.ScriptIds.Contains(config.Id))
                        entry.Owners.Add(preset.Config.Id);
                }

                _scriptRegistry[vk] = entry;
                EnsureRunState(entry);

                _logger.LogInformation(
                    "Registered script '{Name}' on key '{Key}' (VK=0x{VK:X2})",
                    config.Name, config.TriggerKey, vk);
            }
        }

        /// <summary>Remove a script, stopping it first if running.</summary>
        public void UnregisterScript(Guid id)
        {
            lock (_toggleLock)
            {
                var entry = _scriptRegistry.Values.FirstOrDefault(e => e.Config.Id == id);
                if (entry == null) return;

                StopEntry(entry);
                entry.Owners.Clear();
                _scriptRegistry.TryRemove(InputSimulatorService.ResolveVk(entry.Config.TriggerKey), out _);
                _logger.LogInformation("Unregistered script '{Name}'", entry.Config.Name);
            }
        }

        // ── Public API: presets ────────────────────────────────────────────────

        /// <summary>Add or replace a preset. If a previous version was active, it is deactivated first
        /// (releasing its claim on member scripts) so the new config starts inactive.</summary>
        public void RegisterPreset(Preset preset)
        {
            var vk = InputSimulatorService.ResolveVk(preset.TriggerKey);
            if (vk == 0)
            {
                _logger.LogWarning("RegisterPreset: cannot resolve trigger key '{Key}'", preset.TriggerKey);
                return;
            }

            lock (_toggleLock)
            {
                var old = _presetRegistry.Values.FirstOrDefault(e => e.Config.Id == preset.Id);
                if (old != null)
                {
                    if (old.IsActive)
                        DeactivatePresetLocked(old);
                    _presetRegistry.TryRemove(InputSimulatorService.ResolveVk(old.Config.TriggerKey), out _);
                }

                if (_scriptRegistry.ContainsKey(vk))
                    _logger.LogWarning(
                        "RegisterPreset: trigger key '{Key}' (VK=0x{VK:X2}) is also bound to a script. " +
                        "The script will take precedence in the hook callback.",
                        preset.TriggerKey, vk);

                _presetRegistry[vk] = new PresetEntry(preset);

                _logger.LogInformation(
                    "Registered preset '{Name}' on key '{Key}' (VK=0x{VK:X2}) with {Count} member(s)",
                    preset.Name, preset.TriggerKey, vk, preset.ScriptIds.Count);
            }
        }

        /// <summary>Remove a preset, deactivating it first if active.</summary>
        public void UnregisterPreset(Guid id)
        {
            lock (_toggleLock)
            {
                var entry = _presetRegistry.Values.FirstOrDefault(e => e.Config.Id == id);
                if (entry == null) return;

                if (entry.IsActive)
                    DeactivatePresetLocked(entry);
                _presetRegistry.TryRemove(InputSimulatorService.ResolveVk(entry.Config.TriggerKey), out _);
                _logger.LogInformation("Unregistered preset '{Name}'", entry.Config.Name);
            }
        }

        // ── Public API: status ─────────────────────────────────────────────────

        /// <summary>Returns a snapshot of runtime status for all registered scripts.</summary>
        public IEnumerable<ScriptStatus> GetStatus()
        {
            lock (_toggleLock)
            {
                return _scriptRegistry.Values.Select(e => new ScriptStatus
                {
                    Id = e.Config.Id,
                    Name = e.Config.Name,
                    TriggerKey = e.Config.TriggerKey,
                    IsRunning = e.Owners.Count > 0,
                    ShowInOverlay = e.Config.ShowInOverlay,
                    HasPixelTrigger = e.Config.PixelTrigger != null,
                    PixelState = e.PixelState
                }).ToList();
            }
        }

        /// <summary>Returns a snapshot of runtime status for all registered presets.</summary>
        public IEnumerable<PresetStatus> GetPresetStatus()
        {
            lock (_toggleLock)
            {
                return _presetRegistry.Values.Select(e => new PresetStatus
                {
                    Id = e.Config.Id,
                    Name = e.Config.Name,
                    TriggerKey = e.Config.TriggerKey,
                    IsActive = e.IsActive,
                    ShowInOverlay = e.Config.ShowInOverlay,
                    ScriptIds = new List<Guid>(e.Config.ScriptIds)
                }).ToList();
            }
        }

        /// <summary>Stops all running scripts immediately. Deactivates all presets.</summary>
        public void StopAll()
        {
            lock (_toggleLock)
            {
                foreach (var preset in _presetRegistry.Values)
                    if (preset.IsActive) preset.IsActive = false;

                foreach (var entry in _scriptRegistry.Values)
                {
                    entry.Owners.Clear();
                    StopEntry(entry);
                }
            }
        }

        // ── Hook thread ────────────────────────────────────────────────────────
        private void RunHookThread()
        {
            _hookThreadId = GetCurrentThreadId();
            _hookProc = HookCallback; // keep GC-reachable reference on stack/field

            using var proc = Process.GetCurrentProcess();
            using var module = proc.MainModule!;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                GetModuleHandle(module.ModuleName), 0);

            if (_hookHandle == IntPtr.Zero)
            {
                _logger.LogError("SetWindowsHookEx failed. LastError={0}", Marshal.GetLastWin32Error());
                return;
            }

            _logger.LogInformation("Global keyboard hook installed.");

            // Windows message pump – required for WH_KEYBOARD_LL delivery
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _logger.LogInformation("Global keyboard hook removed.");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                var kbs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vkCode = (ushort)kbs.vkCode;
                DispatchKey(vkCode);
            }

            // Always call next hook – key passes through to the game.
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        /// <summary>
        /// Routes a key press to whichever script or preset claims that VK.
        /// Scripts take precedence if both exist on the same VK — but validation
        /// at the API layer prevents that case in normal use.
        /// </summary>
        private void DispatchKey(ushort vkCode)
        {
            if (_scriptRegistry.TryGetValue(vkCode, out var script))
            {
                ToggleUserOwnership(script);
                return;
            }
            if (_presetRegistry.TryGetValue(vkCode, out var preset))
            {
                TogglePreset(preset);
            }
        }

        // ── Toggle logic ───────────────────────────────────────────────────────

        /// <summary>Flip the user's direct claim on a script (the legacy "press to toggle" behavior).</summary>
        private void ToggleUserOwnership(ScriptEntry entry)
        {
            lock (_toggleLock)
            {
                if (entry.Owners.Contains(UserOwnerId))
                {
                    entry.Owners.Remove(UserOwnerId);
                    _logger.LogInformation(
                        "User released script '{Name}' (remaining owners: {Owners})",
                        entry.Config.Name, entry.Owners.Count);
                }
                else
                {
                    entry.Owners.Add(UserOwnerId);
                    _logger.LogInformation(
                        "User claimed script '{Name}' (total owners: {Owners})",
                        entry.Config.Name, entry.Owners.Count);
                }
                EnsureRunState(entry);
            }
        }

        /// <summary>Toggle a preset on/off, propagating ownership to every member script.</summary>
        private void TogglePreset(PresetEntry preset)
        {
            lock (_toggleLock)
            {
                if (preset.IsActive)
                {
                    DeactivatePresetLocked(preset);
                    _logger.LogInformation("Deactivated preset '{Name}'", preset.Config.Name);
                }
                else
                {
                    ActivatePresetLocked(preset);
                    _logger.LogInformation(
                        "Activated preset '{Name}' ({Count} member(s))",
                        preset.Config.Name, preset.Config.ScriptIds.Count);
                }
            }
        }

        // Caller MUST hold _toggleLock.
        private void ActivatePresetLocked(PresetEntry preset)
        {
            preset.IsActive = true;
            foreach (var scriptId in preset.Config.ScriptIds)
            {
                var entry = _scriptRegistry.Values.FirstOrDefault(e => e.Config.Id == scriptId);
                if (entry == null)
                {
                    _logger.LogDebug(
                        "Preset '{Preset}': member script {Id} not registered — skipping",
                        preset.Config.Name, scriptId);
                    continue;
                }
                entry.Owners.Add(preset.Config.Id);
                EnsureRunState(entry);
            }
        }

        // Caller MUST hold _toggleLock.
        private void DeactivatePresetLocked(PresetEntry preset)
        {
            preset.IsActive = false;
            foreach (var scriptId in preset.Config.ScriptIds)
            {
                var entry = _scriptRegistry.Values.FirstOrDefault(e => e.Config.Id == scriptId);
                if (entry == null) continue;
                entry.Owners.Remove(preset.Config.Id);
                EnsureRunState(entry);
            }
        }

        /// <summary>
        /// Reconcile a script's run state against its owner set:
        /// owners non-empty + not running → start; owners empty + running → stop.
        /// Caller MUST hold _toggleLock.
        /// </summary>
        private void EnsureRunState(ScriptEntry entry)
        {
            bool shouldRun = entry.Owners.Count > 0;
            bool isRunning = entry.Cts != null && !entry.Cts.IsCancellationRequested;

            if (shouldRun && !isRunning)
            {
                _logger.LogInformation("Starting script '{Name}'", entry.Config.Name);
                var cts = new CancellationTokenSource();
                entry.Cts = cts;
                _ = RunScriptAsync(entry, cts.Token);
            }
            else if (!shouldRun && isRunning)
            {
                _logger.LogInformation("Stopping script '{Name}'", entry.Config.Name);
                StopEntry(entry);
            }
        }

        // ── Script execution ───────────────────────────────────────────────────

        /// <summary>
        /// Entry point for a toggled-on script. Dispatches to the blind loop or
        /// the pixel-gated loop depending on whether a PixelTrigger is set.
        /// </summary>
        private Task RunScriptAsync(ScriptEntry entry, CancellationToken ct)
            => entry.Config.PixelTrigger is null
                ? RunBlindLoopAsync(entry, ct)
                : RunPixelGatedAsync(entry, ct);

        /// <summary>
        /// Default mode: loop the step sequence continuously until cancelled.
        /// Held keys persist across steps AND loop iterations — a key listed in
        /// consecutive steps stays physically down instead of fluttering.
        /// </summary>
        private async Task RunBlindLoopAsync(ScriptEntry entry, CancellationToken ct)
        {
            var rng = Random.Shared;
            var config = entry.Config;
            var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                while (!ct.IsCancellationRequested)
                    await RunStepsOnceAsync(config, heldKeys, rng, ct);
            }
            catch (OperationCanceledException)
            {
                // Normal stop path – swallow
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in script '{Name}'", config.Name);
            }
            finally
            {
                ReleaseHeld(heldKeys);
                entry.Cts = null;
            }
        }

        /// <summary>
        /// Pixel-gated mode: watch one screen pixel and run the step sequence
        /// when the pixel reads as "ready". Behavior depends on
        /// <see cref="PixelTrigger.RequireReset"/>:
        ///
        /// • false (continuous autocast) — fires every time a "ready" sample
        ///   is observed, gated only by the re-arm delay. If the pixel stays
        ///   ready, it keeps firing.
        ///
        /// • true (one-shot per cycle) — fires once when the pixel becomes
        ///   ready, then disarms until at least one non-ready sample resets
        ///   it. The pixel must return to a non-ready state (typically the
        ///   cooldown color) before another fire can happen.
        /// </summary>
        private async Task RunPixelGatedAsync(ScriptEntry entry, CancellationToken ct)
        {
            var rng = Random.Shared;
            var config = entry.Config;
            var pt = config.PixelTrigger!;
            var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Clamp config to sane floors so a bad value can't peg the CPU.
            int pollMs = Math.Max(15, pt.PollIntervalMs);
            int reArmMs = Math.Max(0, pt.ReArmDelayMs);
            int radius = Math.Clamp(pt.SampleRadius, 0, 8);
            bool warnedUnreadable = false;

            // One-shot state machine. When RequireReset is false this stays
            // true forever and the loop behaves like pure autocast. When true,
            // it goes false after each fire and only comes back true once we
            // observe the pixel in a non-ready state — i.e. the user's
            // "reset to initial state" requirement.
            bool armed = true;

            // Initialize the overlay state to "waiting" so the user immediately
            // sees the script as watching, rather than NotApplicable.
            entry.PixelState = PixelOverlayState.Waiting;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var color = _screen.ColorAtAveraged(pt.X, pt.Y, radius);

                    if (color is null)
                    {
                        entry.PixelState = PixelOverlayState.Unreadable;

                        // Screen unreadable (e.g. fullscreen-exclusive game).
                        // Warn once, then back off harder than the poll interval
                        // so we don't spin on a black screen.
                        if (!warnedUnreadable)
                        {
                            _logger.LogWarning(
                                "PixelTrigger '{Name}': cannot read screen at ({X},{Y}). " +
                                "Is the game in fullscreen-exclusive mode? Switch to borderless.",
                                config.Name, pt.X, pt.Y);
                            warnedUnreadable = true;
                        }
                        await Task.Delay(Math.Max(pollMs, 250), ct);
                        continue;
                    }
                    warnedUnreadable = false;

                    var c = color.Value;
                    var live = new[] { c.R, c.G, c.B };
                    double dReady = ScreenColorService.Distance(live, pt.ReadyColor);
                    double dCool = ScreenColorService.Distance(live, pt.CooldownColor);

                    // "Ready" means: close enough to the ready color in absolute
                    // terms, AND closer to ready than to cooldown. The second
                    // test is what makes a two-color trigger robust to the
                    // lighting/animation drift a single threshold can't handle.
                    bool ready = dReady <= pt.Tolerance && dReady < dCool;

                    if (ready && armed)
                    {
                        entry.PixelState = PixelOverlayState.Ready;
                        await RunStepsOnceAsync(config, heldKeys, rng, ct);
                        ReleaseHeld(heldKeys);          // each cast is a discrete burst
                        if (pt.RequireReset) armed = false;
                        if (reArmMs > 0)
                            await Task.Delay(reArmMs, ct);
                    }
                    else
                    {
                        // Any non-ready observation re-arms the trigger. (When
                        // RequireReset is false, armed is already true and this
                        // is a no-op — so the autocast path is unchanged.)
                        entry.PixelState = PixelOverlayState.Waiting;
                        if (!ready) armed = true;
                        await Task.Delay(pollMs, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal stop path – swallow
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in pixel-gated script '{Name}'", config.Name);
            }
            finally
            {
                ReleaseHeld(heldKeys);
                entry.Cts = null;
            }
        }

        /// <summary>
        /// Runs every step of a script exactly once. <paramref name="heldKeys"/>
        /// tracks which keys are physically down so a key common to consecutive
        /// steps is not released and re-pressed — it just stays held.
        /// </summary>
        private async Task RunStepsOnceAsync(
            ScriptConfig config, HashSet<string> heldKeys, Random rng, CancellationToken ct)
        {
            foreach (var step in config.Steps)
            {
                if (ct.IsCancellationRequested) break;

                // 1. Release only keys held previously that this step no longer needs
                foreach (var key in heldKeys
                             .Where(k => !step.Hold.Contains(k, StringComparer.OrdinalIgnoreCase))
                             .ToList())
                {
                    _inputSim.KeyUp(key);
                    heldKeys.Remove(key);
                }

                // 2. Press hold keys not already down; keys still held just stay down
                foreach (var key in step.Hold)
                {
                    if (heldKeys.Add(key))
                        _inputSim.KeyDown(key);
                }

                // 3. Tap each press key
                foreach (var key in step.Press)
                    _inputSim.KeyPress(key);

                // 4. Random delay in [DelayMin, DelayMax]
                var delay = config.DelayMin
                    + (config.DelayMax - config.DelayMin) * rng.NextDouble();
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
        }

        /// <summary>Releases every key in the set and clears it.</summary>
        private void ReleaseHeld(HashSet<string> heldKeys)
        {
            foreach (var key in heldKeys)
                _inputSim.KeyUp(key);
            heldKeys.Clear();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static void StopEntry(ScriptEntry entry)
        {
            var cts = entry.Cts;
            if (cts == null) return;

            try { cts.Cancel(); }
            catch { /* already cancelled */ }
            entry.Cts = null;
        }

        private void LoadFromRepository()
        {
            // Order matters: load scripts first so presets can find their members.
            foreach (var s in _repo.GetAll())
                RegisterScript(s);

            foreach (var p in _presetRepo.GetAll())
                RegisterPreset(p);
        }

        // ── Nested types ───────────────────────────────────────────────────────
        private sealed class ScriptEntry(ScriptConfig config)
        {
            public ScriptConfig Config { get; } = config;
            public CancellationTokenSource? Cts { get; set; }

            /// <summary>
            /// Set of owners that have claimed this script. Sentinel
            /// <see cref="UserOwnerId"/> means the user pressed the script's
            /// own trigger key. Any other Guid is a preset id. Mutated only
            /// under <see cref="_toggleLock"/>.
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

        private sealed class PresetEntry(Preset config)
        {
            public Preset Config { get; } = config;
            /// <summary>True while this preset's trigger has been pressed an odd number of times.</summary>
            public bool IsActive { get; set; }
        }
    }

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
