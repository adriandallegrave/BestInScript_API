using BestInScript.API.Overlay;
using BestInScript.API.Persistence;
using Microsoft.AspNetCore.Mvc;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace BestInScript.API.Controllers
{
    /// <summary>
    /// CRUD-lite for the on-screen overlay settings and a /screens endpoint
    /// so the web UI can populate the display picker.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class OverlayController : ControllerBase
    {
        private readonly OverlaySettingsStore _store;

        public OverlayController(OverlaySettingsStore store) => _store = store;

        // GET /api/overlay/settings
        /// <summary>Current overlay placement / appearance settings.</summary>
        [HttpGet("settings")]
        public ActionResult<OverlaySettings> Get() => Ok(_store.Get());

        // PUT /api/overlay/settings
        /// <summary>Replace the overlay settings. Takes effect immediately.</summary>
        [HttpPut("settings")]
        public ActionResult<OverlaySettings> Update([FromBody] OverlaySettings settings)
        {
            if (settings == null) return BadRequest("Settings body is required.");
            _store.Save(settings);
            return Ok(_store.Get());
        }

        // GET /api/overlay/screens
        /// <summary>
        /// Enumerates connected monitors so the web UI can offer a "Display"
        /// drop-down. Index aligns with <see cref="OverlaySettings.ScreenIndex"/>.
        /// </summary>
        [HttpGet("screens")]
        public ActionResult<IEnumerable<ScreenInfo>> Screens()
        {
            if (!OperatingSystem.IsWindows())
                return Ok(Array.Empty<ScreenInfo>());

            var screens = WinFormsScreen.AllScreens;
            var primary = WinFormsScreen.PrimaryScreen;
            var list = screens.Select((s, i) => new ScreenInfo
            {
                Index     = i,
                Name      = s.DeviceName,
                Width     = s.Bounds.Width,
                Height    = s.Bounds.Height,
                IsPrimary = primary != null && s.DeviceName == primary.DeviceName
            });
            return Ok(list);
        }

        public class ScreenInfo
        {
            public int Index { get; set; }
            public string Name { get; set; } = "";
            public int Width { get; set; }
            public int Height { get; set; }
            public bool IsPrimary { get; set; }
        }
    }
}
