using BestInScript.API.Engine;
using BestInScript.API.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace BestInScript.API.Controllers
{
    /// <summary>
    /// Named config profiles: each is a directory of scripts.json + presets.json,
    /// switchable from the web UI (per character / build / season). The overlay
    /// settings are global and not part of a profile.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ProfilesController : ControllerBase
    {
        private readonly ProfileManager _profiles;
        private readonly HotkeyEngine _engine;

        public ProfilesController(ProfileManager profiles, HotkeyEngine engine)
        {
            _profiles = profiles;
            _engine = engine;
        }

        // GET /api/profiles
        [HttpGet]
        public ActionResult<ProfilesResponse> Get()
            => Ok(new ProfilesResponse { Active = _profiles.Active, Profiles = _profiles.List() });

        // POST /api/profiles
        /// <summary>Create a new profile, optionally copying the active profile's scripts + presets.</summary>
        [HttpPost]
        public ActionResult<ProfilesResponse> Create([FromBody] CreateProfileRequest req)
        {
            if (req is null) return BadRequest("Request body is required.");

            var error = _profiles.Create(req.Name ?? "", req.CopyFromCurrent);
            if (error is not null) return BadRequest(error);

            return Ok(new ProfilesResponse { Active = _profiles.Active, Profiles = _profiles.List() });
        }

        // POST /api/profiles/{name}/activate
        /// <summary>Switch to a profile. Stops running scripts and loads the profile's config.</summary>
        [HttpPost("{name}/activate")]
        public ActionResult<ProfilesResponse> Activate(string name)
        {
            if (!_profiles.Exists(name))
                return NotFound($"Profile '{name}' does not exist.");

            _engine.SwitchProfile(name);
            return Ok(new ProfilesResponse { Active = _profiles.Active, Profiles = _profiles.List() });
        }

        // PUT /api/profiles/{name}
        [HttpPut("{name}")]
        public ActionResult<ProfilesResponse> Rename(string name, [FromBody] RenameProfileRequest req)
        {
            if (req is null) return BadRequest("Request body is required.");

            var error = _profiles.Rename(name, req.NewName ?? "");
            if (error is not null) return BadRequest(error);

            return Ok(new ProfilesResponse { Active = _profiles.Active, Profiles = _profiles.List() });
        }

        // DELETE /api/profiles/{name}
        [HttpDelete("{name}")]
        public ActionResult<ProfilesResponse> Delete(string name)
        {
            var error = _profiles.Delete(name);
            if (error is not null) return BadRequest(error);

            return Ok(new ProfilesResponse { Active = _profiles.Active, Profiles = _profiles.List() });
        }

        public class ProfilesResponse
        {
            public string Active { get; set; } = "";
            public IReadOnlyList<string> Profiles { get; set; } = [];
        }

        public class CreateProfileRequest
        {
            public string? Name { get; set; }
            public bool CopyFromCurrent { get; set; }
        }

        public class RenameProfileRequest
        {
            public string? NewName { get; set; }
        }
    }
}
