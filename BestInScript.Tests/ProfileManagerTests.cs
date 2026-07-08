using BestInScript.API.Models;
using BestInScript.API.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestInScript.Tests;

/// <summary>
/// Covers profile lifecycle + the store-repointing contract: creation, activation
/// (which rebinds the real repos), rename, delete guards, copy-from-current, the
/// legacy-file migration into a Default profile, and pointer persistence.
/// </summary>
public sealed class ProfileManagerTests : IDisposable
{
    private readonly string _dir;
    private readonly IConfiguration _config;

    public ProfileManagerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bis-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BestInScript:DataDirectory"] = _dir
            })
            .Build();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private (ScriptRepository scripts, PresetRepository presets) Repos()
        => (new ScriptRepository(_config, NullLogger<ScriptRepository>.Instance),
            new PresetRepository(_config, NullLogger<PresetRepository>.Instance));

    private ProfileManager Manager(params IProfileScopedStore[] stores)
        => new(_config, stores, NullLogger<ProfileManager>.Instance);

    private string ProfilePath(string profile, string file)
        => Path.Combine(_dir, "profiles", profile, file);

    private static ScriptConfig NewScript(string name = "s")
        => new() { Id = Guid.NewGuid(), Name = name, TriggerKey = "F1" };

    // ── Fresh start & migration ──────────────────────────────────────────────

    [Fact]
    public void FreshStart_CreatesDefaultAndPointsStoresAtIt()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);

        Assert.Equal(ProfileManager.DefaultProfileName, mgr.Active);
        Assert.Equal(new[] { "Default" }, mgr.List());
        Assert.Equal(ProfilePath("Default", "scripts.json"), s.FilePath);
        Assert.Equal(ProfilePath("Default", "presets.json"), p.FilePath);
    }

    [Fact]
    public void LegacyFiles_MigratedIntoDefaultProfile()
    {
        var (s, p) = Repos();
        // Pre-profiles layout: repos initially point at <baseDir>/scripts.json.
        s.Save(NewScript("legacy"));
        Assert.True(File.Exists(Path.Combine(_dir, "scripts.json")));

        var mgr = Manager(s, p);

        Assert.Equal("Default", mgr.Active);
        Assert.False(File.Exists(Path.Combine(_dir, "scripts.json")));      // moved out of base dir
        Assert.True(File.Exists(ProfilePath("Default", "scripts.json")));   // into the profile
        Assert.Single(s.GetAll());                                          // content preserved
        Assert.Equal("legacy", s.GetAll()[0].Name);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_Empty_AddsProfileWithoutSwitching()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);

        Assert.Null(mgr.Create("S6", copyFromCurrent: false));

        Assert.Contains("S6", mgr.List());
        Assert.Equal("Default", mgr.Active);                 // create does not switch
        Assert.False(File.Exists(ProfilePath("S6", "scripts.json"))); // empty: no file yet
    }

    [Fact]
    public void Create_CopyFromCurrent_DuplicatesActiveFiles()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        s.Save(NewScript("carried"));   // saved into the active (Default) profile

        Assert.Null(mgr.Create("S6", copyFromCurrent: true));

        Assert.True(File.Exists(ProfilePath("S6", "scripts.json")));
        var copied = new ScriptRepository(_config, NullLogger<ScriptRepository>.Instance);
        copied.Rebind(ProfilePath("S6", "scripts.json"));
        Assert.Single(copied.GetAll());
        Assert.Equal("carried", copied.GetAll()[0].Name);
    }

    [Fact]
    public void Create_DuplicateName_Rejected()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        Assert.NotNull(mgr.Create("Default", copyFromCurrent: false));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_InvalidName_Rejected(string name)
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        Assert.NotNull(mgr.Create(name, copyFromCurrent: false));
    }

    // ── Activate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Activate_RepointsStores_SoWritesLandInNewProfile()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        mgr.Create("S6", copyFromCurrent: false);

        mgr.Activate("S6");

        Assert.Equal("S6", mgr.Active);
        Assert.Equal(ProfilePath("S6", "scripts.json"), s.FilePath);
        Assert.Equal(ProfilePath("S6", "presets.json"), p.FilePath);

        s.Save(NewScript("in-s6"));
        Assert.True(File.Exists(ProfilePath("S6", "scripts.json")));
        Assert.False(File.Exists(ProfilePath("Default", "scripts.json"))); // Default untouched
    }

    [Fact]
    public void Activate_Missing_Throws()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        Assert.Throws<InvalidOperationException>(() => mgr.Activate("nope"));
    }

    [Fact]
    public void ActivePointer_PersistsAcrossReconstruction()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        mgr.Create("S6", copyFromCurrent: false);
        mgr.Activate("S6");

        // Simulate a restart: brand-new stores + manager reading the same base dir.
        var (s2, p2) = Repos();
        var mgr2 = Manager(s2, p2);

        Assert.Equal("S6", mgr2.Active);
        Assert.Equal(ProfilePath("S6", "scripts.json"), s2.FilePath);
    }

    // ── Rename ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_ActiveProfile_UpdatesPointerAndStores()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);

        Assert.Null(mgr.Rename("Default", "Main"));

        Assert.Equal("Main", mgr.Active);
        Assert.Contains("Main", mgr.List());
        Assert.DoesNotContain("Default", mgr.List());
        Assert.Equal(ProfilePath("Main", "scripts.json"), s.FilePath);
    }

    [Fact]
    public void Rename_NonActive_LeavesActiveUntouched()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        mgr.Create("S6", copyFromCurrent: false);

        Assert.Null(mgr.Rename("S6", "S7"));

        Assert.Equal("Default", mgr.Active);
        Assert.Contains("S7", mgr.List());
        Assert.DoesNotContain("S6", mgr.List());
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ActiveProfile_Blocked()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        mgr.Create("S6", copyFromCurrent: false);   // so Default isn't the last one either

        Assert.NotNull(mgr.Delete("Default"));       // active — refused
        Assert.Contains("Default", mgr.List());
    }

    [Fact]
    public void Delete_LastProfile_Blocked()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        // Only Default exists and it is active — both guards apply.
        Assert.NotNull(mgr.Delete("Default"));
    }

    [Fact]
    public void Delete_NonActive_RemovesDirectory()
    {
        var (s, p) = Repos();
        var mgr = Manager(s, p);
        mgr.Create("S6", copyFromCurrent: false);

        Assert.Null(mgr.Delete("S6"));

        Assert.DoesNotContain("S6", mgr.List());
        Assert.False(Directory.Exists(Path.Combine(_dir, "profiles", "S6")));
    }
}
