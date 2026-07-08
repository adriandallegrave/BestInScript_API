using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BestInScript.API.Engine
{
    /// <summary>
    /// Owns the global low-level keyboard hook (WH_KEYBOARD_LL) and its
    /// dedicated background thread with a Windows message pump.
    ///
    /// Raises <see cref="KeyPressed"/> synchronously from the hook callback
    /// for every key-down. The key is NEVER suppressed — the callback always
    /// returns CallNextHookEx so the game receives the key normally.
    /// </summary>
    public sealed class KeyboardHook : IDisposable
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
        private readonly ILogger<KeyboardHook> _logger;

        private Thread? _hookThread;
        private uint _hookThreadId;
        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc? _hookProc; // must keep reference to prevent GC

        public KeyboardHook(ILogger<KeyboardHook> logger) => _logger = logger;

        /// <summary>Raised synchronously on the hook thread for every key-down (VK code).</summary>
        public event Action<ushort>? KeyPressed;

        /// <summary>Installs the hook on a dedicated background thread.</summary>
        public void Start()
        {
            _hookThread = new Thread(RunHookThread)
            {
                IsBackground = true,
                Name = "BIS_HookThread"
            };
            _hookThread.Start();
        }

        /// <summary>Signals the hook thread's message loop to exit and waits for it.</summary>
        public void Stop()
        {
            if (_hookThreadId != 0)
                PostThreadMessage(_hookThreadId, WM_QUIT, 0, 0);

            _hookThread?.Join(TimeSpan.FromSeconds(3));
        }

        public void Dispose() => Stop();

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
                KeyPressed?.Invoke(vkCode);
            }

            // Always call next hook – key passes through to the game.
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }
}
