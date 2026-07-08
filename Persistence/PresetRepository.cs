using BestInScript.API.Models;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Persists Preset objects to a JSON file on disk (presets.json by
    /// default), resolved against BestInScript:DataDirectory just like
    /// scripts.json.
    /// </summary>
    public class PresetRepository : JsonListFileStore<Preset>, IPresetRepository, IProfileScopedStore
    {
        private const string DefaultFileName = "presets.json";

        public PresetRepository(IConfiguration config, ILogger<PresetRepository> logger)
            : base(DataFilePathResolver.Resolve(config, "BestInScript:PresetsFilePath", DefaultFileName),
                   logger, "presets")
        {
            logger.LogInformation("Preset data file: {Path}", FilePath);
        }

        protected override Guid GetId(Preset item) => item.Id;

        // ── IProfileScopedStore ──────────────────────────────────────────────
        public string ProfileFileName => DefaultFileName;
        public void Rebind(string absolutePath) => SetFilePath(absolutePath);
    }
}
