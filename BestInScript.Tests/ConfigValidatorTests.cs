using BestInScript.API.Models;
using BestInScript.API.Persistence;
using BestInScript.API.Services;

namespace BestInScript.Tests;

/// <summary>
/// Covers the per-entry overlay-style validation added in 1.7.0. Uses empty
/// stub repositories so trigger-collision / member-reference checks pass and
/// only the overlay-color / overlay-icon rules are exercised.
/// </summary>
public class ConfigValidatorTests
{
    private sealed class EmptyScriptRepo : IScriptRepository
    {
        public List<ScriptConfig> GetAll() => new();
        public ScriptConfig? GetById(Guid id) => null;
        public ScriptConfig Save(ScriptConfig script) => script;
        public bool Delete(Guid id) => false;
    }

    private sealed class EmptyPresetRepo : IPresetRepository
    {
        public List<Preset> GetAll() => new();
        public Preset? GetById(Guid id) => null;
        public Preset Save(Preset preset) => preset;
        public bool Delete(Guid id) => false;
    }

    private static ConfigValidator NewValidator()
        => new(new EmptyScriptRepo(), new EmptyPresetRepo());

    private static ScriptConfig ValidScript() => new()
    {
        Name = "Test",
        TriggerKey = "3",
        Steps = new List<ScriptStep>()
    };

    private static Preset ValidPreset() => new()
    {
        Name = "Test",
        TriggerKey = "F1",
        ScriptIds = new List<Guid>()
    };

    // ── Color ───────────────────────────────────────────────────────────────

    [Fact]
    public void Script_NullColor_IsValid()
    {
        var s = ValidScript();
        s.OverlayColor = null;
        Assert.Null(NewValidator().ValidateScript(s));
    }

    [Fact]
    public void Script_ValidRgb_IsValid()
    {
        var s = ValidScript();
        s.OverlayColor = new[] { 255, 0, 128 };
        Assert.Null(NewValidator().ValidateScript(s));
    }

    [Theory]
    [InlineData(new[] { 0, 0 })]           // too short
    [InlineData(new[] { 0, 0, 0, 0 })]     // too long
    [InlineData(new[] { 300, 0, 0 })]      // above 255
    [InlineData(new[] { -1, 0, 0 })]       // below 0
    public void Script_BadColor_IsRejected(int[] color)
    {
        var s = ValidScript();
        s.OverlayColor = color;
        Assert.NotNull(NewValidator().ValidateScript(s));
    }

    [Fact]
    public void Preset_BadColor_IsRejected()
    {
        var p = ValidPreset();
        p.OverlayColor = new[] { 0, 999, 0 };
        Assert.NotNull(NewValidator().ValidatePreset(p));
    }

    [Fact]
    public void Preset_ValidRgb_IsValid()
    {
        var p = ValidPreset();
        p.OverlayColor = new[] { 10, 20, 30 };
        Assert.Null(NewValidator().ValidatePreset(p));
    }

    // ── Icon ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("⚔️")]
    [InlineData("12345678")]   // exactly 8 UTF-16 units
    public void Script_AcceptableIcon_IsValid(string? icon)
    {
        var s = ValidScript();
        s.OverlayIcon = icon;
        Assert.Null(NewValidator().ValidateScript(s));
    }

    [Fact]
    public void Script_OverlongIcon_IsRejected()
    {
        var s = ValidScript();
        s.OverlayIcon = "123456789";   // 9 chars
        Assert.NotNull(NewValidator().ValidateScript(s));
    }

    [Fact]
    public void Preset_OverlongIcon_IsRejected()
    {
        var p = ValidPreset();
        p.OverlayIcon = "123456789";
        Assert.NotNull(NewValidator().ValidatePreset(p));
    }
}
