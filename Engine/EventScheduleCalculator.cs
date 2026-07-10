using BestInScript.API.Models;

namespace BestInScript.API.Engine
{
    /// <summary>
    /// Pure time math over a cached <see cref="EventSchedule"/>: pick the next
    /// occurrence of each event type and format countdowns. Kept network-free and
    /// static so it can be unit-tested without touching HTTP (mirrors
    /// <see cref="PixelReadyEvaluator"/>).
    /// </summary>
    public static class EventScheduleCalculator
    {
        /// <summary>
        /// Minutes a Helltide is active before its ~5-minute lock. The API only
        /// gives hourly START times, so the live "ends in" window is derived from
        /// this. Adjust here if a season changes the Helltide cadence.
        /// </summary>
        public const int HelltideActiveMinutes = 55;

        /// <summary>Next event per type at <paramref name="now"/> from the cached schedule.</summary>
        public static EventSnapshot GetSnapshot(EventSchedule? sched, DateTimeOffset now)
        {
            if (sched == null) return EventSnapshot.Empty;

            long nowUnix = now.ToUnixTimeSeconds();

            var boss = NextBoss(sched.WorldBoss, nowUnix);
            long? legion = NextStart(sched.Legion, nowUnix);
            var helltide = NextHelltide(sched.Helltide, nowUnix);

            bool has = boss != null || legion != null || helltide != null;
            return new EventSnapshot(has, boss, legion, helltide);
        }

        private static WorldBossNext? NextBoss(List<EventEntry> entries, long nowUnix)
        {
            EventEntry? next = null;
            foreach (var e in entries)
                if (e.Timestamp > nowUnix && (next == null || e.Timestamp < next.Timestamp))
                    next = e;
            if (next == null) return null;

            string name = string.IsNullOrWhiteSpace(next.Boss) ? "World Boss" : next.Boss!;
            string zone = next.Zone is { Count: > 0 } ? next.Zone[0].Name : "";
            return new WorldBossNext(name, zone, next.Timestamp);
        }

        private static long? NextStart(List<EventEntry> entries, long nowUnix)
        {
            long? best = null;
            foreach (var e in entries)
                if (e.Timestamp > nowUnix && (best == null || e.Timestamp < best))
                    best = e.Timestamp;
            return best;
        }

        private static HelltideNext? NextHelltide(List<EventEntry> entries, long nowUnix)
        {
            long activeSecs = HelltideActiveMinutes * 60L;

            // Most recent start at or before now — is that Helltide still live?
            long? lastStart = null;
            foreach (var e in entries)
                if (e.Timestamp <= nowUnix && (lastStart == null || e.Timestamp > lastStart))
                    lastStart = e.Timestamp;

            if (lastStart != null && nowUnix < lastStart.Value + activeSecs)
                return new HelltideNext(true, lastStart.Value + activeSecs); // live → ends-in

            var next = NextStart(entries, nowUnix);
            return next == null ? null : new HelltideNext(false, next.Value); // lock / idle → starts-in
        }

        /// <summary>
        /// Format seconds-remaining until <paramref name="targetUnix"/> as
        /// <c>H:MM</c> (minute resolution, floored). Negatives clamp to <c>0:00</c>.
        /// </summary>
        public static string FormatRemaining(long targetUnix, long nowUnix)
        {
            long secs = targetUnix - nowUnix;
            if (secs < 0) secs = 0;
            long totalMin = secs / 60;
            long h = totalMin / 60;
            long m = totalMin % 60;
            return $"{h}:{m:D2}";
        }
    }
}
