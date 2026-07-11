using BestInScript.API.Engine;
using BestInScript.API.Models;
using BestInScript.API.Persistence;
using BestInScript.API.Services;
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
        private readonly EventScheduleService _events;
        private readonly OverlayEditModeSignal _editSignal;

        public OverlayController(
            OverlaySettingsStore store,
            EventScheduleService events,
            OverlayEditModeSignal editSignal)
        {
            _store = store;
            _events = events;
            _editSignal = editSignal;
        }

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

            // Emergency stop-all hotkey: keyboard keys only (mouse buttons and unknown names
            // rejected). Empty/null is allowed and disables the hotkey.
            if (!string.IsNullOrWhiteSpace(settings.StopAllHotkey)
                && !InputSimulatorService.IsValidTriggerKey(settings.StopAllHotkey))
                return BadRequest(
                    $"'{settings.StopAllHotkey}' is not a valid stop-all hotkey (keyboard keys only).");

            _store.Save(settings);
            return Ok(_store.Get());
        }

        // POST /api/overlay/edit-mode
        /// <summary>
        /// Arm drag-to-position edit mode on the live overlay: the pill drops
        /// click-through, becomes draggable, and shows ✓/✕ buttons to save or
        /// cancel. No-op when the overlay isn't running (e.g. non-Windows).
        /// </summary>
        [HttpPost("edit-mode")]
        public IActionResult EnterEditMode()
        {
            _editSignal.RequestEnter();
            return Ok();
        }

        // GET /api/overlay/events
        /// <summary>
        /// Live "next event" snapshot (world boss / helltide / legion) with
        /// formatted H:MM countdowns, so the config UI can show a reference preview.
        /// </summary>
        [HttpGet("events")]
        public ActionResult<object> Events()
        {
            var now = DateTimeOffset.UtcNow;
            long nowUnix = now.ToUnixTimeSeconds();
            var s = _events.GetSnapshot(now);
            return Ok(new
            {
                hasData = s.HasData,
                worldBoss = s.Boss == null ? null : new
                {
                    name = s.Boss.Name,
                    zone = s.Boss.Zone,
                    remaining = EventScheduleCalculator.FormatRemaining(s.Boss.StartUnix, nowUnix),
                    startUnix = s.Boss.StartUnix
                },
                legion = s.LegionStartUnix == null ? null : new
                {
                    remaining = EventScheduleCalculator.FormatRemaining(s.LegionStartUnix.Value, nowUnix),
                    startUnix = s.LegionStartUnix.Value
                },
                helltide = s.Helltide == null ? null : new
                {
                    active = s.Helltide.Active,
                    remaining = EventScheduleCalculator.FormatRemaining(s.Helltide.TargetUnix, nowUnix),
                    targetUnix = s.Helltide.TargetUnix
                }
            });
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
