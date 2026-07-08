using BestInScript.API.Engine;
using BestInScript.API.Models;
using BestInScript.API.Services;
using BestInScript.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestInScript.Tests;

/// <summary>
/// Pins the ownership model: a script runs iff its owner set is non-empty;
/// owners are the user sentinel and/or active preset ids.
/// </summary>
public class ScriptCoordinatorTests
{
    private readonly FakeScriptRunner _runner = new();
    private readonly FakeScreenSampler _screen = new();
    private readonly PixelCaptureService _capture;
    private readonly ScriptCoordinator _coordinator;

    public ScriptCoordinatorTests()
    {
        _capture = new PixelCaptureService(_screen, NullLogger<PixelCaptureService>.Instance);
        _coordinator = new ScriptCoordinator(NullLogger<ScriptCoordinator>.Instance, _runner, _capture);
    }

    private static ScriptConfig Script(string name, string trigger) => new()
    {
        Name = name,
        TriggerKey = trigger,
        Steps = [new ScriptStep { Press = ["A"] }]
    };

    private static Preset Preset(string name, string trigger, params Guid[] scriptIds) => new()
    {
        Name = name,
        TriggerKey = trigger,
        ScriptIds = scriptIds.ToList()
    };

    private static ushort Vk(string key) => InputSimulatorService.ResolveVk(key);

    private bool IsRunning(Guid id)
        => _coordinator.GetStatus().Single(s => s.Id == id).IsRunning;

