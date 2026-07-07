using System.Text.Json;
using BestInScript.API.Models;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Persists ScriptConfig objects to a JSON file on disk. Thread-safe via lock.
    ///
    /// File location resolution (highest to lowest precedence):
    ///   1. BestInScript:DataFilePath if set AND absolute — used as-is.
    ///   2. BestInScript:DataDirectory + (DataFilePath or "scripts.json").
    ///   3. AppContext.BaseDirectory + (DataFilePath or "scripts.json").
    ///
    /// The chosen directory is auto-created at startup so a fresh path like
    /// C:\temp works even if the folder doesn't exist yet.
    /// </summary>
    public class ScriptRepository
    {
        private const string DefaultFileName = "scripts.json";

        private readonly string _filePath;
        private readonly ILogger<ScriptRepository> _logger;
        private readonly object _lock = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ScriptRepository(IConfiguration config, ILogger<ScriptRepository> logger)
        {
            _logger = logger;
            _filePath = ResolveDataFilePath(config, "BestInScript:DataFilePath", DefaultFileName);
            EnsureDirectory(_filePath);
            _logger.LogInformation("Script data file: {Path}", _filePath);
        }

        public List<ScriptConfig> GetAll()
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath))
                    return [];

                try
                {
                    var json = File.ReadAllText(_filePath);
                    return JsonSerializer.Deserialize<List<ScriptConfig>>(json, JsonOpts) ?? [];
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read scripts file");
                    return [];
                }
            }
        }

        public ScriptConfig? GetById(Guid id)
            => GetAll().FirstOrDefault(s => s.Id == id);

        public ScriptConfig Save(ScriptConfig script)
        {
            lock (_lock)
            {
                var all = GetAllInternal();
                var existing = all.FindIndex(s => s.Id == script.Id);

                if (existing >= 0)
                    all[existing] = script;
                else
                    all.Add(script);

                WriteAll(all);
                return script;
            }
        }

        public bool Delete(Guid id)
        {
            lock (_lock)
            {
                var all = GetAllInternal();
                var count = all.RemoveAll(s => s.Id == id);
                if (count > 0) WriteAll(all);
                return count > 0;
            }
        }

        // ── Internals ──────────────────────────────────────────────────────────

        private List<ScriptConfig> GetAllInternal()
        {
            if (!File.Exists(_filePath))
                return [];

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<ScriptConfig>>(json, JsonOpts) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private void WriteAll(List<ScriptConfig> scripts)
        {
            EnsureDirectory(_filePath);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(scripts, JsonOpts));
        }

        /// <summary>
        /// Shared path-resolution logic — kept internal so OverlaySettingsStore
        /// can reuse it via composition if desired, but currently each file
        /// resolves its own path against the same DataDirectory key.
        /// </summary>
        internal static string ResolveDataFilePath(
            IConfiguration config, string specificKey, string defaultFileName)
        {
            var specific = config[specificKey];
            var dataDir = config["BestInScript:DataDirectory"];

            // 1. Explicit absolute path wins.
            if (!string.IsNullOrWhiteSpace(specific) && Path.IsPathRooted(specific))
                return Path.GetFullPath(specific);

            // 2. Otherwise, combine directory + filename.
            var fileName = string.IsNullOrWhiteSpace(specific) ? defaultFileName : specific;
            var directory = string.IsNullOrWhiteSpace(dataDir)
                ? AppContext.BaseDirectory
                : dataDir;

            return Path.GetFullPath(Path.Combine(directory, fileName));
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