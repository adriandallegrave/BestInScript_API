using BestInScript.API.Engine;
using BestInScript.API.Models;

namespace BestInScript.Tests;

/// <summary>
/// Pins the two-color ready rule: dReady &lt;= Tolerance AND dReady &lt; dCooldown.
/// These tests exist so the rule can never be collapsed to a single-threshold
/// comparison without a red build.
/// </summary>
public class PixelReadyEvaluatorTests
{
    private static PixelTrigger Trigger(int[] ready, int[] cooldown, int tolerance) => new()
    {
        ReadyColor = ready,
        CooldownColor = cooldown,
        Tolerance = tolerance
    };

    [Fact]
    public void ExactReadyColor_IsReady()
    {
        var pt = Trigger([200, 200, 200], [60, 60, 60], 40);
        Assert.True(PixelReadyEvaluator.IsReady((200, 200, 200), pt));
    }

    [Fact]
    public void WithinTolerance_CloserToReady_IsReady()
    {
        var pt = Trigger([200, 200, 200], [60, 60, 60], 40);
        Assert.True(PixelReadyEvaluator.IsReady((210, 210, 210), pt));
    }

    [Fact]
    public void WithinTolerance_ButCloserToCooldown_IsNotReady()
    {
        // Sample is within tolerance of ready (distance ~13.9) but even closer
        // to the cooldown color (~3.5) — the second comparison must veto it.
        var pt = Trigger([100, 100, 100], [90, 90, 90], 40);
        Assert.False(PixelReadyEvaluator.IsReady((92, 92, 92), pt));
    }

    [Fact]
    public void MatchesNeitherColor_IsNotReady()
    {
        var pt = Trigger([255, 0, 0], [0, 0, 255], 40);
        Assert.False(PixelReadyEvaluator.IsReady((0, 255, 0), pt));
    }

    [Fact]
    public void AtToleranceBoundary_IsReady()
    {
        // Distance to ready is exactly 50 (3-4-5 triple × 10); tolerance is 50 — <= keeps it ready.
        var pt = Trigger([0, 0, 0], [255, 255, 255], 50);
        Assert.True(PixelReadyEvaluator.IsReady((30, 40, 0), pt));
    }

    [Fact]
    public void EquidistantFromBoth_IsNotReady()
    {
        // dReady == dCooldown (50 each) and tolerance is generous — the strict
        // '<' against cooldown must reject the tie.
        var pt = Trigger([0, 0, 0], [100, 0, 0], 100);
        Assert.False(PixelReadyEvaluator.IsReady((50, 0, 0), pt));
    }
}