    [Fact]
    public void UserToggle_StartsScript()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);

        _coordinator.HandleTriggerKey(Vk("F1"));

        Assert.True(IsRunning(s.Id));
        Assert.Single(_runner.Started);
    }

    [Fact]
    public void UserToggle_Twice_StopsScript()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);

        _coordinator.HandleTriggerKey(Vk("F1"));
        _coordinator.HandleTriggerKey(Vk("F1"));

        Assert.False(IsRunning(s.Id));
    }

    [Fact]
    public void PresetToggle_ClaimsAllMembers()
    {
        var s1 = Script("s1", "F1");
        var s2 = Script("s2", "F2");
        _coordinator.RegisterScript(s1);
        _coordinator.RegisterScript(s2);
        _coordinator.RegisterPreset(Preset("p", "F5", s1.Id, s2.Id));

        _coordinator.HandleTriggerKey(Vk("F5"));

        Assert.True(IsRunning(s1.Id));
        Assert.True(IsRunning(s2.Id));
        Assert.True(_coordinator.GetPresetStatus().Single().IsActive);
    }

    [Fact]
    public void PresetToggle_Off_ReleasesOnlyItsClaim()
    {
        var s1 = Script("s1", "F1");
        var s2 = Script("s2", "F2");
        _coordinator.RegisterScript(s1);
        _coordinator.RegisterScript(s2);
        _coordinator.RegisterPreset(Preset("p", "F5", s1.Id, s2.Id));

        _coordinator.HandleTriggerKey(Vk("F5"));  // preset on: claims s1 + s2
        _coordinator.HandleTriggerKey(Vk("F1"));  // user also claims s1
        _coordinator.HandleTriggerKey(Vk("F5"));  // preset off

        Assert.True(IsRunning(s1.Id));   // user still owns it
        Assert.False(IsRunning(s2.Id));  // only the preset owned it
    }

    [Fact]
    public void TwoPresets_SharingMember_KeepRunningUntilBothOff()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);
        _coordinator.RegisterPreset(Preset("p1", "F5", s.Id));
        _coordinator.RegisterPreset(Preset("p2", "F6", s.Id));

        _coordinator.HandleTriggerKey(Vk("F5"));
        _coordinator.HandleTriggerKey(Vk("F6"));
        _coordinator.HandleTriggerKey(Vk("F5"));  // p1 off — p2 still owns
        Assert.True(IsRunning(s.Id));

        _coordinator.HandleTriggerKey(Vk("F6"));  // p2 off — last owner released
        Assert.False(IsRunning(s.Id));
    }

    [Fact]
    public void UserAndPreset_ScriptStopsOnlyWhenBothRelease()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);
        _coordinator.RegisterPreset(Preset("p", "F5", s.Id));

        _coordinator.HandleTriggerKey(Vk("F1"));  // user claim
        _coordinator.HandleTriggerKey(Vk("F5"));  // preset claim
        _coordinator.HandleTriggerKey(Vk("F1"));  // user release — preset still owns
        Assert.True(IsRunning(s.Id));

        _coordinator.HandleTriggerKey(Vk("F5"));  // preset release
        Assert.False(IsRunning(s.Id));
    }

    [Fact]
    public void RegisterScript_ReappliesActivePresetOwnership()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);
        _coordinator.RegisterPreset(Preset("p", "F5", s.Id));
        _coordinator.HandleTriggerKey(Vk("F5"));

        // Edit the script while the preset owns it (same id, new config).
        var edited = Script("s-edited", "F1");
        edited.Id = s.Id;
        _coordinator.RegisterScript(edited);

        // The preset's claim was re-applied, so the edited script keeps running.
        Assert.True(IsRunning(s.Id));
        Assert.Equal(2, _runner.Started.Count); // old run stopped, new run started
    }

    [Fact]
    public void RegisterScript_TriggerChange_RemovesOldVkBinding()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);

        var edited = Script("s", "F2");
        edited.Id = s.Id;
        _coordinator.RegisterScript(edited);

        _coordinator.HandleTriggerKey(Vk("F1")); // old binding must be gone
        Assert.False(IsRunning(s.Id));

        _coordinator.HandleTriggerKey(Vk("F2")); // new binding works
        Assert.True(IsRunning(s.Id));
    }

    [Fact]
    public void HandleTriggerKey_ScriptWinsOverPreset_OnSameVk()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);
        _coordinator.RegisterPreset(Preset("p", "F1", s.Id));

        _coordinator.HandleTriggerKey(Vk("F1"));

        Assert.True(IsRunning(s.Id));
        Assert.False(_coordinator.GetPresetStatus().Single().IsActive);
    }

    [Fact]
    public void StopAll_ClearsOwners_AndDeactivatesPresets()
    {
        var s1 = Script("s1", "F1");
        var s2 = Script("s2", "F2");
        _coordinator.RegisterScript(s1);
        _coordinator.RegisterScript(s2);
        _coordinator.RegisterPreset(Preset("p", "F5", s2.Id));

        _coordinator.HandleTriggerKey(Vk("F1")); // user claims s1
        _coordinator.HandleTriggerKey(Vk("F5")); // preset claims s2

        _coordinator.StopAll();

        Assert.False(IsRunning(s1.Id));
        Assert.False(IsRunning(s2.Id));
        Assert.False(_coordinator.GetPresetStatus().Single().IsActive);
    }

    [Fact]
    public void CancelAllRunning_KeepsOwners()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);
        _coordinator.HandleTriggerKey(Vk("F1"));

        _coordinator.CancelAllRunning();

        // Shutdown cancels the task but does NOT release the user's claim —
        // distinct from StopAll (status still reports the owner as attached).
        Assert.True(IsRunning(s.Id));
    }

    [Fact]
    public void UnregisterScript_StopsAndRemoves()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);
        _coordinator.HandleTriggerKey(Vk("F1"));

        _coordinator.UnregisterScript(s.Id);

        Assert.Empty(_coordinator.GetStatus());
    }

    [Fact]
    public void ClearAll_EmptiesRegistries_AndStopsRuns()
    {
        var s1 = Script("s1", "F1");
        var s2 = Script("s2", "F2");
        _coordinator.RegisterScript(s1);
        _coordinator.RegisterScript(s2);
        _coordinator.RegisterPreset(Preset("p", "F5", s1.Id));
        _coordinator.HandleTriggerKey(Vk("F1")); // s1 running

        _coordinator.ClearAll();

        // Both registries emptied — used on a profile switch before loading the new config.
        Assert.Empty(_coordinator.GetStatus());
        Assert.Empty(_coordinator.GetPresetStatus());
        Assert.Equal(0, _coordinator.ScriptCount);
        Assert.Equal(0, _coordinator.PresetCount);
        Assert.True(_runner.Tokens.Single().IsCancellationRequested); // the running script was cancelled
    }

    [Fact]
    public void ArmedCaptureHotkey_IsConsumed_DoesNotToggleScript()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);
        _capture.Arm("F1"); // capture armed on the same key the script is bound to

        _coordinator.HandleTriggerKey(Vk("F1"));

        // The press was consumed by capture; the script must not have toggled on.
        Assert.False(IsRunning(s.Id));
    }

    [Fact]
    public void UnarmedCapture_NormalToggleStillWorks()
    {
        var s = Script("s", "F1");
        _coordinator.RegisterScript(s);
        // capture is idle (never armed)

        _coordinator.HandleTriggerKey(Vk("F1"));

        Assert.True(IsRunning(s.Id));
    }
}
