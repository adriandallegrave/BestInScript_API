using System.Runtime.InteropServices;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Reads pixel colors from the desktop framebuffer via GDI.
    ///
    /// DETECTION SAFETY
    /// ----------------
    /// This is a passive read of the OS-composited desktop surface — the same
    /// path screen-capture software and color-picker tools use. It never opens,
    /// reads, or attaches to the game process, so there is nothing for an
    /// anti-cheat to observe. (Contrast with reading game memory, which DOES
    /// touch the process and is detectable.)
    ///
    /// LIMITATION
    /// ----------
    /// GetPixel against the desktop DC returns black/garbage for games running
    /// in true fullscreen-EXCLUSIVE mode. Fullscreen-borderless and windowed
    /// modes both work correctly — most ARPGs default to borderless.
    /// </summary>
    public class ScreenColorService : IScreenSampler
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hDC, int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        // GetPixel returns this when the coordinate is not on any device surface.
        private const uint CLR_INVALID = 0xFFFFFFFF;

        /// <summary>
        /// Reads the color of a single screen pixel (screen coordinates).
        /// Returns null if the read failed — e.g. a fullscreen-exclusive game,
        /// or a coordinate that is off every monitor.
        /// </summary>
        public (int R, int G, int B)? ColorAt(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;
            try
            {
                uint c = GetPixel(hdc, x, y);
                if (c == CLR_INVALID) return null;
                // GetPixel packs the color as 0x00BBGGRR.
                return ((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        /// <summary>
        /// Reads an averaged color over a square region centered on (x, y).
        /// radius 0 = single pixel, 1 = 3x3, 2 = 5x5, etc. Averaging smooths out
        /// icon-edge highlights and single-frame animation artifacts so the
        /// reading does not jitter between samples.
        /// Returns null only if not a single pixel in the region could be read.
        /// </summary>
        public (int R, int G, int B)? ColorAtAveraged(int x, int y, int radius)
        {
            if (radius <= 0) return ColorAt(x, y);

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;
            try
            {
                long sr = 0, sg = 0, sb = 0;
                int n = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        uint c = GetPixel(hdc, x + dx, y + dy);
                        if (c == CLR_INVALID) continue;
                        sr += (int)(c & 0xFF);
                        sg += (int)((c >> 8) & 0xFF);
                        sb += (int)((c >> 16) & 0xFF);
                        n++;
                    }
                }
                if (n == 0) return null;
                return ((int)(sr / n), (int)(sg / n), (int)(sb / n));
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        /// <summary>Current mouse cursor position in screen coordinates.</summary>
        public (int X, int Y)? CursorPosition()
            => GetCursorPos(out var p) ? (p.X, p.Y) : null;

        /// <summary>
        /// Euclidean distance between two RGB colors. Range 0 (identical) to
        /// ~441 (black vs white). A tolerance of ~30-50 reliably separates a
        /// bright "ready" skill icon from its darkened "on cooldown" state.
        /// </summary>
        public static double Distance(int[] a, int[] b)
        {
            if (a is null || b is null || a.Length < 3 || b.Length < 3)
                return double.MaxValue;
            double dr = a[0] - b[0];
            double dg = a[1] - b[1];
            double db = a[2] - b[2];
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }
    }
}