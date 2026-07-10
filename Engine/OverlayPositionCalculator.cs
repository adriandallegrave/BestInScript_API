namespace BestInScript.API.Engine
{
    /// <summary>
    /// Pure geometry for the overlay's custom (dragged) position: pick the screen
    /// under a point, convert between a screen-relative offset and an absolute
    /// top-left, and clamp so the pill can never sit fully off-screen. Kept
    /// Win32/DPI-free so it is unit-testable (mirrors <see cref="EventScheduleCalculator"/>).
    ///
    /// All coordinates are device-independent pixels (DIP); the caller converts
    /// physical monitor bounds to DIP before handing rects in.
    /// </summary>
    public static class OverlayPositionCalculator
    {
        /// <summary>A screen's bounds in device-independent pixels.</summary>
        public readonly record struct ScreenRect(double Left, double Top, double Width, double Height);

        /// <summary>
        /// Index of the screen whose bounds contain (<paramref name="x"/>,<paramref name="y"/>),
        /// or <paramref name="fallback"/> when the point is outside every screen.
        /// </summary>
        public static int ScreenIndexAt(
            IReadOnlyList<ScreenRect> screens, double x, double y, int fallback)
        {
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                if (x >= s.Left && x < s.Left + s.Width &&
                    y >= s.Top && y < s.Top + s.Height)
                    return i;
            }
            return fallback;
        }

        /// <summary>Offset of an absolute top-left from a screen's top-left.</summary>
        public static (double X, double Y) ToRelative(ScreenRect screen, double absX, double absY)
            => (absX - screen.Left, absY - screen.Top);

        /// <summary>
        /// Absolute, clamped top-left for a screen-relative offset. Clamps so a
        /// <paramref name="winW"/>×<paramref name="winH"/> window stays fully on
        /// <paramref name="screen"/> (or pins to the top-left when it is larger
        /// than the screen).
        /// </summary>
        public static (double X, double Y) ToAbsoluteClamped(
            ScreenRect screen, double relX, double relY, double winW, double winH)
        {
            double x = Clamp(relX, 0, screen.Width - winW);
            double y = Clamp(relY, 0, screen.Height - winH);
            return (screen.Left + x, screen.Top + y);
        }

        // max < min when the window is larger than the screen → pin to min.
        private static double Clamp(double v, double min, double max)
        {
            if (max < min) return min;
            return v < min ? min : (v > max ? max : v);
        }
    }
}
