using BestInScript.API.Engine;
using BestInScript.API.Persistence;
using BestInScript.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestInScript.API.Controllers
{
    /// <summary>
    /// Config snapshots (BACKLOG 3.4): the rotating on-disk history every config write
    /// leaves behind, plus the restore path that undoes a bad edit.
    ///
    /// The three list files are profile-scoped, so what this lists follows the active
    /// profile; overlay-settings.json is global. After a restore the affected consumer is
    /// refreshed in-process — the engine re-registers, the build panel re-reads, the
    /// overlay restyles — so nothing needs a restart.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class SnapshotsController : ControllerBase
    {
        private readonly ConfigSnapshotService _snapshots;
        private readonly ScriptRepository _scripts;
        private readonly PresetRepository _presets;
        private readonly BuildCardRepository _buildCardRepo;
        private readonly OverlaySettingsStore _settings;
        private readonly HotkeyEngine _engine;
        private readonly BuildCardSignal _buildCards;

        public SnapshotsController(
            ConfigSnapshotService snapshots,
            ScriptRepository scripts,
            PresetRepository presets,
            BuildCardRepository buildCardRepo,
            OverlaySettingsStore settings,
            HotkeyEngine engine,
            BuildCardSignal buildCards)
        {
            _snapshots = snapshots;
            _scripts = scripts;
            _presets = presets;
            _buildCardRepo = buildCardRepo;
            _settings = settings;
            _engine = engine;
            _buildCards = buildCards;
        }

        // GET /api/snapshots
        [HttpGet]
        public ActionResult<SnapshotsResponse> Get() => Ok(BuildResponse());

        // POST /api/snapshots/{target}/restore
        /// <summary>
        /// Copy a snapshot back over the live file and refresh whatever reads it. The file's
        /// current contents are archived first, so the restore itself can be undone.
        /// </summary>
        [HttpPost("{target}/restore")]
        public ActionResult<SnapshotsResponse> Restore(string target, [FromBody] RestoreRequest req)
        {
            if (req is null) return BadRequest("Request body is required.");

            var found = Resolve(target);
            if (found is null) return NotFound($"Unknown snapshot target '{target}'.");

            var error = _snapshots.Restore(found.Path, req.Id ?? "");
            if (error is not null) return BadRequest(error);

            found.Refresh();
            return Ok(BuildResponse());
        }

        // ── Internals ──────────────────────────────────────────────────────────

        /// <summary>
        /// The four snapshotted config files. The path is read off the live store on every
        /// call rather than cached, because <see cref="ProfileManager"/> repoints the three
        /// list stores whenever the active profile changes.
        /// </summary>
        private IEnumerable<SnapshotTarget> Targets()
        {
            yield return new SnapshotTarget(
                "scripts", "Scripts", _scripts.FilePath, _engine.ReloadFromDisk);

            yield return new SnapshotTarget(
                "presets", "Presets", _presets.FilePath, _engine.ReloadFromDisk);

            // Images are side files and are not snapshotted: a restored card whose image was
            // deleted renders the panel's "image failed to load" state until it is re-pasted.
            yield return new SnapshotTarget(
                "build-cards", "Build guide cards", _buildCardRepo.FilePath, _buildCards.NotifyCardsChanged);

            yield return new SnapshotTarget(
                "overlay-settings", "Overlay settings", _settings.FilePath, _settings.Reload);
        }

        private SnapshotTarget? Resolve(string target)
            => Targets().FirstOrDefault(
                t => string.Equals(t.Key, target, StringComparison.OrdinalIgnoreCase));

        private SnapshotsResponse BuildResponse() => new()
        {
            Keep = _snapshots.Keep,
            Targets = [.. Targets().Select(t => new SnapshotTargetResponse
            {
                Target = t.Key,
                Label = t.Label,
                File = Path.GetFileName(t.Path),
                Snapshots = _snapshots.List(t.Path)
            })]
        };

        private sealed record SnapshotTarget(string Key, string Label, string Path, Action Refresh);

        public class SnapshotsResponse
        {
            /// <summary>Snapshots retained per file; 0 means the feature is switched off.</summary>
            public int Keep { get; set; }

            public IReadOnlyList<SnapshotTargetResponse> Targets { get; set; } = [];
        }

        public class SnapshotTargetResponse
        {
            public string Target { get; set; } = "";
            public string Label { get; set; } = "";
            public string File { get; set; } = "";
            public IReadOnlyList<ConfigSnapshotInfo> Snapshots { get; set; } = [];
        }

        public class RestoreRequest
        {
            /// <summary>Snapshot file name, as listed by GET /api/snapshots.</summary>
            public string? Id { get; set; }
        }
    }
}
