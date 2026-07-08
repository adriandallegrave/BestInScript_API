using BestInScript.API.Models;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Persists ScriptConfig objects to a JSON file on disk (scripts.json by
    /// default). Path resolution and file handling live in the base class /
    /// <see cref="DataFilePathResolver"/>. The chosen directory is
    /// auto-created at startup so a fresh path like C:\temp works even if the
    /// folder doesn't exist yet.
    /// </summary>
    public class ScriptRepository : JsonListFileStore<ScriptConfig>, IScriptRepository, IProfileScopedStore
    {
        private const string DefaultFileName = "scripts.json";

        public ScriptRepository(IConfiguration config, ILogger<ScriptRepository> logger)
            : base(DataFilePathResolver.Resolve(config, "BestInScript:DataFilePath", DefaultFileName),
                   logger, "scripts")
        {
            logger.LogInformation("Script data file: {Path}", FilePath);
        }

        protected override Guid GetId(ScriptConfig item) => item.Id;

        // ── IProfileScopedStore ──────────────────────────────────────────────
        public string ProfileFileName => DefaultFileName;
        public void Rebind(string absolutePath) => SetFilePath(absolutePath);
    }
}
