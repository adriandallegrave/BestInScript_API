using System.Globalization;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// One archived copy of a config file, as surfaced to the web UI.
    /// </summary>
    public sealed class ConfigSnapshotInfo
    {
        /// <summary>Snapshot file name — the id the restore endpoint takes.</summary>
        public string Id { get; set; } = "";

        /// <summary>When the snapshot was taken, parsed back out of <see cref="Id"/>.</summary>
        public DateTime TakenAtUtc { get; set; }

        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// Rotating on-disk history for the JSON config files (BACKLOG 3.4).
    ///
    /// Every write funnels through <see cref="Capture"/> first, which copies the file's
    /// CURRENT bytes into a ".snapshots" directory beside it and prunes to the newest
    /// <see cref="Keep"/> copies. Capturing the *pre-write* bytes is the whole point: the
    /// failure this protects against is <see cref="JsonListFileStore{T}"/> treating a
    /// corrupt file as an empty list and overwriting it, so the archived copy has to be
    /// the original, not a faithful copy of the wipe.
    ///
    /// Deliberately path-based and profile-unaware: the snapshot directory is derived from
    /// the file's own directory, so profile-scoped files land in
    /// profiles/&lt;name&gt;/.snapshots/ and the global overlay settings land in
    /// &lt;base&gt;/.snapshots/ with no extra wiring. Three consequences of that placement,
    /// all intentional:
    ///   • ".snapshots" is never a direct child of "profiles/", which
    ///     <see cref="ProfileManager"/> enumerates — it would otherwise be listed as a profile.
    ///   • Profile rename (a directory move) and delete (recursive) carry it along for free.
    ///   • It is NOT a declared <see cref="IProfileScopedStore.ProfileSubdirectory"/>, so
    ///     "create profile from current" does not copy it — a new season profile starts with
    ///     clean history rather than inheriting the old one's.
    ///
    /// Snapshot failures are logged and swallowed. Losing a backup must never break a save.
    /// </summary>
    public sealed class ConfigSnapshotService
    {
        /// <summary>Directory name created beside each config file.</summary>
        public const string DirectoryName = ".snapshots";

        /// <summary>Fixed-width UTC stamp, so an ordinal filename sort is chronological.</summary>
        private const string StampFormat = "yyyyMMdd-HHmmss-fff";

        private const string Separator = "--";

        private readonly ILogger<ConfigSnapshotService> _logger;
        private readonly object _lock = new();

        public ConfigSnapshotService(IConfiguration config, ILogger<ConfigSnapshotService> logger)
        {
            _logger = logger;
            Keep = Math.Clamp(config.GetValue("BestInScript:SnapshotCount", 10), 0, 100);
            _logger.LogInformation(
                "Config snapshots {State} (keeping the last {Keep} copy/copies per file)",
                Keep == 0 ? "disabled" : "enabled", Keep);
        }

        /// <summary>How many snapshots are retained per file. 0 disables the feature.</summary>
        public int Keep { get; }

        /// <summary>Directory holding the snapshots for a given config file.</summary>
        public static string SnapshotDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            return string.IsNullOrEmpty(dir)
                ? DirectoryName
                : Path.Combine(dir, DirectoryName);
        }

        /// <summary>
        /// Archive the file's current contents, then prune to the newest <see cref="Keep"/>.
        /// No-op when snapshots are disabled, when the file doesn't exist yet (nothing to
        /// undo), or when its bytes are identical to the newest existing snapshot.
        /// </summary>
        public void Capture(string filePath)
        {
            if (Keep == 0 || !File.Exists(filePath)) return;

            try
            {
                lock (_lock)
                {
                    var dir = SnapshotDirectory(filePath);
                    Directory.CreateDirectory(dir);

                    var existing = SnapshotFiles(filePath, dir);

                    // Skip a write that didn't change anything — a no-op PUT or a burst of
                    // overlay drag commits shouldn't flush the history with identical copies.
                    if (existing.Count > 0 && SameContent(filePath, existing[0]))
                        return;

                    File.Copy(filePath, NextSnapshotPath(filePath, dir), overwrite: false);
                    PruneLocked(filePath, dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not snapshot '{Path}' before writing it", filePath);
            }
        }

        /// <summary>Snapshots for one config file, newest first.</summary>
        public IReadOnlyList<ConfigSnapshotInfo> List(string filePath)
        {
            try
            {
                lock (_lock)
                {
                    var dir = SnapshotDirectory(filePath);
                    return SnapshotFiles(filePath, dir)
                        .Select(p => new ConfigSnapshotInfo
                        {
                            Id = Path.GetFileName(p),
                            TakenAtUtc = ParseStamp(filePath, p),
                            SizeBytes = new FileInfo(p).Length
                        })
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not list snapshots for '{Path}'", filePath);
                return [];
            }
        }

        /// <summary>
        /// Copy a snapshot back over the live file. The file's current contents are captured
        /// first, so a restore is itself undoable.
        /// Returns null on success, or a human-readable error (house convention).
        /// </summary>
        public string? Restore(string filePath, string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotId))
                return "A snapshot id is required.";

            // No client-supplied string reaches the filesystem unchecked: the id has to be a
            // bare file name matching this file's own snapshot naming, never a path.
            if (Path.GetFileName(snapshotId) != snapshotId
                || !snapshotId.StartsWith(Prefix(filePath), StringComparison.OrdinalIgnoreCase)
                || !snapshotId.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return $"'{snapshotId}' is not a snapshot of {Path.GetFileName(filePath)}.";

            try
            {
                var source = Path.Combine(SnapshotDirectory(filePath), snapshotId);
                if (!File.Exists(source))
                    return $"Snapshot '{snapshotId}' no longer exists.";

                Capture(filePath);
                File.Copy(source, filePath, overwrite: true);
                _logger.LogInformation("Restored '{Path}' from snapshot {Id}", filePath, snapshotId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not restore '{Path}' from snapshot {Id}", filePath, snapshotId);
                return $"Could not restore the snapshot: {ex.Message}";
            }
        }

        // ── Internals ──────────────────────────────────────────────────────────

        /// <summary>"scripts--" — also the glob stem, so each file's history stays separate
        /// inside the shared directory.</summary>
        private static string Prefix(string filePath)
            => Path.GetFileNameWithoutExtension(filePath) + Separator;

        /// <summary>Existing snapshots for one file, newest first. Ordinal sort works because
        /// the stamp is fixed-width.</summary>
        private static List<string> SnapshotFiles(string filePath, string dir)
        {
            if (!Directory.Exists(dir)) return [];

            var files = Directory.GetFiles(dir, Prefix(filePath) + "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(files);
            return [.. files];
        }

        private static string NextSnapshotPath(string filePath, string dir)
        {
            var prefix = Prefix(filePath);
            var stamp = DateTime.UtcNow.ToString(StampFormat, CultureInfo.InvariantCulture);

            // The two-digit sequence disambiguates writes inside the same millisecond and is
            // fixed-width on purpose: a variable-length suffix would sort before its own
            // siblings ('-' < '.'), and the ordinal filename sort is what defines "newest".
            for (var n = 0; n < 100; n++)
            {
                var path = Path.Combine(dir, $"{prefix}{stamp}-{n:D2}.json");
                if (!File.Exists(path)) return path;
            }

            throw new IOException($"Could not find a free snapshot name for '{filePath}'.");
        }

        /// <summary>Delete everything past the newest <see cref="Keep"/>. Caller holds the lock.</summary>
        private void PruneLocked(string filePath, string dir)
        {
            foreach (var stale in SnapshotFiles(filePath, dir).Skip(Keep))
            {
                try { File.Delete(stale); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not prune old snapshot '{Path}'", stale);
                }
            }
        }

        private static bool SameContent(string filePath, string snapshotPath)
        {
            var a = new FileInfo(filePath);
            var b = new FileInfo(snapshotPath);
            if (a.Length != b.Length) return false;

            return File.ReadAllBytes(filePath).AsSpan()
                .SequenceEqual(File.ReadAllBytes(snapshotPath));
        }

        /// <summary>Stamp back out of the file name. Falls back to the file's own write time
        /// for a hand-renamed file rather than throwing mid-listing.</summary>
        private static DateTime ParseStamp(string filePath, string snapshotPath)
        {
            var name = Path.GetFileNameWithoutExtension(snapshotPath);
            var stamp = name[Math.Min(Prefix(filePath).Length, name.Length)..];

            // Drop the same-millisecond sequence suffix ("-07"); it is not part of the stamp.
            if (stamp.Length > StampFormat.Length)
                stamp = stamp[..StampFormat.Length];

            return DateTime.TryParseExact(
                stamp, StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : File.GetLastWriteTimeUtc(snapshotPath);
        }
    }
}
