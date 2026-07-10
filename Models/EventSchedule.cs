using System.Text.Json.Serialization;

namespace BestInScript.API.Models
{
    /// <summary>
    /// Raw shape of the helltides.com <c>/api/schedule</c> response: three
    /// forward-looking arrays of upcoming events. <c>timestamp</c> values are
    /// unix SECONDS (UTC). Only the fields the overlay actually uses are modelled.
    /// </summary>
    public sealed class EventSchedule
    {
        // The world-boss key is snake_case; the case-insensitive reader can't
        // bridge the underscore, so it needs an explicit name.
        [JsonPropertyName("world_boss")]
        public List<EventEntry> WorldBoss { get; set; } = new();

        [JsonPropertyName("legion")]
        public List<EventEntry> Legion { get; set; } = new();

        [JsonPropertyName("helltide")]
        public List<EventEntry> Helltide { get; set; } = new();
    }

    /// <summary>
    /// One scheduled event. <see cref="Boss"/> / <see cref="Zone"/> are only
    /// populated for world bosses; legion and helltide carry just a timestamp.
    /// </summary>
    public sealed class EventEntry
    {
        /// <summary>Start time, unix seconds (UTC).</summary>
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("boss")]
        public string? Boss { get; set; }

        [JsonPropertyName("zone")]
        public List<EventZone>? Zone { get; set; }
    }

    /// <summary>A region a world boss spawns in.</summary>
    public sealed class EventZone
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("isWhisper")]
        public bool IsWhisper { get; set; }
    }
}
