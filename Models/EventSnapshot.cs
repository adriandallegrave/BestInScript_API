namespace BestInScript.API.Models
{
    /// <summary>Next world-boss occurrence: boss name, spawn region, and start (unix seconds).</summary>
    public sealed record WorldBossNext(string Name, string Zone, long StartUnix);

    /// <summary>
    /// Current / next Helltide. When <see cref="Active"/> is true a Helltide is
    /// live now and <see cref="TargetUnix"/> is when it ENDS; when false a Helltide
    /// is not running (the ~5-minute lock or idle) and <see cref="TargetUnix"/> is
    /// the next START.
    /// </summary>
    public sealed record HelltideNext(bool Active, long TargetUnix);

    /// <summary>
    /// Computed "next event per type" the overlay renders. <see cref="HasData"/> is
    /// false until the schedule has been fetched, or if every cached event is in the
    /// past / the fetch failed — the overlay then simply omits the event rows.
    /// </summary>
    public sealed record EventSnapshot(
        bool HasData,
        WorldBossNext? Boss,
        long? LegionStartUnix,
        HelltideNext? Helltide)
    {
        public static readonly EventSnapshot Empty = new(false, null, null, null);
    }
}
