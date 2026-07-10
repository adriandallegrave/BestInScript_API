using BestInScript.API.Engine;
using BestInScript.API.Models;

namespace BestInScript.Tests;

/// <summary>
/// Pins the pure event-timer math: next-event selection (strictly future),
/// the Helltide active-vs-lock window at start + 55 min, and H:MM formatting.
/// </summary>
public class EventScheduleCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static long N => Now.ToUnixTimeSeconds();

    private static EventEntry E(long ts, string? boss = null, string? zone = null) => new()
    {
        Timestamp = ts,
        Boss = boss,
        Zone = zone == null ? null : new List<EventZone> { new() { Name = zone } }
    };

    // ── Next-event selection ────────────────────────────────────────────────

    [Fact]
    public void Boss_PicksEarliestFuture_SkipsPast()
    {
        var sched = new EventSchedule
        {
            WorldBoss =
            {
                E(N - 600, "Ashava", "Fractured Peaks"),   // past — ignored
                E(N + 1800, "Azmodan", "Dry Steppes"),      // next
                E(N + 5400, "Avarice", "Scosglen")
            }
        };

        var snap = EventScheduleCalculator.GetSnapshot(sched, Now);

        Assert.True(snap.HasData);
        Assert.NotNull(snap.Boss);
        Assert.Equal("Azmodan", snap.Boss!.Name);
        Assert.Equal("Dry Steppes", snap.Boss.Zone);
        Assert.Equal(N + 1800, snap.Boss.StartUnix);
    }

    [Fact]
    public void Boss_MissingName_FallsBackToGenericLabel()
    {
        var sched = new EventSchedule { WorldBoss = { E(N + 60) } };
        var snap = EventScheduleCalculator.GetSnapshot(sched, Now);
        Assert.Equal("World Boss", snap.Boss!.Name);
        Assert.Equal("", snap.Boss.Zone);
    }

    [Fact]
    public void Legion_PicksEarliestFuture()
    {
        var sched = new EventSchedule
        {
            Legion = { E(N - 60), E(N + 300), E(N + 1500) }
        };
        var snap = EventScheduleCalculator.GetSnapshot(sched, Now);
        Assert.Equal(N + 300, snap.LegionStartUnix);
    }

    // ── Helltide active vs. lock window (55 min) ─────────────────────────────

    [Fact]
    public void Helltide_LiveNow_ReportsActiveAndEndTime()
    {
        // Started 10 min ago → still within the 55-min active window.
        long start = N - 600;
        var sched = new EventSchedule { Helltide = { E(start), E(N + 3000) } };

        var snap = EventScheduleCalculator.GetSnapshot(sched, Now);

        Assert.NotNull(snap.Helltide);
        Assert.True(snap.Helltide!.Active);
        Assert.Equal(start + EventScheduleCalculator.HelltideActiveMinutes * 60, snap.Helltide.TargetUnix);
    }

    [Fact]
    public void Helltide_InLockGap_ReportsNextStart()
    {
        // Last start was 60 min ago (ended 5 min ago); next starts in 5 min.
        var sched = new EventSchedule { Helltide = { E(N - 3600), E(N + 300) } };

        var snap = EventScheduleCalculator.GetSnapshot(sched, Now);

        Assert.NotNull(snap.Helltide);
        Assert.False(snap.Helltide!.Active);
        Assert.Equal(N + 300, snap.Helltide.TargetUnix);
    }

    [Fact]
    public void Helltide_ExactlyAtActiveBoundary_IsNotActive()
    {
        // Started exactly 55 min ago: active window ends *now*, and the rule is
        // strict (now < start + active), so it must read as locked, not active.
        long start = N - EventScheduleCalculator.HelltideActiveMinutes * 60;
        var sched = new EventSchedule { Helltide = { E(start), E(N + 300) } };

        var snap = EventScheduleCalculator.GetSnapshot(sched, Now);

        Assert.False(snap.Helltide!.Active);
        Assert.Equal(N + 300, snap.Helltide.TargetUnix);
    }

    // ── Empty / null handling ────────────────────────────────────────────────

    [Fact]
    public void NullSchedule_IsEmpty()
    {
        var snap = EventScheduleCalculator.GetSnapshot(null, Now);
        Assert.False(snap.HasData);
        Assert.Null(snap.Boss);
        Assert.Null(snap.LegionStartUnix);
        Assert.Null(snap.Helltide);
    }

    [Fact]
    public void AllEventsPast_HasNoData()
    {
        var sched = new EventSchedule
        {
            WorldBoss = { E(N - 100) },
            Legion = { E(N - 100) },
            Helltide = { E(N - 100000) }   // long past → not active, no future start
        };
        var snap = EventScheduleCalculator.GetSnapshot(sched, Now);
        Assert.False(snap.HasData);
    }

    // ── Formatting ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(43 * 60, "0:43")]
    [InlineData(3 * 3600 + 5 * 60, "3:05")]
    [InlineData(60, "0:01")]
    [InlineData(59, "0:00")]     // floored minutes
    [InlineData(0, "0:00")]
    [InlineData(-100, "0:00")]   // clamped
    public void FormatRemaining_RendersHMM(long secondsAhead, string expected)
    {
        Assert.Equal(expected, EventScheduleCalculator.FormatRemaining(N + secondsAhead, N));
    }
}
