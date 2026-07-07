using BestInScript.API.Models;
using BestInScript.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestInScript.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class PresetsController : ControllerBase
    {
        private readonly PresetRepository _presetRepo;
        private readonly ScriptRepository _scriptRepo;
        private readonly HotkeyEngine _engine;

        public PresetsController(
            PresetRepository presetRepo,
            ScriptRepository scriptRepo,
            HotkeyEngine engine)
        {
            _presetRepo = presetRepo;
            _scriptRepo = scriptRepo;
            _engine = engine;
        }

        // GET /api/presets
        [HttpGet]
        public ActionResult<IEnumerable<Preset>> GetAll()
            => Ok(_presetRepo.GetAll());

        // GET /api/presets/{id}
        [HttpGet("{id:guid}")]
        public ActionResult<Preset> GetById(Guid id)
        {
            var preset = _presetRepo.GetById(id);
            return preset is null ? NotFound() : Ok(preset);
        }

        // GET /api/presets/status
        [HttpGet("status")]
        public ActionResult<IEnumerable<PresetStatus>> GetStatus()
            => Ok(_engine.GetPresetStatus());

        // POST /api/presets
        [HttpPost]
        public ActionResult<Preset> Create([FromBody] Preset preset)
        {
            var validation = ValidatePreset(preset, isUpdate: false);
            if (validation is not null) return validation;

            preset.Id = Guid.NewGuid();
            _presetRepo.Save(preset);
            _engine.RegisterPreset(preset);

            return CreatedAtAction(nameof(GetById), new { id = preset.Id }, preset);
        }

        // PUT /api/presets/{id}
        [HttpPut("{id:guid}")]
        public ActionResult<Preset> Update(Guid id, [FromBody] Preset preset)
        {
            if (_presetRepo.GetById(id) is null)
                return NotFound();

            preset.Id = id;
            var validation = ValidatePreset(preset, isUpdate: true);
            if (validation is not null) return validation;

            _presetRepo.Save(preset);
            _engine.RegisterPreset(preset); // re-registers (deactivates old, installs new)

            return Ok(preset);
        }

        // DELETE /api/presets/{id}
        [HttpDelete("{id:guid}")]
        public IActionResult Delete(Guid id)
        {
            if (!_presetRepo.Delete(id))
                return NotFound();

            _engine.UnregisterPreset(id);
            return NoContent();
        }

        // ── Validation ─────────────────────────────────────────────────────────

        private ActionResult? ValidatePreset(Preset preset, bool isUpdate)
        {
            if (string.IsNullOrWhiteSpace(preset.Name))
                return BadRequest("Preset name is required.");

            if (!InputSimulatorService.IsValidTriggerKey(preset.TriggerKey))
                return BadRequest(
                    $"Invalid trigger key '{preset.TriggerKey}'. " +
                    "Must be a keyboard key (not a mouse button).");

            // Trigger key cannot collide with any script's trigger.
            var triggerCollision = _scriptRepo.GetAll()
                .FirstOrDefault(s => string.Equals(
                    s.TriggerKey, preset.TriggerKey, StringComparison.OrdinalIgnoreCase));
            if (triggerCollision != null)
                return BadRequest(
                    $"Trigger key '{preset.TriggerKey}' is already used by script '{triggerCollision.Name}'. " +
                    "Pick a different key.");

            // …or any other preset's trigger.
            var presetCollision = _presetRepo.GetAll()
                .FirstOrDefault(p => p.Id != preset.Id
                    && string.Equals(p.TriggerKey, preset.TriggerKey, StringComparison.OrdinalIgnoreCase));
            if (presetCollision != null)
                return BadRequest(
                    $"Trigger key '{preset.TriggerKey}' is already used by preset '{presetCollision.Name}'. " +
                    "Pick a different key.");

            // Every referenced script id must exist.
            var existingIds = _scriptRepo.GetAll().Select(s => s.Id).ToHashSet();
            var unknown = preset.ScriptIds.Where(id => !existingIds.Contains(id)).ToList();
            if (unknown.Count > 0)
                return BadRequest(
                    $"Preset references {unknown.Count} script id(s) that no longer exist: " +
                    string.Join(", ", unknown));

            // Deduplicate member ids while preserving order.
            preset.ScriptIds = preset.ScriptIds.Distinct().ToList();

            return null;
        }
    }
}
