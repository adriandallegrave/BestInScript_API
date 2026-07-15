using BestInScript.API.Models;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Persists <see cref="BuildCard"/> rows to build-cards.json and their images to a
    /// "cards" subdirectory beside it, both inside the active profile's directory.
    ///
    /// Images are side files rather than base64 inside the JSON on purpose: the base
    /// store re-reads and re-deserializes the whole file on every operation, so blobs in
    /// the JSON would make every read parse megabytes.
    /// </summary>
    public class BuildCardRepository : JsonListFileStore<BuildCard>, IBuildCardRepository, IProfileScopedStore
    {
        private const string DefaultFileName = "build-cards.json";
        private const string ImageSubdirectory = "cards";

        /// <summary>Image formats WPF's BitmapImage decodes and browsers render. The extension
        /// is only ever chosen from this set, never taken verbatim from a request.</summary>
        private static readonly string[] AllowedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

        private readonly ILogger<BuildCardRepository> _logger;

        public BuildCardRepository(IConfiguration config, ILogger<BuildCardRepository> logger)
            : base(DataFilePathResolver.Resolve(config, "BestInScript:BuildCardsFilePath", DefaultFileName),
                   logger, "build cards")
        {
            _logger = logger;
            logger.LogInformation("Build card data file: {Path}", FilePath);
        }

        protected override Guid GetId(BuildCard item) => item.Id;

        /// <summary>Directory holding this profile's card images. Follows <see cref="JsonListFileStore{T}.FilePath"/>
        /// on every profile rebind, so images always resolve against the active profile.</summary>
        public string ImageDirectory
        {
            get
            {
                var dir = Path.GetDirectoryName(FilePath);
                return string.IsNullOrEmpty(dir)
                    ? ImageSubdirectory
                    : Path.Combine(dir, ImageSubdirectory);
            }
        }

        // ── IBuildCardRepository ─────────────────────────────────────────────

        /// <summary>Always returns cycle order, so the panel, the web list, and the cycle
        /// key can never disagree about which card is "next".</summary>
        public override List<BuildCard> GetAll() => InCycleOrder(base.GetAll());

        /// <summary>Cycle order: Order ascending, then Name so ties are stable across reads.</summary>
        public static List<BuildCard> InCycleOrder(IEnumerable<BuildCard> cards)
            => cards.OrderBy(c => c.Order)
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

        public string? ImagePath(BuildCard card)
        {
            if (string.IsNullOrWhiteSpace(card.ImageFileName))
                return null;

            // The name is server-generated, but re-strip any path parts so a hand-edited
            // build-cards.json can't point the panel at a file outside the profile.
            var bare = Path.GetFileName(card.ImageFileName);
            if (string.IsNullOrEmpty(bare))
                return null;

            var path = Path.Combine(ImageDirectory, bare);
            return File.Exists(path) ? path : null;
        }

        public async Task<string> SaveImageAsync(
            Guid id, Stream content, string extension, CancellationToken ct = default)
        {
            var ext = NormalizeExtension(extension);
            var fileName = id.ToString("N") + ext;

            Directory.CreateDirectory(ImageDirectory);

            // A re-upload in a different format would otherwise strand the old file.
            DeleteImagesFor(id, keep: fileName);

            var path = Path.Combine(ImageDirectory, fileName);
            await using (var file = File.Create(path))
                await content.CopyToAsync(file, ct);

            return fileName;
        }

        public override bool Delete(Guid id)
        {
            var removed = base.Delete(id);
            if (removed) DeleteImagesFor(id);
            return removed;
        }

        // ── IProfileScopedStore ──────────────────────────────────────────────
        public string ProfileFileName => DefaultFileName;
        public string? ProfileSubdirectory => ImageSubdirectory;
        public void Rebind(string absolutePath) => SetFilePath(absolutePath);

        // ── Internals ────────────────────────────────────────────────────────

        /// <summary>Maps a requested extension onto the allow-list, defaulting to .png.</summary>
        private static string NormalizeExtension(string extension)
        {
            var ext = extension.StartsWith('.') ? extension : "." + extension;
            ext = ext.ToLowerInvariant();
            return AllowedExtensions.Contains(ext) ? ext : ".png";
        }

        /// <summary>Removes every stored image for a card id, optionally sparing one file name.</summary>
        private void DeleteImagesFor(Guid id, string? keep = null)
        {
            if (!Directory.Exists(ImageDirectory)) return;

            foreach (var ext in AllowedExtensions)
            {
                var name = id.ToString("N") + ext;
                if (string.Equals(name, keep, StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = Path.Combine(ImageDirectory, name);
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete card image '{Path}'", path);
                }
            }
        }
    }
}
