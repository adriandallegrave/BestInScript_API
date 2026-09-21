using BestInScript.API.Models;
using BestInScript.API.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BestInScript.Tests;

/// <summary>
/// Covers the card store's two halves: the JSON rows (CRUD, cycle ordering, rebind)
/// and the image side files that must stay in step with them.
/// </summary>
public sealed class BuildCardRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly IConfiguration _config;

    public BuildCardRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bis-cards-" + Guid.NewGuid().ToString("N"));
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

    private BuildCardRepository Repo()
        => new(_config, NullLogger<BuildCardRepository>.Instance, Snaps());

    private static Stream Bytes(params byte[] b) => new MemoryStream(b);

    private static BuildCard Card(string name, int order = 0)
        => new() { Id = Guid.NewGuid(), Name = name, Order = order };

    // ── Rows ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_Then_GetById_RoundTrips()
    {
        var repo = Repo();
        var card = Card("Endgame Paragon");
        card.Notes = "crit chance first";
        card.Scale = 1.5;

        repo.Save(card);

        var loaded = repo.GetById(card.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Endgame Paragon", loaded!.Name);
        Assert.Equal("crit chance first", loaded.Notes);
        Assert.Equal(1.5, loaded.Scale);
    }

    [Fact]
    public void GetAll_ReturnsCycleOrder_ByOrderThenName()
    {
        var repo = Repo();
        repo.Save(Card("zulu", order: 2));
        repo.Save(Card("bravo", order: 1));
        repo.Save(Card("alpha", order: 1));

        // Order ascending, ties broken by name — so the cycle key and the web list agree.
        Assert.Equal(["alpha", "bravo", "zulu"], repo.GetAll().Select(c => c.Name));
    }

    [Fact]
    public void Save_ExistingId_Updates_DoesNotDuplicate()
    {
        var repo = Repo();
        var card = Card("before");
        repo.Save(card);

        card.Name = "after";
        repo.Save(card);

        var all = repo.GetAll();
        Assert.Single(all);
        Assert.Equal("after", all[0].Name);
    }

    // ── Images ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveImageAsync_WritesIntoCardsSubdirectory_BesideTheJson()
    {
        var repo = Repo();
        var card = Card("Gear");

        card.ImageFileName = await repo.SaveImageAsync(card.Id, Bytes(1, 2, 3), ".png");
        repo.Save(card);

        var expected = Path.Combine(Path.GetDirectoryName(repo.FilePath)!, "cards", card.ImageFileName);
        Assert.True(File.Exists(expected));
        Assert.Equal(expected, repo.ImagePath(card));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(expected));
    }

    [Fact]
    public async Task SaveImageAsync_IgnoresUnsupportedExtension_FallsBackToPng()
    {
        var repo = Repo();
        var card = Card("Gear");

        var name = await repo.SaveImageAsync(card.Id, Bytes(1), ".exe");

        Assert.EndsWith(".png", name);
    }

    [Fact]
    public async Task SaveImageAsync_ReplacingWithDifferentFormat_RemovesTheOldFile()
    {
        var repo = Repo();
        var card = Card("Gear");

        var oldName = await repo.SaveImageAsync(card.Id, Bytes(1), ".png");
        var newName = await repo.SaveImageAsync(card.Id, Bytes(2), ".jpg");

        var dir = Path.Combine(Path.GetDirectoryName(repo.FilePath)!, "cards");
        Assert.NotEqual(oldName, newName);
        Assert.False(File.Exists(Path.Combine(dir, oldName)));   // no stranded orphan
        Assert.True(File.Exists(Path.Combine(dir, newName)));
    }

    [Fact]
    public void ImagePath_NoImage_ReturnsNull()
        => Assert.Null(Repo().ImagePath(Card("no image")));

    [Fact]
    public void ImagePath_FileMissing_ReturnsNull()
    {
        var repo = Repo();
        var card = Card("stale");
        card.ImageFileName = "does-not-exist.png";

        Assert.Null(repo.ImagePath(card));
    }

    [Fact]
    public async Task ImagePath_StripsPathTraversal_FromAHandEditedJson()
    {
        var repo = Repo();
        var card = Card("evil");

        // A file the panel must never render, sitting outside the profile's card dir.
        var outside = Path.Combine(_dir, "secret.png");
        await File.WriteAllBytesAsync(outside, [9]);
        card.ImageFileName = @"..\..\secret.png";

        // The name is reduced to its bare form, which does not exist under cards/.
        Assert.Null(repo.ImagePath(card));
        Assert.True(File.Exists(outside));  // still there — we simply refused to point at it
    }

    [Fact]
    public async Task Delete_RemovesRowAndImageFile()
    {
        var repo = Repo();
        var card = Card("Gear");
        card.ImageFileName = await repo.SaveImageAsync(card.Id, Bytes(1), ".png");
        repo.Save(card);

        var path = repo.ImagePath(card);
        Assert.NotNull(path);

        Assert.True(repo.Delete(card.Id));

        Assert.Empty(repo.GetAll());
        Assert.False(File.Exists(path!));   // image must not outlive its row
    }

    [Fact]
    public void Delete_UnknownId_ReturnsFalse()
        => Assert.False(Repo().Delete(Guid.NewGuid()));

    // ── Profile scoping ──────────────────────────────────────────────────────

    [Fact]
    public void ProfileScopedStore_ExposesJsonNameAndImageSubdirectory()
    {
        IProfileScopedStore repo = Repo();

        Assert.Equal("build-cards.json", repo.ProfileFileName);
        Assert.Equal("cards", repo.ProfileSubdirectory);
    }

    [Fact]
    public async Task Rebind_MovesBothJsonAndImageDirectory_ToTheNewProfile()
    {
        var repo = Repo();
        var card = Card("in default");
        card.ImageFileName = await repo.SaveImageAsync(card.Id, Bytes(1), ".png");
        repo.Save(card);

        var other = Path.Combine(_dir, "profiles", "S6", "build-cards.json");
        repo.Rebind(other);

        Assert.Empty(repo.GetAll());   // new profile starts clean
        Assert.Equal(Path.Combine(_dir, "profiles", "S6", "cards"), repo.ImageDirectory);
    }
}
