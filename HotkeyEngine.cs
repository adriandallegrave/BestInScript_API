using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BestInScript.API.Models;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Manages a global low-level keyboard hook (WH_KEYBOARD_LL) on a dedicated
    /// background thread. When a trigger key is pressed the associated script is
    /// toggled on/off; scripts run indefinitely in their own Task until cancelled.
    ///
    /// The trigger key is NOT suppressed – the game still receives it.
    /// </summary>
    public class HotkeyEngine : IHostedService, IDisposable
    {
        // ── Win32 ──────────────────────────────────────────────────────────────

        private const int  WH_KEYBOARD_LL  = 13;
        private const int  WM_KEYDOWN      = 0x0100;
        private const int  WM_SYSKEYDOWN   = 0x0104;
        private const uint WM_QUIT         = 0x0012;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint  vkCode;
            public uint  scanCode;
            public uint  flags;
            public uint  time;
            public nint  dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public nint  hwnd;
            public uint  message;
            public nuint wParam;
            public nint  lParam;
            public uint  time;
            public int   ptX;
            public int   ptY;
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
        private readonly ScriptRepository       _repo;
        private readonly InputSimulatorService  _inputSim;

        // vkCode → script entry
        private readonly ConcurrentDictionary<ushort, ScriptEntry> _registry = new();

        private Thread?               _hookThread;
        private uint                  _hookThreadId;
        private IntPtr                _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc? _hookProc;   // must keep reference to prevent GC

        private readonly object _toggleLock = new();

        // ── Construction ───────────────────────────────────────────────────────

        public HotkeyEngine(
            ILogger<HotkeyEngine> logger,
            ScriptRepository repo,
            InputSimulatorService inputSim)
        {
            _logger   = logger;
            _repo     = repo;
            _inputSim = inputSim;
        }

        // ── IHostedService ─────────────────────────────────────────────────────

        public Task StartAsync(CancellationToken cancellationToken)
        {
            LoadFromRepository();

            _hookThread = new Thread(RunHookThread)
            {
                IsBackground = true,
                Name         = "BIS_HookThread"
            };
            _hookThread.Start();

            _logger.LogInformation("HotkeyEngine started. {Count} script(s) registered.", _registry.Count);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("HotkeyEngine stopping…");

            // Cancel all running scripts and release any held keys
            foreach (var entry in _registry.Values)
                StopEntry(entry);

            // Signal the hook thread's message loop to exit
            if (_hookThreadId != 0)
                PostThreadMessage(_hookThreadId, WM_QUIT, 0, 0);

            _hookThread?.Join(TimeSpan.FromSeconds(3));
            return Task.CompletedTask;
        }

        public void Dispose() { /* resources freed in StopAsync / hook thread */ }

        // ── Public API (called by controllers) ─────────────────────────────────

        /// <summary>Add or replace a script. Stops any currently running version first.</summary>
        public void RegisterScript(ScriptConfig config)
        {
            var vk = InputSimulatorService.ResolveVk(config.TriggerKey);
            if (vk == 0)
            {
                _logger.LogWarning("RegisterScript: cannot resolve trigger key '{Key}'", config.TriggerKey);
                return;
            }

            // Remove any existing entry mapped to this id (may be on a different VK if trigger changed)
            var old = _registry.Values.FirstOrDefault(e => e.Config.Id == config.Id);
            if (old != null)
            {
                StopEntry(old);
                _registry.TryRemove(
                    InputSimulatorService.ResolveVk(old.Config.TriggerKey), out _);
            }

            var entry = new ScriptEntry(config);
            _registry[vk] = entry;
            _logger.LogInformation("Registered script '{Name}' on key '{Key}' (VK=0x{VK:X2})",
                config.Name, config.TriggerKey, vk);
        }

        /// <summary>Remove a script, stopping it first if running.</summary>
        public void UnregisterScript(Guid id)
        {
            var entry = _registry.Values.FirstOrDefault(e => e.Config.Id == id);
            if (entry == null) return;

            StopEntry(entry);
            _registry.TryRemove(InputSimulatorService.ResolveVk(entry.Config.TriggerKey), out _);
            _logger.LogInformation("Unregistered script '{Name}'", entry.Config.Name);
        }

        /// <summary>Returns a snapshot of runtime status for all registered scripts.</summary>
        public IEnumerable<ScriptStatus> GetStatus()
            => _registry.Values.Select(e => new ScriptStatus
            {
                Id         = e.Config.Id,
                Name       = e.Config.Name,
                TriggerKey = e.Config.TriggerKey,
                IsRunning  = e.IsRunning
            });

        /// <summary>Stops all running scripts immediately.</summary>
        public void StopAll()
        {
            foreach (var entry in _registry.Values)
                StopEntry(entry);
        }

        // ── Hook thread ────────────────────────────────────────────────────────

        private void RunHookThread()
        {
            _hookThreadId = GetCurrentThreadId();
            _hookProc     = HookCallback;   // keep GC-reachable reference on stack/field

            using var proc   = Process.GetCurrentProcess();
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
                var kbs   = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vkCode = (ushort)kbs.vkCode;
                ToggleScript(vkCode);
            }
            // Always call next hook – key passes through to the game.
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // ── Toggle logic ───────────────────────────────────────────────────────

        private void ToggleScript(ushort vkCode)
        {
            if (!_registry.TryGetValue(vkCode, out var entry)) return;

            lock (_toggleLock)
            {
                if (entry.IsRunning)
                {
                    _logger.LogInformation("Stopping script '{Name}'", entry.Config.Name);
                    StopEntry(entry);
                }
                else
                {
                    _logger.LogInformation("Starting script '{Name}'", entry.Config.Name);
                    var cts = new CancellationTokenSource();
                    entry.Cts = cts;
                    _ = RunScriptAsync(entry, cts.Token);
                }
            }
        }

        // ── Script execution loop ──────────────────────────────────────────────

        private async Task RunScriptAsync(ScriptEntry entry, CancellationToken ct)
        {
            var rng    = Random.Shared;
            var config = entry.Config;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    foreach (var step in config.Steps)
                    {
                        if (ct.IsCancellationRequested) break;

                        // 1. Hold keys down
                        foreach (var key in step.Hold)
                            _inputSim.KeyDown(key);

                        // 2. Tap each press key
                        foreach (var key in step.Press)
                            _inputSim.KeyPress(key);

                        // 3. Release held keys
                        foreach (var key in step.Hold)
                            _inputSim.KeyUp(key);

                        // 4. Random delay in [DelayMin, DelayMax]
                        var delay = config.DelayMin
                            + (config.DelayMax - config.DelayMin) * rng.NextDouble();

                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                    }
                }
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
                // Safety: release all keys the script may have held
                foreach (var step in config.Steps)
                    foreach (var key in step.Hold)
                        _inputSim.KeyUp(key);

                entry.Cts = null;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void StopEntry(ScriptEntry entry)
        {
            var cts = entry.Cts;
            if (cts == null) return;
            try   { cts.Cancel(); }
            catch { /* already cancelled */ }
            entry.Cts = null;
        }

        private void LoadFromRepository()
        {
            var scripts = _repo.GetAll();
            foreach (var s in scripts)
                RegisterScript(s);
        }

        // ── Nested types ───────────────────────────────────────────────────────

        private sealed class ScriptEntry(ScriptConfig config)
        {
            public ScriptConfig              Config    { get; }     = config;
            public CancellationTokenSource?  Cts       { get; set; }
            public bool                      IsRunning => Cts != null && !Cts.IsCancellationRequested;
        }
    }

    public class ScriptStatus
    {
        public Guid   Id         { get; set; }
        public string Name       { get; set; } = "";
        public string TriggerKey { get; set; } = "";
        public bool   IsRunning  { get; set; }
    }
}
