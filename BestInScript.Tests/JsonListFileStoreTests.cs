using BestInScript.API.Models;
using BestInScript.API.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestInScript.Tests;

/// <summary>
/// Covers the shared JSON list store through a real repository, focusing on the write
/// path's backup hook (BACKLOG 3.4). The store treats a file it cannot parse as an empty
/// list and overwrites it, so without a pre-write snapshot one bad byte silently costs the
/// user every script — that scenario is asserted here directly.
/// </summary>
public sealed class JsonListFileStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly IConfiguration _config;

    public JsonListFileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bis-store-" + Guid.NewGuid().ToString("N"));
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

    private ConfigSnapshotService _snapshots = null!;

    private ScriptRepository Repo()
    {
        _snapshots = new ConfigSnapshotService(_config, NullLogger<ConfigSnapshotService>.Instance);
        return new ScriptRepository(_config, NullLogger<ScriptRepository>.Instance, _snapshots);
    }

    private static ScriptConfig NewScript(string name)
        => new() { Id = Guid.NewGuid(), Name = name, TriggerKey = "F1" };

    private string[] SnapFiles()
    {
        var dir = Path.Combine(_dir, ConfigSnapshotService.DirectoryName);
        return Directory.Exists(dir) ? Directory.GetFiles(dir) : [];
    }

    [Fact]
    public void FirstSave_WritesNoSnapshot()
    {
        // There is no previous version to keep.
        Repo().Save(NewScript("first"));
        Assert.Empty(SnapFiles());
    }

    [Fact]
    public void SecondSave_SnapshotsThePreviousVersion()
    {
        var repo = Repo();
        repo.Save(NewScript("v1"));
        repo.Save(NewScript("v2"));

        var snap = Assert.Single(SnapFiles());
        Assert.Contains("v1", File.ReadAllText(snap));
        Assert.DoesNotContain("v2", File.ReadAllText(snap));
    }

    [Fact]
    public void Delete_SnapshotsThePreviousVersion()
    {
        var repo = Repo();
        var script = NewScript("doomed");
        repo.Save(script);

        Assert.True(repo.Delete(script.Id));

        Assert.Empty(repo.GetAll());
        Assert.Contains("doomed", File.ReadAllText(Assert.Single(SnapFiles())));
    }

    [Fact]
    public void Delete_UnknownId_WritesNothingAndSnapshotsNothing()
    {
        var repo = Repo();
        repo.Save(NewScript("kept"));

        Assert.False(repo.Delete(Guid.NewGuid()));

        Assert.Empty(SnapFiles());
        Assert.Single(repo.GetAll());
    }

    [Fact]
    public void CorruptFile_IsOverwrittenButRecoverable()
    {
        // The data-loss scenario the feature exists for: the store cannot parse the file,
        // silently treats it as empty, and the next save wipes it. The snapshot is the
        // only copy of what the user actually had.
        var repo = Repo();
        File.WriteAllText(repo.FilePath, """[ { "Name": "hand-edited, broken """);

        repo.Save(NewScript("replacement"));

        // The wipe happened...
        Assert.Single(repo.GetAll());
        Assert.Equal("replacement", repo.GetAll()[0].Name);

        // ...and the original is still there to walk back to.
        var snapshot = Assert.Single(_snapshots.List(repo.FilePath));
        var archived = File.ReadAllText(
            Path.Combine(ConfigSnapshotService.SnapshotDirectory(repo.FilePath), snapshot.Id));
        Assert.Contains("hand-edited, broken", archived);
    }

    [Fact]
    public void Snapshots_FollowTheStoreOnRebind()
    {
        // Profile-scoped stores get repointed at runtime; the history has to move with them
        // rather than piling up in whichever directory the app started in.
        var repo = Repo();
        var other = Path.Combine(_dir, "profiles", "S6", "scripts.json");
        repo.Rebind(other);

        repo.Save(NewScript("v1"));
        repo.Save(NewScript("v2"));

        Assert.Empty(SnapFiles());   // not in the original directory
        Assert.Single(_snapshots.List(other));
    }
}
