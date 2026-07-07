using BestInScript.API.Engine;
using Microsoft.AspNetCore.Mvc;

namespace BestInScript.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class EngineController : ControllerBase
    {
        private readonly HotkeyEngine _engine;

        public EngineController(HotkeyEngine engine)
            => _engine = engine;

        // GET /api/engine/status
        /// <summary>
        /// Returns the runtime status of every registered script
        /// (name, trigger key, and whether it is currently looping).
        /// </summary>
        [HttpGet("status")]
        public ActionResult<IEnumerable<ScriptStatus>> GetStatus()
            => Ok(_engine.GetStatus());

        // POST /api/engine/stop-all
        /// <summary>Immediately stops all running scripts.</summary>
        [HttpPost("stop-all")]
        public IActionResult StopAll()
        {
            _engine.StopAll();
            return Ok(new { message = "All scripts stopped." });
        }
    }
}
