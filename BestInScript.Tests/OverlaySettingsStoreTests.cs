using BestInScript.API.Models;
using BestInScript.API.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestInScript.Tests;

/// <summary>
/// Guards the settings store's manual deep <c>Clone</c>. Get() and the Changed event both
/// hand out clones, so a field missing from Clone persists to disk correctly and still
/// never reaches the live overlay — a silent failure the compiler cannot catch. These
/// tests make that failure loud.
/// </summary>
public sealed class OverlaySettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly IConfiguration _config;

    public OverlaySettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bis-ovs-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Real snapshot service (BACKLOG 3.4) — these stores require one. Writes land
    /// in a ".snapshots" folder inside the temp dir, cleaned up with it.</summary>
    private ConfigSnapshotService Snaps()
        => new(_config, NullLogger<ConfigSnapshotService>.Instance);

    private OverlaySettingsStore Store()
        => new(_config, NullLogger<OverlaySettingsStore>.Instance, Snaps());

    private static BuildPanelConfig FullyPopulatedPanel() => new()
    {
        Enabled = true,
        CycleHotkey = "F9",
        ScreenIndex = 1,
        Anchor = OverlayAnchor.Custom,
        Margin = 20,
        PositionX = 111,
        PositionY = 222,
        MaxWidth = 1200,
        MaxHeight = 800,
        Opacity = 0.75,
        FontSize = 15
    };

    private static void AssertPanelMatches(BuildPanelConfig expected, BuildPanelConfig actual)
    {
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.CycleHotkey, actual.CycleHotkey);
        Assert.Equal(expected.ScreenIndex, actual.ScreenIndex);
        Assert.Equal(expected.Anchor, actual.Anchor);
        Assert.Equal(expected.Margin, actual.Margin);
        Assert.Equal(expected.PositionX, actual.PositionX);
        Assert.Equal(expected.PositionY, actual.PositionY);
        Assert.Equal(expected.MaxWidth, actual.MaxWidth);
        Assert.Equal(expected.MaxHeight, actual.MaxHeight);
        Assert.Equal(expected.Opacity, actual.Opacity);
        Assert.Equal(expected.FontSize, actual.FontSize);
    }

    // ── Clone ────────────────────────────────────────────────────────────────

    [Fact]
    public void Get_ReturnsBuildPanel_EveryFieldCloned()
    {
        var store = Store();
        var panel = FullyPopulatedPanel();
        store.Save(new OverlaySettings { BuildPanel = panel });

        AssertPanelMatches(panel, store.Get().BuildPanel);
    }

    [Fact]
    public void Changed_PublishesBuildPanel_ToTheLiveOverlay()
    {
        var store = Store();
        var panel = FullyPopulatedPanel();

        OverlaySettings? published = null;
        store.Changed += s => published = s;
        store.Save(new OverlaySettings { BuildPanel = panel });

        Assert.NotNull(published);
        AssertPanelMatches(panel, published!.BuildPanel);
    }

    [Fact]
    public void Get_ReturnsDeepCopy_SoCallersCannotMutateStoredState()
    {
        var store = Store();
        store.Save(new OverlaySettings { BuildPanel = new BuildPanelConfig { CycleHotkey = "F9" } });

        var first = store.Get();
        first.BuildPanel.CycleHotkey = "F10";

        Assert.Equal("F9", store.Get().BuildPanel.CycleHotkey);
    }

    [Fact]
    public void Save_NullBuildPanel_FallsBackToDefaults()
    {
        var store = Store();
        store.Save(new OverlaySettings { BuildPanel = null! });

        Assert.NotNull(store.Get().BuildPanel);
        Assert.Null(store.Get().BuildPanel.CycleHotkey);   // dormant until the user binds a key
    }

    // ── Persistence & defaults ───────────────────────────────────────────────

    [Fact]
    public void BuildPanel_SurvivesReload()
    {
        var panel = FullyPopulatedPanel();
        Store().Save(new OverlaySettings { BuildPanel = panel });

        AssertPanelMatches(panel, Store().Get().BuildPanel);
    }

    [Fact]
    public void PreUpgradeFile_WithoutBuildPanel_LoadsWithDefaults()
    {
        // Additive-field contract: an overlay-settings.json written before this feature
        // must still load, with the panel dormant rather than the whole file rejected.
        File.WriteAllText(
            Path.Combine(_dir, "overlay-settings.json"),
            """{ "Enabled": true, "FontSize": 12, "StopAllHotkey": "1" }""");

        var s = Store().Get();

        Assert.Equal("1", s.StopAllHotkey);
        Assert.NotNull(s.BuildPanel);
        Assert.Null(s.BuildPanel.CycleHotkey);
        Assert.True(s.BuildPanel.Enabled);
    }

    // ── Clamping ─────────────────────────────────────────────────────────────

    [Fact]
    public void Save_ClampsBuildPanel_SoAPanelCannotBeInvisibleOrAbsurd()
    {
        var store = Store();
        store.Save(new OverlaySettings
        {
            BuildPanel = new BuildPanelConfig
            {
                Opacity = 5.0,
                FontSize = 200,
                Margin = -50,
                MaxWidth = 99999,
                MaxHeight = 1
            }
        });

        var p = store.Get().BuildPanel;
        Assert.Equal(1.00, p.Opacity);
        Assert.Equal(32, p.FontSize);
        Assert.Equal(0, p.Margin);
        Assert.Equal(4000, p.MaxWidth);
        Assert.Equal(100, p.MaxHeight);
    }

    // ── Snapshots & reload (BACKLOG 3.4) ─────────────────────────────────────

    [Fact]
    public void Save_SnapshotsThePreviousSettings()
    {
        // This PUT is a full replace with no merge, so a save that forgets a field wipes
        // it. The previous file is the only way back.
        var store = Store();
        store.Save(new OverlaySettings { StopAllHotkey = "F12", FontSize = 20 });
        store.Save(new OverlaySettings { StopAllHotkey = null });

        var snapshots = new ConfigSnapshotService(_config, NullLogger<ConfigSnapshotService>.Instance)
            .List(store.FilePath);

        var archived = File.ReadAllText(
            Path.Combine(ConfigSnapshotService.SnapshotDirectory(store.FilePath), snapshots[0].Id));
        Assert.Contains("F12", archived);
    }

    [Fact]
    public void Reload_PicksUpAnExternallyRewrittenFile_AndPublishesIt()
    {
        // The restore path rewrites the file underneath the store. Without Reload the
        // cached settings — and the live overlay + global hotkeys — would never hear about it.
        var store = Store();
        store.Save(new OverlaySettings { StopAllHotkey = "F12" });

        OverlaySettings? published = null;
        store.Changed += s => published = s;

        File.WriteAllText(store.FilePath, """{ "StopAllHotkey": "Pause", "FontSize": 19 }""");
        store.Reload();

        Assert.Equal("Pause", store.Get().StopAllHotkey);
        Assert.NotNull(published);
        Assert.Equal("Pause", published.StopAllHotkey);
        Assert.Equal(19, published.FontSize);
    }
}
