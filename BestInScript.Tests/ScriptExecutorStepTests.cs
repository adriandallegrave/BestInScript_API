using BestInScript.API.Engine;
using BestInScript.API.Models;
using BestInScript.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestInScript.Tests;

/// <summary>
/// Pins RunStepsOnceAsync behavior — including the fair-play invariants:
/// every step incurs exactly one randomized delay inside [DelayMin, DelayMax],
/// and no path skips or zeroes the delay.
/// </summary>
public class ScriptExecutorStepTests
{
    private static (ScriptExecutor Executor, FakeInputSimulator Input, FakeDelayScheduler Delays)
        Build(params double[] randomValues)
    {
        var input = new FakeInputSimulator();
        var delays = new FakeDelayScheduler();
        var executor = new ScriptExecutor(
            NullLogger<ScriptExecutor>.Instance,
            input,
            new FakeScreenSampler(),
            delays,
            new FixedRandomSource(randomValues.Length > 0 ? randomValues : [0.5]));
        return (executor, input, delays);
    }

    private static ScriptConfig Script(double min, double max, params ScriptStep[] steps) => new()
    {
        Name = "test",
        DelayMin = min,
        DelayMax = max,
        Steps = steps.ToList()
    };

    private static ScriptStep Step(string[]? hold = null, string[]? press = null) => new()
    {
        Hold = (hold ?? []).ToList(),
        Press = (press ?? []).ToList()
    };

    [Fact]
    public async Task StepDelay_EqualsMin_WhenRandomZero()
    {
        var (executor, _, delays) = Build(0.0);
        await executor.RunStepsOnceAsync(Script(0.4, 0.6, Step(press: ["A"])), [], CancellationToken.None);

        Assert.Equal(0.4, Assert.Single(delays.Delays).TotalSeconds, 6);
    }

    [Fact]
    public async Task StepDelay_EqualsMax_WhenRandomOne()
    {
        var (executor, _, delays) = Build(1.0);
        await executor.RunStepsOnceAsync(Script(0.4, 0.6, Step(press: ["A"])), [], CancellationToken.None);

        Assert.Equal(0.6, Assert.Single(delays.Delays).TotalSeconds, 6);
    }

    [Fact]
    public async Task StepDelay_AlwaysWithinBounds_AndNeverZero()
    {
        var (executor, _, delays) = Build(0.0, 0.37, 1.0);
        var script = Script(0.4, 0.6, Step(press: ["A"]), Step(press: ["B"]), Step(press: ["C"]));
        await executor.RunStepsOnceAsync(script, [], CancellationToken.None);

        Assert.All(delays.Delays, d =>
        {
            Assert.True(d.TotalSeconds > 0, "Delay must never be zero — humanlike pacing is mandatory.");
            Assert.InRange(d.TotalSeconds, 0.4 - 1e-9, 0.6 + 1e-9);
        });
    }

    [Fact]
    public async Task EveryStep_IncursExactlyOneDelay()
    {
        var (executor, _, delays) = Build();
        var script = Script(0.4, 0.6, Step(press: ["A"]), Step(press: ["B"]), Step(press: ["C"]));
        await executor.RunStepsOnceAsync(script, [], CancellationToken.None);

        Assert.Equal(script.Steps.Count, delays.Delays.Count);
    }

    [Fact]
    public async Task HeldKey_SharedAcrossConsecutiveSteps_IsNotReleased()
    {
        var (executor, input, _) = Build();
        var script = Script(0.4, 0.6, Step(hold: ["Shift"]), Step(hold: ["Shift"]));
        await executor.RunStepsOnceAsync(script, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None);

        Assert.Equal([("down", "Shift")], input.Events);
    }

    [Fact]
    public async Task HeldKey_DroppedByNextStep_IsReleasedFirst()
    {
        var (executor, input, _) = Build();
        var script = Script(0.4, 0.6, Step(hold: ["Shift"]), Step(hold: ["Ctrl"]));
        await executor.RunStepsOnceAsync(script, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None);

        Assert.Equal([("down", "Shift"), ("up", "Shift"), ("down", "Ctrl")], input.Events);
    }

    [Fact]
    public async Task EventOrder_ReleaseThenHoldThenPress()
    {
        var (executor, input, _) = Build();
        var script = Script(0.4, 0.6, Step(hold: ["A"], press: ["X"]), Step(hold: ["B"], press: ["Y"]));
        await executor.RunStepsOnceAsync(script, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None);

        Assert.Equal(
            [("down", "A"), ("press", "X"), ("up", "A"), ("down", "B"), ("press", "Y")],
            input.Events);
    }

    [Fact]
    public async Task CancelledToken_StopsMidSequence()
    {
        var input = new FakeInputSimulator();
        var cts = new CancellationTokenSource();
        var delays = new FakeDelayScheduler { CancelAfter = 1, Cts = cts };
        var executor = new ScriptExecutor(
            NullLogger<ScriptExecutor>.Instance, input, new FakeScreenSampler(),
            delays, new FixedRandomSource(0.5));

        var script = Script(0.4, 0.6, Step(press: ["A"]), Step(press: ["B"]), Step(press: ["C"]));
        await executor.RunStepsOnceAsync(script, [], cts.Token);

        // Cancellation fired during step 1's delay — steps 2 and 3 never run.
        Assert.Equal([("press", "A")], input.Events);
        Assert.Single(delays.Delays);
    }
}
