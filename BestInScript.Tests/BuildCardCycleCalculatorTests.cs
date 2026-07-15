using BestInScript.API.Engine;

namespace BestInScript.Tests;

/// <summary>
/// Pins the build panel's one-key cycle: hidden → first → next → … → hidden,
/// and the re-validation that keeps a stale selection from indexing past the end
/// after cards are added/removed in the web UI.
/// </summary>
public class BuildCardCycleCalculatorTests
{
    private const int Hidden = BuildCardCycleCalculator.Hidden;

    // ── Next ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Next_FromHidden_SelectsFirstCard()
        => Assert.Equal(0, BuildCardCycleCalculator.Next(Hidden, count: 3));

    [Fact]
    public void Next_WalksForwardThroughCards()
    {
        Assert.Equal(1, BuildCardCycleCalculator.Next(0, count: 3));
        Assert.Equal(2, BuildCardCycleCalculator.Next(1, count: 3));
    }

    [Fact]
    public void Next_PastLastCard_HidesRatherThanWrappingToFirst()
        => Assert.Equal(Hidden, BuildCardCycleCalculator.Next(2, count: 3));

    [Fact]
    public void Next_SingleCard_TogglesOnThenOff()
    {
        var shown = BuildCardCycleCalculator.Next(Hidden, count: 1);
        Assert.Equal(0, shown);
        Assert.Equal(Hidden, BuildCardCycleCalculator.Next(shown, count: 1));
    }

    [Fact]
    public void Next_NoCards_StaysHidden()
    {
        Assert.Equal(Hidden, BuildCardCycleCalculator.Next(Hidden, count: 0));
        // Even from a stale selection left over from before the last card was deleted.
        Assert.Equal(Hidden, BuildCardCycleCalculator.Next(0, count: 0));
    }

    [Fact]
    public void Next_FullCycle_ReturnsToHidden()
    {
        var i = Hidden;
        for (int n = 0; n < 3; n++)
            i = BuildCardCycleCalculator.Next(i, count: 3);

        Assert.Equal(2, i);
        Assert.Equal(Hidden, BuildCardCycleCalculator.Next(i, count: 3));
    }

    [Fact]
    public void Next_IndexBeyondCount_Hides()
        => Assert.Equal(Hidden, BuildCardCycleCalculator.Next(9, count: 3));

    // ── Clamp ────────────────────────────────────────────────────────────────

    [Fact]
    public void Clamp_ValidSelection_Kept()
        => Assert.Equal(1, BuildCardCycleCalculator.Clamp(1, count: 3));

    [Fact]
    public void Clamp_SelectionPastEnd_Hides()
        => Assert.Equal(Hidden, BuildCardCycleCalculator.Clamp(3, count: 3));

    [Fact]
    public void Clamp_NoCards_Hides()
        => Assert.Equal(Hidden, BuildCardCycleCalculator.Clamp(0, count: 0));

    [Fact]
    public void Clamp_AlreadyHidden_StaysHidden()
        => Assert.Equal(Hidden, BuildCardCycleCalculator.Clamp(Hidden, count: 3));
}
