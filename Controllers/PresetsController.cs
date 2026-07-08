using BestInScript.API.Engine;
using BestInScript.API.Models;
using BestInScript.API.Persistence;
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
        private readonly HotkeyEngine _engine;
        private readonly ConfigValidator _validator;

        public PresetsController(
            PresetRepository presetRepo,
            HotkeyEngine engine,
            ConfigValidator validator)
        {
            _presetRepo = presetRepo;
            _engine = engine;
            _validator = validator;
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
            var error = _validator.ValidatePreset(preset);
            if (error is not null) return BadRequest(error);

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
            var error = _validator.ValidatePreset(preset);
            if (error is not null) return BadRequest(error);

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
    }
}
