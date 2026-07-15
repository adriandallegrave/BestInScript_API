using BestInScript.API.Engine;
using BestInScript.API.Models;
using Rect = BestInScript.API.Engine.OverlayPositionCalculator.ScreenRect;

namespace BestInScript.Tests;

/// <summary>
/// Pins the pure geometry shared by the status pill and the build-guide panel:
/// 9-point anchoring, screen-under-a-point selection, relative↔absolute offset
/// round-trips, and the off-screen clamp.
/// </summary>
public class OverlayPositionCalculatorTests
{
    // Two side-by-side 1920×1080 screens; the second starts at x=1920.
    private static readonly IReadOnlyList<Rect> TwoScreens = new[]
    {
        new Rect(0, 0, 1920, 1080),
        new Rect(1920, 0, 1920, 1080)
    };

    // ── AnchoredTopLeft ─────────────────────────────────────────────────────

    // A 200×100 window with a 10px margin on the second screen, so the tests also
    // prove the anchor is applied relative to that screen's origin, not the desktop's.
    private static (double X, double Y) Anchored(OverlayAnchor anchor)
        => OverlayPositionCalculator.AnchoredTopLeft(
            TwoScreens[1], anchor, margin: 10, winW: 200, winH: 100);

    [Fact]
    public void AnchoredTopLeft_Corners_InsetByMarginFromTheScreenOrigin()
    {
        Assert.Equal((1930d, 10d), Anchored(OverlayAnchor.TopLeft));
        Assert.Equal((1920 + 1920 - 200 - 10, 10d), Anchored(OverlayAnchor.TopRight));
        Assert.Equal((1930d, 1080 - 100 - 10d), Anchored(OverlayAnchor.BottomLeft));
        Assert.Equal((1920 + 1920 - 200 - 10, 1080 - 100 - 10d), Anchored(OverlayAnchor.BottomRight));
    }

    [Fact]
    public void AnchoredTopLeft_Edges_CenterOnTheFreeAxis()
    {
        // Top/bottom center: x centered, y pinned by the margin.
        Assert.Equal((1920 + (1920 - 200) / 2d, 10d), Anchored(OverlayAnchor.TopCenter));
        Assert.Equal((1920 + (1920 - 200) / 2d, 1080 - 100 - 10d), Anchored(OverlayAnchor.BottomCenter));

        // Left/right middle: y centered, x pinned by the margin.
        Assert.Equal((1930d, (1080 - 100) / 2d), Anchored(OverlayAnchor.MiddleLeft));
        Assert.Equal((1920 + 1920 - 200 - 10, (1080 - 100) / 2d), Anchored(OverlayAnchor.MiddleRight));
    }

    [Fact]
    public void AnchoredTopLeft_MiddleCenter_CentersBothAxes()
        => Assert.Equal(
            (1920 + (1920 - 200) / 2d, (1080 - 100) / 2d),
            Anchored(OverlayAnchor.MiddleCenter));

    [Fact]
    public void AnchoredTopLeft_Custom_FallsBackToCentered()
        // Custom has no anchored position of its own — callers route it to
        // ToAbsoluteClamped — so this only pins that it stays on-screen.
        => Assert.Equal(Anchored(OverlayAnchor.MiddleCenter), Anchored(OverlayAnchor.Custom));

    [Fact]
    public void AnchoredTopLeft_WindowWiderThanScreen_LeavesTheLeftEdgeVisible()
    {
        // A build card scaled past the monitor width: the overhang must go right, so the
        // start of the image is still readable rather than centered half off both edges.
        var (x, _) = OverlayPositionCalculator.AnchoredTopLeft(
            TwoScreens[0], OverlayAnchor.TopLeft, margin: 10, winW: 3000, winH: 100);

        Assert.Equal(10d, x);
    }

    // ── ScreenIndexAt ───────────────────────────────────────────────────────

    [Fact]
    public void ScreenIndexAt_PicksScreenContainingPoint()
    {
        Assert.Equal(0, OverlayPositionCalculator.ScreenIndexAt(TwoScreens, 100, 100, fallback: -1));
        Assert.Equal(1, OverlayPositionCalculator.ScreenIndexAt(TwoScreens, 2000, 500, fallback: -1));
    }

    [Fact]
    public void ScreenIndexAt_ReturnsFallback_WhenOutsideAll()
    {
        Assert.Equal(7, OverlayPositionCalculator.ScreenIndexAt(TwoScreens, -50, -50, fallback: 7));
    }

    [Fact]
    public void ScreenIndexAt_RightEdgeIsExclusive_NextScreenInclusive()
    {
        // x == 1920 belongs to the second screen (left-inclusive, right-exclusive).
        Assert.Equal(1, OverlayPositionCalculator.ScreenIndexAt(TwoScreens, 1920, 0, fallback: -1));
    }

    // ── Relative ↔ absolute round-trip ──────────────────────────────────────

    [Fact]
    public void ToRelative_ThenToAbsolute_RoundTrips_WithinScreen()
    {
        var screen = TwoScreens[1];               // origin at (1920, 0)
        var (relX, relY) = OverlayPositionCalculator.ToRelative(screen, 2200, 300);
        Assert.Equal(280, relX);
        Assert.Equal(300, relY);

        var (absX, absY) = OverlayPositionCalculator.ToAbsoluteClamped(screen, relX, relY, 200, 34);
        Assert.Equal(2200, absX);
        Assert.Equal(300, absY);
    }

    // ── Clamp ────────────────────────────────────────────────────────────────

    [Fact]
    public void ToAbsoluteClamped_KeepsWindowFullyOnScreen()
    {
        var screen = new Rect(0, 0, 1920, 1080);

        // Way past the bottom-right → clamped so the 200×34 window still fits.
        var (x, y) = OverlayPositionCalculator.ToAbsoluteClamped(screen, 5000, 5000, 200, 34);
        Assert.Equal(1920 - 200, x);
        Assert.Equal(1080 - 34, y);

        // Negative offsets clamp to the screen's top-left.
        var (nx, ny) = OverlayPositionCalculator.ToAbsoluteClamped(screen, -100, -100, 200, 34);
        Assert.Equal(0, nx);
        Assert.Equal(0, ny);
    }

    [Fact]
    public void ToAbsoluteClamped_PinsToTopLeft_WhenWindowLargerThanScreen()
    {
        var screen = new Rect(100, 50, 150, 40);
        var (x, y) = OverlayPositionCalculator.ToAbsoluteClamped(screen, 20, 20, 300, 300);
        Assert.Equal(100, x);
        Assert.Equal(50, y);
    }
}
