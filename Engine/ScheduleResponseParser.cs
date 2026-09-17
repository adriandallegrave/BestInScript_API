using System.Text.Json;
using System.Text.Json.Nodes;
using BestInScript.API.Models;

namespace BestInScript.API.Engine
{
    /// <summary>
    /// Turns the schedule source's response body into an <see cref="EventSchedule"/>.
    /// Two shapes are accepted:
    /// <list type="bullet">
    ///   <item>a JSON object — the original <c>helltides.com/api/schedule</c> response;</item>
    ///   <item>an HTML page carrying a Nuxt SSR payload — <c>helltides.com/schedule</c>.</item>
    /// </list>
    /// The JSON API now sits behind a Cloudflare bot challenge (403 for any
    /// non-browser client), but the public schedule page server-renders the exact
    /// same data into its <c>__NUXT_DATA__</c> script, so that page is the default
    /// source. Kept static and network-free so it can be unit-tested without HTTP.
    /// </summary>
    public static class ScheduleResponseParser
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string NuxtDataMarker = "id=\"__NUXT_DATA__\"";

        // devalue payloads may contain cycles. A schedule is ~4 levels deep, so the
        // depth cap only bites on pathological input (and prevents a stack overflow,
        // which can't be caught); the node budget stops shared refs from fanning out.
        private const int MaxDepth = 32;
        private const int MaxNodes = 50_000;

        /// <summary>
        /// The schedule in <paramref name="body"/>, or null when it holds none (e.g.
        /// a challenge page, or the site moved its data). Throws
        /// <see cref="JsonException"/> when the JSON itself is malformed.
        /// </summary>
        public static EventSchedule? Parse(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;

            if (body.AsSpan().TrimStart().StartsWith("{"))
                return JsonSerializer.Deserialize<EventSchedule>(body, JsonOpts);

            string? payload = ExtractNuxtData(body);
            return payload == null ? null : FromNuxtPayload(payload);
        }

        private static string? ExtractNuxtData(string html)
        {
            int marker = html.IndexOf(NuxtDataMarker, StringComparison.Ordinal);
            if (marker < 0) return null;
            int start = html.IndexOf('>', marker);
            if (start < 0) return null;
            int end = html.IndexOf("</script>", start, StringComparison.OrdinalIgnoreCase);
            return end < 0 ? null : html[(start + 1)..end];
        }

        /// <summary>
        /// A Nuxt payload is devalue-encoded: one flat JSON array where objects and
        /// arrays hold integer indexes into that array instead of values. Find the
        /// object with a <c>world_boss</c> key (the schedule fetch result), rebuild it
        /// as a plain JSON tree, and deserialize that as if it came from the API.
        /// </summary>
        private static EventSchedule? FromNuxtPayload(string payload)
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var values = doc.RootElement.EnumerateArray().ToArray();
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].ValueKind == JsonValueKind.Object &&
                    values[i].TryGetProperty("world_boss", out _))
                {
                    int budget = MaxNodes;
                    return Hydrate(values, i, 0, ref budget)?.Deserialize<EventSchedule>(JsonOpts);
                }
            }
            return null;
        }

        private static JsonNode? Hydrate(JsonElement[] values, int index, int depth, ref int budget)
        {
            // Negative indexes are devalue sentinels (undefined, NaN, ±Infinity, -0).
            if (index < 0 || index >= values.Length || depth > MaxDepth) return null;
            if (--budget < 0) throw new JsonException("Schedule payload exceeds the node budget.");

            var v = values[index];
            switch (v.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new JsonObject();
                    foreach (var p in v.EnumerateObject())
                    {
                        // A sentinel means undefined: omit the key (as JSON.stringify
                        // would) so the model's default stands instead of a null.
                        if (p.Value.ValueKind == JsonValueKind.Number &&
                            p.Value.TryGetInt32(out int r) && r < 0) continue;
                        obj[p.Name] = Deref(values, p.Value, depth, ref budget);
                    }
                    return obj;

                case JsonValueKind.Array:
                    // ["Type", ...args] is a devalue special form; a plain array is all refs.
                    if (v.GetArrayLength() > 0 && v[0].ValueKind == JsonValueKind.String)
                    {
                        return v[0].GetString() is "Reactive" or "ShallowReactive" or "Ref" or "ShallowRef"
                               && v.GetArrayLength() > 1
                            ? Deref(values, v[1], depth, ref budget)
                            : null; // Date, Set, Map, … — nothing the schedule uses
                    }
                    var arr = new JsonArray();
                    foreach (var item in v.EnumerateArray())
                        arr.Add(Deref(values, item, depth, ref budget));
                    return arr;

                default:
                    return JsonNode.Parse(v.GetRawText()); // string / number / bool / null
            }
        }

        private static JsonNode? Deref(JsonElement[] values, JsonElement reference, int depth, ref int budget) =>
            reference.ValueKind == JsonValueKind.Number && reference.TryGetInt32(out int i)
                ? Hydrate(values, i, depth + 1, ref budget)
                : null;
    }
}
