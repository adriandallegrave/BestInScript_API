using BestInScript.API.Models;

namespace BestInScript.API.Engine
{
    /// <summary>
    /// Pure geometry for placing an overlay window: resolve a 9-point anchor, pick the
    /// screen under a point, convert between a screen-relative offset and an absolute
    /// top-left, and clamp so a window can never sit fully off-screen. Kept
    /// Win32/DPI-free so it is unit-testable (mirrors <see cref="EventScheduleCalculator"/>).
    ///
    /// Shared by the status pill and the build-guide panel — both anchor the same way.
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

        /// <summary>
        /// Absolute top-left for a <paramref name="winW"/>×<paramref name="winH"/> window
        /// docked to one of the 8 edge/corner anchors (or centered) on
        /// <paramref name="screen"/>, inset by <paramref name="margin"/>.
        ///
        /// <see cref="OverlayAnchor.Custom"/> has no anchored position of its own — callers
        /// handle it via <see cref="ToAbsoluteClamped"/> — and falls through to centered here.
        /// </summary>
        public static (double X, double Y) AnchoredTopLeft(
            ScreenRect screen, OverlayAnchor anchor, double margin, double winW, double winH)
        {
            double x = anchor switch
            {
                OverlayAnchor.TopLeft or OverlayAnchor.MiddleLeft or OverlayAnchor.BottomLeft
                    => screen.Left + margin,
                OverlayAnchor.TopRight or OverlayAnchor.MiddleRight or OverlayAnchor.BottomRight
                    => screen.Left + screen.Width - winW - margin,
                _ => screen.Left + (screen.Width - winW) / 2
            };

            double y = anchor switch
            {
                OverlayAnchor.TopLeft or OverlayAnchor.TopCenter or OverlayAnchor.TopRight
                    => screen.Top + margin,
                OverlayAnchor.BottomLeft or OverlayAnchor.BottomCenter or OverlayAnchor.BottomRight
                    => screen.Top + screen.Height - winH - margin,
                _ => screen.Top + (screen.Height - winH) / 2
            };

            return (x, y);
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
