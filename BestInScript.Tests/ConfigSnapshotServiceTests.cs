using BestInScript.API.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestInScript.Tests;

/// <summary>
/// Covers the rotating config backup (BACKLOG 3.4). Two properties carry the whole
/// feature and are easy to break: a snapshot must hold the contents from BEFORE the
/// write (so a corrupt-file wipe stays recoverable), and the newest-first ordering the
/// retention prune depends on must survive same-millisecond writes. Restore also takes a
/// client-supplied file name, so the path validation is security-relevant.
/// </summary>
public sealed class ConfigSnapshotServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public ConfigSnapshotServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bis-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "scripts.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static ConfigSnapshotService Service(int? keep = null)
    {
        var entries = new Dictionary<string, string?>();
        if (keep is not null) entries["BestInScript:SnapshotCount"] = keep.Value.ToString();

        return new ConfigSnapshotService(
            new ConfigurationBuilder().AddInMemoryCollection(entries).Build(),
            NullLogger<ConfigSnapshotService>.Instance);
    }

    private string SnapDir => Path.Combine(_dir, ConfigSnapshotService.DirectoryName);

    private string[] SnapFiles()
        => Directory.Exists(SnapDir) ? Directory.GetFiles(SnapDir) : [];

    // ── Capture ──────────────────────────────────────────────────────────────

    [Fact]
    public void Capture_ArchivesTheContentsFromBeforeTheWrite()
    {
        // The headline guarantee: what lands in .snapshots is the OLD file, so the new
        // (possibly bad) content is what you can walk back from.
        var svc = Service();
        File.WriteAllText(_file, "VERSION-1");

        svc.Capture(_file);
        File.WriteAllText(_file, "VERSION-2");

        var snap = Assert.Single(SnapFiles());
        Assert.Equal("VERSION-1", File.ReadAllText(snap));
        Assert.Equal("VERSION-2", File.ReadAllText(_file));
    }

    [Fact]
    public void Capture_NamesSnapshotsAfterTheFileStem()
    {
        var svc = Service();
        File.WriteAllText(_file, "a");
        var other = Path.Combine(_dir, "presets.json");
        File.WriteAllText(other, "b");

        svc.Capture(_file);
        svc.Capture(other);

        // One shared directory, but each file's history is independent.
        Assert.Single(svc.List(_file));
        Assert.Single(svc.List(other));
        Assert.StartsWith("scripts--", svc.List(_file)[0].Id);
        Assert.StartsWith("presets--", svc.List(other)[0].Id);
    }

    [Fact]
    public void Capture_MissingFile_WritesNothing()
    {
        // Nothing to undo before the very first save.
        Service().Capture(_file);
        Assert.Empty(SnapFiles());
    }

    [Fact]
    public void Capture_UnchangedContent_IsSkipped()
    {
        // A no-op PUT or a burst of overlay drag commits must not flush the history
        // with identical copies.
        var svc = Service();
        File.WriteAllText(_file, "same");

        svc.Capture(_file);
        svc.Capture(_file);
        svc.Capture(_file);

        Assert.Single(SnapFiles());
    }

    [Fact]
    public void Capture_KeepsOnlyTheNewestN_DroppingTheOldest()
    {
        var svc = Service(keep: 3);

        for (var i = 1; i <= 6; i++)
        {
            File.WriteAllText(_file, $"v{i}");
            svc.Capture(_file);
        }

        var kept = svc.List(_file).Select(s => File.ReadAllText(Path.Combine(SnapDir, s.Id))).ToList();
        Assert.Equal(3, kept.Count);
        Assert.Equal(["v6", "v5", "v4"], kept);   // newest first, v1–v3 pruned
    }

    [Fact]
    public void Capture_SameMillisecondWrites_StayInOrder()
    {
        // Ordinal filename sort defines "newest", so a same-millisecond sequence suffix
        // must be fixed-width — otherwise '-' sorts before '.' and the order inverts.
        var svc = Service(keep: 20);

        for (var i = 1; i <= 8; i++)
        {
            File.WriteAllText(_file, $"v{i}");
            svc.Capture(_file);
        }

        var kept = svc.List(_file).Select(s => File.ReadAllText(Path.Combine(SnapDir, s.Id))).ToList();
        Assert.Equal(["v8", "v7", "v6", "v5", "v4", "v3", "v2", "v1"], kept);
    }

    [Fact]
    public void Capture_SnapshotCountZero_DisablesTheFeature()
    {
        var svc = Service(keep: 0);
        File.WriteAllText(_file, "a");

        svc.Capture(_file);

        Assert.Equal(0, svc.Keep);
        Assert.False(Directory.Exists(SnapDir));
    }

    [Fact]
    public void Keep_DefaultsToTen()
        => Assert.Equal(10, Service().Keep);

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public void List_NoSnapshots_IsEmpty()
        => Assert.Empty(Service().List(_file));

    [Fact]
    public void List_ReportsStampAndSize()
    {
        var svc = Service();
        File.WriteAllText(_file, "hello");
        var before = DateTime.UtcNow.AddSeconds(-5);

        svc.Capture(_file);

        var info = Assert.Single(svc.List(_file));
        Assert.InRange(info.TakenAtUtc, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(new FileInfo(_file).Length, info.SizeBytes);
    }

    // ── Restore ──────────────────────────────────────────────────────────────

    [Fact]
    public void Restore_PutsTheArchivedContentBack()
    {
        var svc = Service();
        File.WriteAllText(_file, "good");
        svc.Capture(_file);
        File.WriteAllText(_file, "ruined");

        var id = svc.List(_file)[0].Id;
        Assert.Null(svc.Restore(_file, id));

        Assert.Equal("good", File.ReadAllText(_file));
    }

    [Fact]
    public void Restore_ArchivesTheCurrentFileFirst_SoItIsUndoable()
    {
        var svc = Service();
        File.WriteAllText(_file, "good");
        svc.Capture(_file);
        File.WriteAllText(_file, "ruined");

        svc.Restore(_file, svc.List(_file)[0].Id);

        // Two snapshots now: the original, plus the state we just restored away from.
        var contents = svc.List(_file)
            .Select(s => File.ReadAllText(Path.Combine(SnapDir, s.Id)))
            .ToList();
        Assert.Equal(["ruined", "good"], contents);
    }

    [Fact]
    public void Restore_UnknownId_ReturnsErrorAndLeavesTheFileAlone()
    {
        var svc = Service();
        File.WriteAllText(_file, "live");

        var error = svc.Restore(_file, "scripts--20200101-000000-000-00.json");

        Assert.NotNull(error);
        Assert.Contains("no longer exists", error);
        Assert.Equal("live", File.ReadAllText(_file));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"..\..\evil.json")]
    [InlineData("../../evil.json")]
    [InlineData(@"C:\Windows\System32\evil.json")]
    [InlineData("presets--20200101-000000-000-00.json")]   // another file's history
    [InlineData("scripts--20200101-000000-000-00.txt")]    // not a snapshot
    [InlineData("scripts.json")]                            // the live file itself
    public void Restore_RejectsAnythingThatIsNotThisFilesSnapshotName(string id)
    {
        var svc = Service();
        File.WriteAllText(_file, "live");

        Assert.NotNull(svc.Restore(_file, id));
        Assert.Equal("live", File.ReadAllText(_file));
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    [Fact]
    public void SnapshotDirectory_SitsBesideTheFile()
        => Assert.Equal(
            Path.Combine(_dir, ".snapshots"),
            ConfigSnapshotService.SnapshotDirectory(_file));
}
