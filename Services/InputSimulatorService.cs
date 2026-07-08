using System.Runtime.InteropServices;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Simulates keyboard and mouse input via Win32 SendInput.
    ///
    /// ANTI-CHEAT SAFETY
    /// -----------------
    /// All keyboard events are sent with KEYEVENTF_SCANCODE, not KEYEVENTF_KEYDOWN.
    /// The scan code is derived from the virtual key via MapVirtualKey(MAPVK_VK_TO_VSC).
    /// This makes every event follow the exact same path a physical USB/PS2 keyboard
    /// uses: scancode -> HID driver -> raw input -> WM_KEYDOWN. Anti-cheats that
    /// compare the scan code field against the virtual key field (a common heuristic
    /// for injected input) will see a legitimate pairing.
    ///
    /// Mouse button events use MOUSEEVENTF_* flags with no absolute positioning,
    /// identical to what a physical mouse produces.
    ///
    /// Valid key names (case-insensitive):
    ///   Letters  : A-Z
    ///   Numbers  : 0-9 (top row)
    ///   F-keys   : F1-F12
    ///   Modifiers: Shift/LShift/RShift, Ctrl/LCtrl/RCtrl, Alt/LAlt/RAlt
    ///   Special  : Space, Enter, Tab, Escape, Backspace, Delete, Insert,
    ///              Up, Down, Left, Right, Home, End, PageUp, PageDown
    ///   Numpad   : NumPad0-NumPad9, Multiply, Add, Subtract, Decimal, Divide
    ///   Mouse    : Mouse1 (left), Mouse2 (right), Mouse3 (middle),
    ///              Mouse4 (XButton1), Mouse5 (XButton2)
    /// </summary>
    public class InputSimulatorService : IInputSimulator
    {
        // Win32 constants
        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_XDOWN = 0x0080;
        private const uint MOUSEEVENTF_XUP = 0x0100;

        private const uint XBUTTON1 = 0x0001;
        private const uint XBUTTON2 = 0x0002;

        private const uint MAPVK_VK_TO_VSC = 0;

        // On 64-bit Windows the INPUT union starts at offset 8 (4-byte type + 4-byte pad)
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
            [FieldOffset(8)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public nint dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public nint dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // Public API

        public void KeyPress(string key) { KeyDown(key); KeyUp(key); }

        public void KeyDown(string key)
        {
            if (IsMouseButton(key)) SendMouseEvent(key, true);
            else SendKeyEvent(ResolveVk(key), true);
        }

        public void KeyUp(string key)
        {
            if (IsMouseButton(key)) SendMouseEvent(key, false);
            else SendKeyEvent(ResolveVk(key), false);
        }

        // Keyboard: always use scan code so the event is hardware-identical

        private void SendKeyEvent(ushort vk, bool down)
        {
            if (vk == 0) return;

            var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

            uint flags = KEYEVENTF_SCANCODE;
            if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
            if (!down) flags |= KEYEVENTF_KEYUP;

            var inputs = new INPUT[]
            {
                new()
                {
                    type = INPUT_KEYBOARD,
                    ki   = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags }
                }
            };
            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }

        // Mouse

        private void SendMouseEvent(string key, bool down)
        {
            uint flags;
            uint data = 0;

            switch (key.Trim().ToUpperInvariant())
            {
                case "MOUSE1": flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
                case "MOUSE2": flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
                case "MOUSE3": flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
                case "MOUSE4": flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON1; break;
                case "MOUSE5": flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON2; break;
                default: return;
            }

            var inputs = new INPUT[]
            {
                new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } }
            };
            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }

        // Helpers

        private static bool IsMouseButton(string key)
        {
            var k = key.Trim();
            return k.Equals("Mouse1", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Mouse2", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Mouse3", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Mouse4", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Mouse5", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExtendedKey(ushort vk) => vk switch
        {
            0x2D or 0x2E or
            0x21 or 0x22 or
            0x23 or 0x24 or
            0x25 or 0x26 or 0x27 or 0x28 or
            0xA3 or 0xA5 or
            0x6F => true,
            _ => false
        };

        public static ushort ResolveVk(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;
            key = key.Trim().ToUpperInvariant();
            return key switch
            {
                "A" => 0x41,
                "B" => 0x42,
                "C" => 0x43,
                "D" => 0x44,
                "E" => 0x45,
                "F" => 0x46,
                "G" => 0x47,
                "H" => 0x48,
                "I" => 0x49,
                "J" => 0x4A,
                "K" => 0x4B,
                "L" => 0x4C,
                "M" => 0x4D,
                "N" => 0x4E,
                "O" => 0x4F,
                "P" => 0x50,
                "Q" => 0x51,
                "R" => 0x52,
                "S" => 0x53,
                "T" => 0x54,
                "U" => 0x55,
                "V" => 0x56,
                "W" => 0x57,
                "X" => 0x58,
                "Y" => 0x59,
                "Z" => 0x5A,
                "0" => 0x30,
                "1" => 0x31,
                "2" => 0x32,
                "3" => 0x33,
                "4" => 0x34,
                "5" => 0x35,
                "6" => 0x36,
                "7" => 0x37,
                "8" => 0x38,
                "9" => 0x39,
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F5" => 0x74,
                "F6" => 0x75,
                "F7" => 0x76,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,
                "SHIFT" or "LSHIFT" => 0xA0,
                "RSHIFT" => 0xA1,
                "CTRL" or "LCTRL" or "CONTROL" => 0xA2,
                "RCTRL" => 0xA3,
                "ALT" or "LALT" => 0xA4,
                "RALT" => 0xA5,
                "SPACE" => 0x20,
                "ENTER" or "RETURN" => 0x0D,
                "TAB" => 0x09,
                "ESCAPE" or "ESC" => 0x1B,
                "BACKSPACE" => 0x08,
                "DELETE" or "DEL" => 0x2E,
                "INSERT" or "INS" => 0x2D,
                "HOME" => 0x24,
                "END" => 0x23,
                "PAGEUP" or "PGUP" => 0x21,
                "PAGEDOWN" or "PGDN" => 0x22,
                "UP" => 0x26,
                "DOWN" => 0x28,
                "LEFT" => 0x25,
                "RIGHT" => 0x27,
                "NUMPAD0" => 0x60,
                "NUMPAD1" => 0x61,
                "NUMPAD2" => 0x62,
                "NUMPAD3" => 0x63,
                "NUMPAD4" => 0x64,
                "NUMPAD5" => 0x65,
                "NUMPAD6" => 0x66,
                "NUMPAD7" => 0x67,
                "NUMPAD8" => 0x68,
                "NUMPAD9" => 0x69,
                "MULTIPLY" => 0x6A,
                "ADD" => 0x6B,
                "SUBTRACT" => 0x6D,
                "DECIMAL" => 0x6E,
                "DIVIDE" => 0x6F,
                "MOUSE1" or "MOUSE2" or "MOUSE3" or "MOUSE4" or "MOUSE5" => 0,
                _ => 0
            };
        }

        public static bool IsValidKey(string key)
            => IsMouseButton(key) || ResolveVk(key) != 0;

        public static bool IsValidTriggerKey(string key)
            => !IsMouseButton(key) && ResolveVk(key) != 0;
    }
}