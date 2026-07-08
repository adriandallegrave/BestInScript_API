using System.Text.Json;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Owns the set of named config profiles and which one is active.
    ///
    /// A profile is a directory under <c>&lt;baseDir&gt;/profiles/&lt;name&gt;/</c> holding one
    /// copy of every <see cref="IProfileScopedStore"/>'s file (scripts.json, presets.json).
    /// The active profile is remembered in <c>&lt;baseDir&gt;/profiles.json</c>. The global
    /// overlay-settings file stays at the base directory and is NOT part of a profile.
    ///
    /// On construction this migrates any pre-profiles setup (loose scripts.json / presets.json
    /// in the base directory) into a <c>Default</c> profile, then points every scoped store at
    /// the active profile's directory. Switching (see <see cref="Activate"/>) repoints the
    /// stores; the caller (<c>HotkeyEngine</c>) is responsible for reloading the engine registry.
    /// </summary>
    public sealed class ProfileManager
    {
        public const string DefaultProfileName = "Default";
        private const string PointerFileName = "profiles.json";
        private const int MaxNameLength = 64;

        private readonly IReadOnlyList<IProfileScopedStore> _stores;
        private readonly ILogger<ProfileManager> _logger;
        private readonly object _lock = new();

        private readonly string _baseDir;
        private readonly string _profilesRoot;
        private readonly string _pointerPath;

        private string _active = DefaultProfileName;

        public ProfileManager(
            IConfiguration config,
            IEnumerable<IProfileScopedStore> stores,
            ILogger<ProfileManager> logger)
        {
            _stores = stores.ToList();
            _logger = logger;

            _baseDir = DataFilePathResolver.ResolveBaseDirectory(config);
            _profilesRoot = Path.Combine(_baseDir, "profiles");
            _pointerPath = Path.Combine(_baseDir, PointerFileName);

            Initialize();
        }

        /// <summary>Name of the currently active profile.</summary>
        public string Active
        {
            get { lock (_lock) return _active; }
        }

        /// <summary>All profile names, <c>Default</c> first then alphabetical (case-insensitive).</summary>
        public IReadOnlyList<string> List()
        {
            lock (_lock) return ListLocked();
        }

        public bool Exists(string name)
        {
            lock (_lock) return ExistsLocked(name);
        }

        // ── Mutations ────────────────────────────────────────────────────────────

        /// <summary>
        /// Create a new empty profile (optionally seeded with a copy of the active
        /// profile's files). Does NOT switch to it. Returns an error message on
        /// invalid/duplicate name, or null on success.
        /// </summary>
        public string? Create(string name, bool copyFromCurrent)
        {
            lock (_lock)
            {
                var error = ValidateName(name);
                if (error != null) return error;
                name = name.Trim();

                if (ExistsLocked(name))
                    return $"A profile named '{name}' already exists.";

                var dir = ProfileDir(name);
                Directory.CreateDirectory(dir);

                if (copyFromCurrent)
                {
                    var src = ProfileDir(_active);
                    foreach (var store in _stores)
                    {
                        var from = Path.Combine(src, store.ProfileFileName);
                        if (File.Exists(from))
                            File.Copy(from, Path.Combine(dir, store.ProfileFileName), overwrite: true);
                    }
                }

                _logger.LogInformation("Created profile '{Name}' (copyFromCurrent={Copy})", name, copyFromCurrent);
                return null;
            }
        }

        /// <summary>
        /// Make <paramref name="name"/> the active profile: repoint every scoped store at its
        /// directory and persist the pointer. Throws if the profile does not exist. Does NOT
        /// reload the engine — the caller does that after this returns.
        /// </summary>
        public void Activate(string name)
        {
            lock (_lock)
            {
                if (!ExistsLocked(name))
                    throw new InvalidOperationException($"Profile '{name}' does not exist.");

                _active = CanonicalName(name);
                RebindStoresLocked(_active);
                SavePointerLocked();
                _logger.LogInformation("Activated profile '{Name}'", _active);
            }
        }

        /// <summary>Rename a profile. If it is the active one, the pointer and store bindings
        /// follow. Returns an error message or null on success.</summary>
        public string? Rename(string oldName, string newName)
        {
            lock (_lock)
            {
                if (!ExistsLocked(oldName))
                    return $"Profile '{oldName}' does not exist.";

                var error = ValidateName(newName);
                if (error != null) return error;
                newName = newName.Trim();

                var canonicalOld = CanonicalName(oldName);
                bool sameNameDifferentCase =
                    string.Equals(canonicalOld, newName, StringComparison.OrdinalIgnoreCase);

                if (ExistsLocked(newName) && !sameNameDifferentCase)
                    return $"A profile named '{newName}' already exists.";

                var from = ProfileDir(canonicalOld);
                var to = ProfileDir(newName);

                if (sameNameDifferentCase)
                {
                    // Case-only rename: Directory.Move refuses same path, so bounce via a temp dir.
                    var tmp = ProfileDir(canonicalOld + "__rename_" + Guid.NewGuid().ToString("N"));
                    Directory.Move(from, tmp);
                    Directory.Move(tmp, to);
                }
                else
                {
                    Directory.Move(from, to);
                }

                if (string.Equals(_active, canonicalOld, StringComparison.OrdinalIgnoreCase))
                {
                    _active = newName;
                    RebindStoresLocked(_active);
                    SavePointerLocked();
                }

                _logger.LogInformation("Renamed profile '{Old}' → '{New}'", canonicalOld, newName);
                return null;
            }
        }

        /// <summary>Delete a profile. The active profile and the last remaining profile cannot be
        /// deleted. Returns an error message or null on success.</summary>
        public string? Delete(string name)
        {
            lock (_lock)
            {
                if (!ExistsLocked(name))
                    return $"Profile '{name}' does not exist.";

                if (string.Equals(_active, name, StringComparison.OrdinalIgnoreCase))
                    return "Cannot delete the active profile. Switch to another profile first.";

                if (ListLocked().Count <= 1)
                    return "Cannot delete the only remaining profile.";

                Directory.Delete(ProfileDir(CanonicalName(name)), recursive: true);
                _logger.LogInformation("Deleted profile '{Name}'", name);
                return null;
            }
        }

        // ── Initialization / migration ───────────────────────────────────────────

        private void Initialize()
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_profilesRoot);

                // First run after upgrade: no profiles yet. Create Default and pull any loose
                // pre-profiles files (base-dir scripts.json / presets.json) into it.
                if (ListLocked().Count == 0)
                {
                    var defaultDir = ProfileDir(DefaultProfileName);
                    Directory.CreateDirectory(defaultDir);

                    foreach (var store in _stores)
                    {
                        var legacy = Path.Combine(_baseDir, store.ProfileFileName);
                        var dest = Path.Combine(defaultDir, store.ProfileFileName);
                        if (File.Exists(legacy) && !File.Exists(dest))
                        {
                            try
                            {
                                File.Move(legacy, dest);
                                _logger.LogInformation(
                                    "Migrated '{File}' into the Default profile", store.ProfileFileName);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex,
                                    "Could not migrate '{File}' into the Default profile", store.ProfileFileName);
                            }
                        }
                    }
                }

                // Resolve the active profile from the pointer, repairing if it is missing/stale.
                var wanted = LoadPointerLocked();
                var list = ListLocked();
                _active = (wanted != null && ExistsLocked(wanted))
                    ? CanonicalName(wanted)
                    : list.First(); // ListLocked guarantees Default exists after the block above

                RebindStoresLocked(_active);
                SavePointerLocked();

                _logger.LogInformation(
                    "Profiles ready. Active: '{Active}'. Available: {All}",
                    _active, string.Join(", ", list));
            }
        }

        // ── Locked helpers (caller holds _lock) ──────────────────────────────────

        private List<string> ListLocked()
        {
            if (!Directory.Exists(_profilesRoot))
                return [];

            var names = Directory.GetDirectories(_profilesRoot)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

            // Default first, then case-insensitive alphabetical.
            names.Sort((a, b) =>
            {
                bool ad = string.Equals(a, DefaultProfileName, StringComparison.OrdinalIgnoreCase);
                bool bd = string.Equals(b, DefaultProfileName, StringComparison.OrdinalIgnoreCase);
                if (ad != bd) return ad ? -1 : 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return names;
        }

        private bool ExistsLocked(string name)
            => !string.IsNullOrWhiteSpace(name)
               && ListLocked().Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Returns the on-disk casing of an existing profile name.</summary>
        private string CanonicalName(string name)
            => ListLocked().FirstOrDefault(
                   n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? name.Trim();

        private string ProfileDir(string name) => Path.Combine(_profilesRoot, name);

        private void RebindStoresLocked(string name)
        {
            var dir = ProfileDir(name);
            Directory.CreateDirectory(dir);
            foreach (var store in _stores)
                store.Rebind(Path.Combine(dir, store.ProfileFileName));
        }

        private string? LoadPointerLocked()
        {
            try
            {
                if (File.Exists(_pointerPath))
                {
                    var json = File.ReadAllText(_pointerPath);
                    var p = JsonSerializer.Deserialize<Pointer>(json);
                    if (!string.IsNullOrWhiteSpace(p?.Active)) return p!.Active;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read {File}; falling back to Default", PointerFileName);
            }
            return null;
        }

        private void SavePointerLocked()
        {
            try
            {
                var json = JsonSerializer.Serialize(
                    new Pointer { Active = _active }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_pointerPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not write {File}", PointerFileName);
            }
        }

        private static string? ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Profile name is required.";

            name = name.Trim();

            if (name.Length > MaxNameLength)
                return $"Profile name must be {MaxNameLength} characters or fewer.";

            if (name is "." or "..")
                return "Invalid profile name.";

            var invalid = Path.GetInvalidFileNameChars();
            if (name.IndexOfAny(invalid) >= 0 || name.Contains('/') || name.Contains('\\'))
                return "Profile name cannot contain path separators or these characters: " +
                       new string(invalid.Where(c => !char.IsControl(c)).ToArray());

            return null;
        }

        private sealed class Pointer
        {
            public string Active { get; set; } = DefaultProfileName;
        }
    }
}
