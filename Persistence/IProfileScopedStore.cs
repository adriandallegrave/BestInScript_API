namespace BestInScript.API.Persistence
{
    /// <summary>
    /// A data store whose backing file is swapped when the active profile
    /// changes. Implemented by the per-profile stores (scripts, presets); the
    /// global overlay-settings store deliberately does NOT implement it.
    ///
    /// <see cref="ProfileManager"/> owns these stores and points them at the
    /// active profile's directory at startup and on every switch.
    /// </summary>
    public interface IProfileScopedStore
    {
        /// <summary>The bare file name this store uses inside a profile directory (e.g. "scripts.json").</summary>
        string ProfileFileName { get; }

        /// <summary>Repoint the store at a new absolute file path (and reload any cached state).</summary>
        void Rebind(string absolutePath);
    }
}
