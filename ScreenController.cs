using BestInScript.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestInScript.API.Controllers
{
    /// <summary>
    /// Endpoints for sampling screen pixel colors — used by the config UI to
    /// build PixelTriggers (the live readout and the click-to-sample workflow).
    /// All reads are passive desktop captures; see <see cref="ScreenColorService"/>.
    /// </summary>
    [ApiController]
    [Route("api/screen")]
    public class ScreenController : ControllerBase
    {
        private readonly ScreenColorService _screen;

        public ScreenController(ScreenColorService screen) => _screen = screen;

        /// <summary>
        /// Color at a screen coordinate. Optional radius averages a square
        /// region around it (0 = single pixel, 1 = 3x3, 2 = 5x5, ...).
        /// GET /api/screen/color?x=100&amp;y=200&amp;radius=1
        /// </summary>
        [HttpGet("color")]
        public IActionResult GetColor(
            [FromQuery] int x, [FromQuery] int y, [FromQuery] int radius = 0)
        {
            var c = _screen.ColorAtAveraged(x, y, Math.Clamp(radius, 0, 8));
            if (c is null)
                return Problem(
                    "Could not read screen pixel. If a game is running in " +
                    "fullscreen-exclusive mode, switch it to borderless or windowed.",
                    statusCode: 503);

            return Ok(new { x, y, r = c.Value.R, g = c.Value.G, b = c.Value.B });
        }

        /// <summary>
        /// Current cursor position together with the color under it. Drives the
        /// "sample at cursor" buttons in the UI.
        /// GET /api/screen/cursor?radius=1
        /// </summary>
        [HttpGet("cursor")]
        public IActionResult GetCursor([FromQuery] int radius = 0)
        {
            var pos = _screen.CursorPosition();
            if (pos is null)
                return Problem("Could not read cursor position.", statusCode: 503);

            var c = _screen.ColorAtAveraged(pos.Value.X, pos.Value.Y, Math.Clamp(radius, 0, 8));
            return Ok(new
            {
                x = pos.Value.X,
                y = pos.Value.Y,
                r = c?.R,
                g = c?.G,
                b = c?.B,
                readable = c is not null
            });
        }
    }
}