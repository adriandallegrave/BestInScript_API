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
    public class ScriptsController : ControllerBase
    {
        private readonly IScriptRepository _repo;
        private readonly HotkeyEngine      _engine;
        private readonly ConfigValidator   _validator;

        public ScriptsController(
            IScriptRepository repo,
            HotkeyEngine engine,
            ConfigValidator validator)
        {
            _repo      = repo;
            _engine    = engine;
            _validator = validator;
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
            var error = _validator.ValidateScript(script);
            if (error is not null) return BadRequest(error);

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

            var error = _validator.ValidateScript(script);
            if (error is not null) return BadRequest(error);

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
            => Ok(KeyNames.All());
    }
}
