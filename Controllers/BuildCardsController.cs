using BestInScript.API.Models;
using BestInScript.API.Persistence;
using BestInScript.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace BestInScript.API.Controllers
{
    /// <summary>
    /// CRUD for the overlay's build-guide cards, plus the image upload/serve pair.
    ///
    /// Images arrive as a multipart upload (the web UI turns a clipboard paste or a
    /// drag-drop into a Blob) and are stored as-is — no re-encode. The panel downscales
    /// at decode time instead, so a big paragon board costs little to render.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BuildCardsController : ControllerBase
    {
        /// <summary>Ceiling for one card image. A snip of a guide is a few hundred KB;
        /// this is headroom, not a target.</summary>
        private const long MaxImageBytes = 8 * 1024 * 1024;

        private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".webp"] = "image/webp"
        };

        private static readonly Dictionary<string, string> ExtensionByMime = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/jpg"] = ".jpg",
            ["image/gif"] = ".gif",
            ["image/bmp"] = ".bmp",
            ["image/webp"] = ".webp"
        };

        private readonly IBuildCardRepository _repo;
        private readonly BuildCardSignal _signal;

        public BuildCardsController(IBuildCardRepository repo, BuildCardSignal signal)
        {
            _repo = repo;
            _signal = signal;
        }

        // GET /api/buildcards
        /// <summary>All cards in cycle order. Metadata only — fetch bytes from /{id}/image.</summary>
        [HttpGet]
        [Produces("application/json")]
        public ActionResult<IEnumerable<BuildCard>> GetAll() => Ok(_repo.GetAll());

        // GET /api/buildcards/{id}
        [HttpGet("{id:guid}")]
        [Produces("application/json")]
        public ActionResult<BuildCard> GetById(Guid id)
        {
            var card = _repo.GetById(id);
            return card is null ? NotFound() : Ok(card);
        }

        // POST /api/buildcards
        /// <summary>Create a card from an image upload. The name defaults to the card count.</summary>
        [HttpPost]
        [Produces("application/json")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxImageBytes)]
        public async Task<ActionResult<BuildCard>> Create(
            [FromForm] string? name, [FromForm] IFormFile? file, CancellationToken ct)
        {
            var error = ValidateUpload(file);
            if (error is not null) return BadRequest(error);

            var card = new BuildCard
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(name) ? "Card" : name.Trim(),
                // Append to the end of the cycle rather than fighting for position 0.
                Order = _repo.GetAll().Count
            };

            await using (var stream = file!.OpenReadStream())
                card.ImageFileName = await _repo.SaveImageAsync(card.Id, stream, ExtensionFor(file), ct);

            _repo.Save(card);
            _signal.NotifyCardsChanged();

            return CreatedAtAction(nameof(GetById), new { id = card.Id }, card);
        }

        // PUT /api/buildcards/{id}
        /// <summary>Update a card's metadata (name / order / notes / scale). The image is untouched —
        /// replace that via POST /{id}/image.</summary>
        [HttpPut("{id:guid}")]
        [Produces("application/json")]
        public ActionResult<BuildCard> Update(Guid id, [FromBody] BuildCard card)
        {
            var existing = _repo.GetById(id);
            if (existing is null) return NotFound();
            if (card is null) return BadRequest("Card body is required.");
            if (string.IsNullOrWhiteSpace(card.Name)) return BadRequest("Card name is required.");

            existing.Name = card.Name.Trim();
            existing.Order = card.Order;
            existing.Notes = card.Notes;
            existing.Scale = Math.Clamp(card.Scale <= 0 ? 1.0 : card.Scale, 0.25, 2.0);
            // ImageFileName is deliberately not taken from the request — it is
            // server-generated and owned by the image endpoints.

            _repo.Save(existing);
            _signal.NotifyCardsChanged();
            return Ok(existing);
        }

        // POST /api/buildcards/{id}/image
        /// <summary>Replace a card's image, keeping its name and position in the cycle.</summary>
        [HttpPost("{id:guid}/image")]
        [Produces("application/json")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxImageBytes)]
        public async Task<ActionResult<BuildCard>> ReplaceImage(
            Guid id, [FromForm] IFormFile? file, CancellationToken ct)
        {
            var card = _repo.GetById(id);
            if (card is null) return NotFound();

            var error = ValidateUpload(file);
            if (error is not null) return BadRequest(error);

            await using (var stream = file!.OpenReadStream())
                card.ImageFileName = await _repo.SaveImageAsync(card.Id, stream, ExtensionFor(file), ct);

            _repo.Save(card);
            _signal.NotifyCardsChanged();
            return Ok(card);
        }

        // GET /api/buildcards/{id}/image
        /// <summary>The card's image bytes, for the web UI's preview.</summary>
        [HttpGet("{id:guid}/image")]
        public IActionResult GetImage(Guid id)
        {
            var card = _repo.GetById(id);
            if (card is null) return NotFound();

            // Resolved by id through the repo — no client-supplied path ever reaches the
            // filesystem, so there is nothing to traverse.
            var path = _repo.ImagePath(card);
            if (path is null) return NotFound();

            var ext = Path.GetExtension(path);
            var mime = MimeByExtension.TryGetValue(ext, out var m) ? m : "application/octet-stream";
            return PhysicalFile(path, mime);
        }

        // DELETE /api/buildcards/{id}
        [HttpDelete("{id:guid}")]
        public IActionResult Delete(Guid id)
        {
            if (!_repo.Delete(id)) return NotFound();

            _signal.NotifyCardsChanged();
            return NoContent();
        }

        // ── Internals ────────────────────────────────────────────────────────

        private static string? ValidateUpload(IFormFile? file)
        {
            if (file is null || file.Length == 0)
                return "An image file is required.";

            if (file.Length > MaxImageBytes)
                return $"Image is too large (max {MaxImageBytes / (1024 * 1024)} MB).";

            var byMime = ExtensionByMime.ContainsKey(file.ContentType ?? "");
            var byExt = MimeByExtension.ContainsKey(Path.GetExtension(file.FileName ?? ""));
            if (!byMime && !byExt)
                return "Unsupported image type. Use PNG, JPEG, GIF, BMP or WebP.";

            return null;
        }

        /// <summary>Extension chosen from the content type, falling back to the uploaded name's
        /// extension. The repository re-checks it against its allow-list either way.</summary>
        private static string ExtensionFor(IFormFile file)
        {
            if (ExtensionByMime.TryGetValue(file.ContentType ?? "", out var ext))
                return ext;

            var fromName = Path.GetExtension(file.FileName ?? "");
            return MimeByExtension.ContainsKey(fromName) ? fromName : ".png";
        }
    }
}
