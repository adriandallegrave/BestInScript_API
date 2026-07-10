using System.Net.Http;
using System.Text.Json;
using BestInScript.API.Engine;
using BestInScript.API.Models;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Fetches the Diablo 4 event schedule (world boss / helltide / legion) from a
    /// public community API ONCE at startup and caches it in memory. The overlay
    /// polls <see cref="GetSnapshot"/> to render live countdowns; all time math is
    /// delegated to <see cref="EventScheduleCalculator"/>.
    ///
    /// This is the app's only outbound network call. It is passive / informational
    /// (never contacts the game process) and never blocks startup — a failed fetch
    /// just means the overlay omits the event rows. One payload covers ~14 h of
    /// bosses and ~24 h of helltides/legions; if it fully drains during a very long
    /// session a single lazy re-fetch is triggered (throttled to 5 min).
    /// </summary>
    public sealed class EventScheduleService : IHostedService, IDisposable
    {
        private const string DefaultUrl = "https://helltides.com/api/schedule";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ILogger<EventScheduleService> _logger;
        private readonly HttpClient _http;
        private readonly string _url;
        private readonly bool _enabled;

        private volatile EventSchedule? _schedule;
        private int _fetching;                                   // 0/1 re-entrancy guard
        private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;

        public EventScheduleService(IConfiguration config, ILogger<EventScheduleService> logger)
        {
            _logger = logger;
            _url = config["BestInScript:ScheduleApiUrl"] is { Length: > 0 } u ? u : DefaultUrl;
            _enabled = config.GetValue("BestInScript:EventsEnabled", true);

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // Some hosts reject the default .NET UA; present a browser-like one.
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BestInScript");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("Event timers disabled (BestInScript:EventsEnabled=false).");
                return Task.CompletedTask;
            }

            // Fire-and-forget so the web host isn't blocked on the network.
            _ = FetchAsync();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Next event per type at <paramref name="now"/>. Triggers a one-shot,
        /// throttled lazy re-fetch if the cached schedule has fully drained.
        /// </summary>
        public EventSnapshot GetSnapshot(DateTimeOffset now)
        {
            if (!_enabled) return EventSnapshot.Empty;

            var snap = EventScheduleCalculator.GetSnapshot(_schedule, now);
            if (_schedule != null && !snap.HasData &&
                DateTimeOffset.UtcNow - _lastFetch > TimeSpan.FromMinutes(5))
            {
                _ = FetchAsync();
            }
            return snap;
        }

        private async Task FetchAsync()
        {
            if (Interlocked.Exchange(ref _fetching, 1) == 1) return;
            _lastFetch = DateTimeOffset.UtcNow;
            try
            {
                var json = await _http.GetStringAsync(_url);
                var sched = JsonSerializer.Deserialize<EventSchedule>(json, JsonOpts);
                if (sched != null)
                {
                    _schedule = sched;
                    _logger.LogInformation(
                        "Event schedule loaded: {Boss} bosses, {Legion} legions, {Helltide} helltides.",
                        sched.WorldBoss.Count, sched.Legion.Count, sched.Helltide.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch event schedule from {Url}; event rows hidden.", _url);
            }
            finally
            {
                Interlocked.Exchange(ref _fetching, 0);
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
