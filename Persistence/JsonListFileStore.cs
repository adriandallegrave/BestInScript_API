using System.Text.Json;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Base class for stores that persist a list of entities to a single JSON
    /// file on disk. Thread-safe via lock. Every operation re-reads the file,
    /// so external edits are picked up on the next call.
    ///
    /// Read errors on the public <see cref="GetAll"/> are logged and yield an
    /// empty list; reads inside <see cref="Save"/>/<see cref="Delete"/> are
    /// silent (a corrupt file is simply treated as empty and overwritten).
    /// </summary>
    public abstract class JsonListFileStore<T> where T : class
    {
        private readonly object _lock = new();
        private readonly ILogger _logger;
        private readonly string _entityLabel;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        protected JsonListFileStore(string filePath, ILogger logger, string entityLabel)
        {
            FilePath = filePath;
            _logger = logger;
            _entityLabel = entityLabel;
            EnsureDirectory(FilePath);
        }

        /// <summary>Fully resolved path of the backing JSON file. May be repointed
        /// at runtime by <see cref="SetFilePath"/> when the active profile changes.</summary>
        public string FilePath { get; private set; }

        /// <summary>
        /// Swap the backing file (used by <see cref="ProfileManager"/> on a profile
        /// switch). Takes the same lock as reads/writes so no operation sees a half-set
        /// path, and ensures the new directory exists. There is no cached list to reload —
        /// every op re-reads the file — so repointing is just swapping the path.
        /// </summary>
        protected void SetFilePath(string path)
        {
            lock (_lock)
            {
                FilePath = path;
                EnsureDirectory(FilePath);
            }
        }

        /// <summary>Identity selector used for upsert and delete.</summary>
        protected abstract Guid GetId(T item);

        public List<T> GetAll()
        {
            lock (_lock)
            {
                if (!File.Exists(FilePath))
                    return [];

                try
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? [];
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read {Entity} file", _entityLabel);
                    return [];
                }
            }
        }

        public T? GetById(Guid id)
            => GetAll().FirstOrDefault(x => GetId(x) == id);

        public T Save(T item)
        {
            lock (_lock)
            {
                var all = ReadSilent();
                var existing = all.FindIndex(x => GetId(x) == GetId(item));

                if (existing >= 0)
                    all[existing] = item;
                else
                    all.Add(item);

                WriteAll(all);
                return item;
            }
        }

        public bool Delete(Guid id)
        {
            lock (_lock)
            {
                var all = ReadSilent();
                var count = all.RemoveAll(x => GetId(x) == id);
                if (count > 0) WriteAll(all);
                return count > 0;
            }
        }

        // ── Internals ──────────────────────────────────────────────────────────

        private List<T> ReadSilent()
        {
            if (!File.Exists(FilePath))
                return [];

            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private void WriteAll(List<T> items)
        {
            EnsureDirectory(FilePath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items, JsonOpts));
        }

        private void EnsureDirectory(string filePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not create data directory for '{Path}'. File operations may fail.",
                    filePath);
            }
        }
    }
}
