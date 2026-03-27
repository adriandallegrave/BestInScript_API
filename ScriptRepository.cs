using System.Text.Json;
using BestInScript.API.Models;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Persists ScriptConfig objects to a JSON file on disk.
    /// Thread-safe via lock.
    /// </summary>
    public class ScriptRepository
    {
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
            var path = config["BestInScript:DataFilePath"] ?? "scripts.json";
            // Resolve relative to the app's content root
            _filePath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppContext.BaseDirectory, path);

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
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_filePath, JsonSerializer.Serialize(scripts, JsonOpts));
        }
    }
}
