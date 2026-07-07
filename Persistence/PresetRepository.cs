using System.Text.Json;
using BestInScript.API.Models;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Persists Preset objects to a JSON file on disk. Thread-safe via lock.
    /// Mirrors <see cref="ScriptRepository"/> — single file holds many
    /// presets, path resolved against BestInScript:DataDirectory just like
    /// scripts.json.
    /// </summary>
    public class PresetRepository
    {
        private const string DefaultFileName = "presets.json";

        private readonly string _filePath;
        private readonly ILogger<PresetRepository> _logger;
        private readonly object _lock = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public PresetRepository(IConfiguration config, ILogger<PresetRepository> logger)
        {
            _logger = logger;
            _filePath = ScriptRepository.ResolveDataFilePath(
                config, "BestInScript:PresetsFilePath", DefaultFileName);
            EnsureDirectory(_filePath);
            _logger.LogInformation("Preset data file: {Path}", _filePath);
        }

        public List<Preset> GetAll()
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath))
                    return [];

                try
                {
                    var json = File.ReadAllText(_filePath);
                    return JsonSerializer.Deserialize<List<Preset>>(json, JsonOpts) ?? [];
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read presets file");
                    return [];
                }
            }
        }

        public Preset? GetById(Guid id)
            => GetAll().FirstOrDefault(p => p.Id == id);

        public Preset Save(Preset preset)
        {
            lock (_lock)
            {
                var all = GetAllInternal();
                var existing = all.FindIndex(p => p.Id == preset.Id);

                if (existing >= 0)
                    all[existing] = preset;
                else
                    all.Add(preset);

                WriteAll(all);
                return preset;
            }
        }

        public bool Delete(Guid id)
        {
            lock (_lock)
            {
                var all = GetAllInternal();
                var count = all.RemoveAll(p => p.Id == id);
                if (count > 0) WriteAll(all);
                return count > 0;
            }
        }

        private List<Preset> GetAllInternal()
        {
            if (!File.Exists(_filePath))
                return [];

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<Preset>>(json, JsonOpts) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private void WriteAll(List<Preset> presets)
        {
            EnsureDirectory(_filePath);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(presets, JsonOpts));
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
