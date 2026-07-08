using BestInScript.API.Services;
using BestInScript.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestInScript.Tests;

/// <summary>
/// Pins the guided in-game capture (BACKLOG 2.1): arm/disarm, hotkey consumption,
/// the two-pass ready→cooldown flow, and the coordinate-nudge suggestion.
/// </summary>
public class PixelCaptureServiceTests
{
    private static ushort Vk(string key) => InputSimulatorService.ResolveVk(key);

    private static PixelCaptureService New(FakeScreenSampler screen)
        => new(screen, NullLogger<PixelCaptureService>.Instance);

    [Fact]
    public void Arm_RejectsInvalidAndMouseKeys()
    {
        var svc = New(new FakeScreenSampler());

        Assert.NotNull(svc.Arm("Mouse1"));   // mouse buttons never reach the keyboard hook
        Assert.NotNull(svc.Arm("Nonsense")); // unresolvable
        Assert.Null(svc.Arm("F8"));          // valid

        Assert.Equal("AwaitingReady", svc.GetState().Stage);
    }

    [Fact]
    public void TryConsumeKey_FalseWhenIdleOrWrongKey_TrueOnArmedHotkey()
    {
        var svc = New(new FakeScreenSampler());

        Assert.False(svc.TryConsumeKey(Vk("F8"))); // not armed

        svc.Arm("F8");
        Assert.False(svc.TryConsumeKey(Vk("F9"))); // wrong key
        Assert.True(svc.TryConsumeKey(Vk("F8")));  // armed hotkey — consumed
    }

    [Fact]
    public void PerformCapture_TwoPasses_FillsBothColors_AndAdvancesToDone()
    {
        var screen = new FakeScreenSampler { Fixed = (50, 60, 70) };
        var svc = New(screen);

        svc.Arm("F8");
        Assert.Equal("AwaitingReady", svc.GetState().Stage);

        svc.PerformCapture(10, 20); // ready pass
        var mid = svc.GetState();
        Assert.Equal("AwaitingCooldown", mid.Stage);
        Assert.Equal(new[] { 50, 60, 70 }, mid.ReadyColor);
        Assert.Equal(10, mid.X);
        Assert.Equal(20, mid.Y);

        screen.Fixed = (10, 10, 10);
        svc.PerformCapture(10, 20); // cooldown pass
        var done = svc.GetState();
        Assert.Equal("Done", done.Stage);
        Assert.Equal(new[] { 10, 10, 10 }, done.CoolColor);
    }

    [Fact]
    public async Task TryConsumeKey_ReadyUsesCursor_CooldownReusesStoredCoord()
    {
        var screen = new FakeScreenSampler { Fixed = (5, 5, 5), Cursor = (77, 88) };
        var svc = New(screen);
        svc.Arm("F8");

        Assert.True(svc.TryConsumeKey(Vk("F8")));      // ready pass samples the cursor
        await WaitForStage(svc, "AwaitingCooldown");
        Assert.Equal(77, svc.GetState().X);
        Assert.Equal(88, svc.GetState().Y);

        screen.Cursor = (999, 999);                    // cooldown pass must ignore the moved cursor
        Assert.True(svc.TryConsumeKey(Vk("F8")));
        await WaitForStage(svc, "Done");
        Assert.Equal(77, svc.GetState().X);
        Assert.Equal(88, svc.GetState().Y);
    }

    [Fact]
    public void Suggestion_RecommendsOffsetWithLargestSeparation()
    {
        int span = 2 * PixelCaptureService.GridRadius + 1; // 7
        int center = PixelCaptureService.GridRadius * span + PixelCaptureService.GridRadius; // index 24
        // Offset dx=2, dy=0 → grid index (0+3)*7 + (2+3) = 26.
        const int strongIdx = 26;

        var screen = new FakeScreenSampler();
        // Ready pass: every cell identical.
        for (int i = 0; i < span * span; i++)
            screen.Colors.Enqueue((100, 100, 100));
        // Cooldown pass: near-identical everywhere (sep 8) except the strong offset (sep large).
        for (int i = 0; i < span * span; i++)
            screen.Colors.Enqueue(i == strongIdx ? ((int, int, int)?)(0, 0, 0) : (100, 100, 108));

        var svc = New(screen);
        svc.Arm("F8");
        svc.PerformCapture(100, 100); // ready
        svc.PerformCapture(100, 100); // cooldown

        var st = svc.GetState();
        Assert.Equal("Done", st.Stage);
        Assert.NotNull(st.Suggestion);
        Assert.Equal(2, st.Suggestion!.Dx);
        Assert.Equal(0, st.Suggestion.Dy);
        Assert.Equal(102, st.Suggestion.SuggestedX);
        Assert.Equal(100, st.Suggestion.SuggestedY);
        Assert.Equal(8, st.Suggestion.SeparationAtCenter, 3);
        Assert.True(st.Suggestion.SeparationAtBest > st.Suggestion.SeparationAtCenter);
    }

    [Fact]
    public void UnreadableCenter_FlagsUnreadable_AndKeepsStage()
    {
        var screen = new FakeScreenSampler { Fixed = null }; // every read fails
        var svc = New(screen);
        svc.Arm("F8");

        svc.PerformCapture(10, 20);

        var st = svc.GetState();
        Assert.True(st.Unreadable);
        Assert.Equal("AwaitingReady", st.Stage); // stayed put so the user can retry
        Assert.Null(st.ReadyColor);
    }

    [Fact]
    public void Disarm_ResetsToIdle_AndStopsConsuming()
    {
        var svc = New(new FakeScreenSampler());
        svc.Arm("F8");

        svc.Disarm();

        Assert.Equal("Idle", svc.GetState().Stage);
        Assert.False(svc.TryConsumeKey(Vk("F8")));
    }

    private static async Task WaitForStage(PixelCaptureService svc, string stage)
    {
        for (int i = 0; i < 200; i++)
        {
            if (svc.GetState().Stage == stage) return;
            await Task.Delay(5);
        }
        throw new TimeoutException($"Capture stage '{stage}' was not reached in time.");
    }
}
