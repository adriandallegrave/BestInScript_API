using BestInScript.API.Models;
using BestInScript.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestInScript.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ScriptsController : ControllerBase
    {
        private readonly ScriptRepository  _repo;
        private readonly PresetRepository  _presetRepo;
        private readonly HotkeyEngine      _engine;
        private readonly InputSimulatorService _inputSim;

        public ScriptsController(
            ScriptRepository repo,
            PresetRepository presetRepo,
            HotkeyEngine engine,
            InputSimulatorService inputSim)
        {
            _repo       = repo;
            _presetRepo = presetRepo;
            _engine     = engine;
            _inputSim   = inputSim;
        }

        // GET /api/scripts
        [HttpGet]
        public ActionResult<IEnumerable<ScriptConfig>> GetAll()
            => Ok(_repo.GetAll());

        // GET /api/scripts/{id}
        [HttpGet("{id:guid}")]
        public ActionResult<ScriptConfig> GetById(Guid id)
        {
            var script = _repo.GetById(id);
            return script is null ? NotFound() : Ok(script);
        }

        // POST /api/scripts
        [HttpPost]
        public ActionResult<ScriptConfig> Create([FromBody] ScriptConfig script)
        {
            var validation = ValidateScript(script);
            if (validation is not null) return validation;

            script.Id = Guid.NewGuid();
            _repo.Save(script);
            _engine.RegisterScript(script);

            return CreatedAtAction(nameof(GetById), new { id = script.Id }, script);
        }

        // PUT /api/scripts/{id}
        [HttpPut("{id:guid}")]
        public ActionResult<ScriptConfig> Update(Guid id, [FromBody] ScriptConfig script)
        {
            if (_repo.GetById(id) is null)
                return NotFound();

            var validation = ValidateScript(script);
            if (validation is not null) return validation;

            script.Id = id;
            _repo.Save(script);
            _engine.RegisterScript(script); // re-registers (stops old, starts new binding)

            return Ok(script);
        }

        // DELETE /api/scripts/{id}
        [HttpDelete("{id:guid}")]
        public IActionResult Delete(Guid id)
        {
            if (!_repo.Delete(id))
                return NotFound();

            _engine.UnregisterScript(id);
            return NoContent();
        }

        // GET /api/scripts/valid-keys
        /// <summary>Returns all recognised key names for use in steps.</summary>
        [HttpGet("valid-keys")]
        public ActionResult<IEnumerable<string>> GetValidKeys()
            => Ok(AllValidKeys());

        // ── Validation ─────────────────────────────────────────────────────────

        private ActionResult? ValidateScript(ScriptConfig script)
        {
            if (string.IsNullOrWhiteSpace(script.Name))
                return BadRequest("Script name is required.");

            if (!InputSimulatorService.IsValidTriggerKey(script.TriggerKey))
                return BadRequest(
                    $"Invalid trigger key '{script.TriggerKey}'. " +
                    "Must be a keyboard key (not a mouse button). " +
                    "Call GET /api/scripts/valid-keys for the full list.");

            // Trigger key cannot collide with a preset's trigger.
            var presetCollision = _presetRepo.GetAll()
                .FirstOrDefault(p => string.Equals(
                    p.TriggerKey, script.TriggerKey, StringComparison.OrdinalIgnoreCase));
            if (presetCollision != null)
                return BadRequest(
                    $"Trigger key '{script.TriggerKey}' is already used by preset '{presetCollision.Name}'. " +
                    "Pick a different key.");

            if (script.DelayMin < 0.1 || script.DelayMax > 5.0 || script.DelayMin > script.DelayMax)
                return BadRequest("DelayMin must be ≥0.1 s, DelayMax must be ≤5.0 s, and Min must be ≤Max.");

            foreach (var step in script.Steps)
            {
                foreach (var key in step.Hold.Concat(step.Press))
                {
                    if (!InputSimulatorService.IsValidKey(key))
                        return BadRequest($"Unknown key name '{key}' in a step. " +
                                          "Call GET /api/scripts/valid-keys for the full list.");
                }
            }

            return null;
        }

        private static IEnumerable<string> AllValidKeys()
        {
            var letters  = Enumerable.Range('A', 26).Select(c => ((char)c).ToString());
            var digits   = Enumerable.Range(0, 10).Select(n => n.ToString());
            var fkeys    = Enumerable.Range(1, 12).Select(n => $"F{n}");
            var numpad   = Enumerable.Range(0, 10).Select(n => $"NumPad{n}");
            var special  = new[]
            {
                "Space","Enter","Tab","Escape","Backspace","Delete","Insert",
                "Home","End","PageUp","PageDown",
                "Up","Down","Left","Right",
                "Shift","LShift","RShift",
                "Ctrl","LCtrl","RCtrl",
                "Alt","LAlt","RAlt",
                "Multiply","Add","Subtract","Decimal","Divide"
            };
            var mouse = new[] { "Mouse1","Mouse2","Mouse3","Mouse4","Mouse5" };

            return letters.Concat(digits).Concat(fkeys).Concat(numpad)
                          .Concat(special).Concat(mouse);
        }
    }
}
