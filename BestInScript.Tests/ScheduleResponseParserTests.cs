using System.Text.Json;
using BestInScript.API.Engine;
using BestInScript.API.Models;

namespace BestInScript.Tests;

/// <summary>
/// Pins schedule parsing for both source shapes: the devalue-encoded Nuxt payload
/// server-rendered into helltides.com/schedule (the default source, since the JSON
/// API sits behind a Cloudflare challenge) and the original /api/schedule JSON.
/// The page fixture mirrors the live page's payload structure, trimmed to a few events.
/// </summary>
public class ScheduleResponseParserTests
{
    private const string SchedulePage = """
        <!DOCTYPE html><html lang="en"><head><title>Event Schedule | Helltides.com</title></head><body>
        <div id="__nuxt"></div>
        <script type="application/json" data-nuxt-data="nuxt-app" data-ssr="true" id="__NUXT_DATA__">[
        ["ShallowReactive",1],
        {"data":2,"state":30,"once":32,"serverRendered":16},
        ["ShallowReactive",3],
        {"$ixs74Qcu6A":4},
        {"world_boss":5,"legion":20,"helltide":25},
        [6],
        {"id":7,"timestamp":7,"boss":8,"type":9,"startTime":10,"zone":11},
        1789614000,
        "Avarice",
        "world_boss",
        "2026-09-17T03:00:00.000Z",
        [12,17],
        {"id":13,"name":14,"isWhisper":15,"boss":8},
        "kehjistan",
        "Kehjistan",
        false,
        true,
        {"id":18,"name":19,"isWhisper":15,"boss":8},
        "skovos",
        "Skovos",
        [21],
        {"id":22,"timestamp":22,"type":23},
        1789607700,
        "legion",
        "helltide",
        [26,28],
        {"id":27,"timestamp":27,"type":24},
        1789603200,
        {"id":29,"timestamp":29,"type":24},
        1789606800,
        ["Reactive",31],
        {"HelltideStore":33,"fetchedAt":35},
        ["Set"],
        {"helltide":34,"markers":36},
        null,
        ["Date","2026-09-17T00:59:39.950Z"],
        []
        ]</script>
        <script>window.__NUXT__={};window.__NUXT__.config={public:{}}</script>
        </body></html>
        """;

    [Fact]
    public void SchedulePage_ParsesEmbeddedPayload()
    {
        var sched = ScheduleResponseParser.Parse(SchedulePage);

        Assert.NotNull(sched);
        var boss = Assert.Single(sched!.WorldBoss);
        Assert.Equal(1789614000, boss.Timestamp);
        Assert.Equal("Avarice", boss.Boss);
        Assert.Equal(new[] { "Kehjistan", "Skovos" }, boss.Zone!.Select(z => z.Name));
        Assert.Equal(1789607700, Assert.Single(sched.Legion).Timestamp);
        Assert.Equal(new long[] { 1789603200, 1789606800 }, sched.Helltide.Select(h => h.Timestamp));
    }

    [Fact]
    public void SchedulePage_FeedsCalculator()
    {
        // 00:56 UTC: inside the Helltide lock gap, boss and legion still ahead.
        var now = DateTimeOffset.FromUnixTimeSeconds(1789606583);

        var snap = EventScheduleCalculator.GetSnapshot(ScheduleResponseParser.Parse(SchedulePage), now);

        Assert.True(snap.HasData);
        Assert.Equal(new WorldBossNext("Avarice", "Kehjistan", 1789614000), snap.Boss);
        Assert.Equal(1789607700, snap.LegionStartUnix);
        Assert.False(snap.Helltide!.Active);
        Assert.Equal(1789606800, snap.Helltide.TargetUnix);
    }

    [Fact]
    public void ApiJson_StillParses()
    {
        const string json = """
            {"world_boss":[{"timestamp":1789614000,"boss":"Ashava","zone":[{"name":"Scosglen","isWhisper":false}]}],
             "legion":[{"timestamp":1789607700}],
             "helltide":[{"timestamp":1789606800}]}
            """;

        var sched = ScheduleResponseParser.Parse(json);

        Assert.NotNull(sched);
        Assert.Equal("Scosglen", Assert.Single(sched!.WorldBoss).Zone![0].Name);
        Assert.Equal(1789607700, Assert.Single(sched.Legion).Timestamp);
        Assert.Equal(1789606800, Assert.Single(sched.Helltide).Timestamp);
    }

    [Fact]
    public void CloudflareChallengePage_ReturnsNull()
    {
        const string html = """
            <!DOCTYPE html><html lang="en-US"><head><title>Just a moment...</title></head>
            <body><script nonce="x">(function(){window._cf_chl_opt={};})();</script></body></html>
            """;

        Assert.Null(ScheduleResponseParser.Parse(html));
    }

    [Fact]
    public void PayloadWithoutSchedule_ReturnsNull()
    {
        // The homepage's shape: stores (one even has a "helltide" key) but no schedule fetch.
        const string html = """
            <script type="application/json" id="__NUXT_DATA__">[["ShallowReactive",1],{"pinia":2},["Reactive",3],{"HelltideStore":4},{"helltide":5,"active":6},null,false]</script>
            """;

        Assert.Null(ScheduleResponseParser.Parse(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyBody_ReturnsNull(string? body)
    {
        Assert.Null(ScheduleResponseParser.Parse(body));
    }

    [Fact]
    public void MalformedPayload_Throws()
    {
        const string html = """<script id="__NUXT_DATA__">[{"world_boss":1},[</script>""";

        Assert.ThrowsAny<JsonException>(() => ScheduleResponseParser.Parse(html));
    }

    [Fact]
    public void DevalueUndefined_KeepsModelDefaults()
    {
        // -1 is devalue's "undefined" sentinel, not an index.
        const string html = """
            <script id="__NUXT_DATA__">[{"world_boss":1,"legion":-1,"helltide":2},[3],[],{"timestamp":4,"boss":-1},1789614000]</script>
            """;

        var sched = ScheduleResponseParser.Parse(html);

        Assert.NotNull(sched);
        Assert.Null(Assert.Single(sched!.WorldBoss).Boss);
        Assert.Empty(sched.Legion);
        Assert.Empty(sched.Helltide);
    }

    [Fact]
    public void SelfReferencingPayload_Terminates()
    {
        // world_boss holds the container itself; devalue permits cycles.
        const string html = """<script id="__NUXT_DATA__">[{"world_boss":1},[0]]</script>""";

        var sched = ScheduleResponseParser.Parse(html);

        Assert.NotNull(sched);
        Assert.Equal(0, Assert.Single(sched!.WorldBoss).Timestamp);
    }
}
