namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Shared data-file path resolution used by every JSON store.
    ///
    /// Resolution (highest to lowest precedence):
    ///   1. The store-specific config key (e.g. BestInScript:DataFilePath)
    ///      if set AND absolute — used as-is.
    ///   2. BestInScript:DataDirectory + (specific value or the default file name).
    ///   3. AppContext.BaseDirectory + (specific value or the default file name).
    /// </summary>
    public static class DataFilePathResolver
    {
        public static string Resolve(
            IConfiguration config, string specificKey, string defaultFileName)
        {
            var specific = config[specificKey];
            var dataDir = config["BestInScript:DataDirectory"];

            // 1. Explicit absolute path wins.
            if (!string.IsNullOrWhiteSpace(specific) && Path.IsPathRooted(specific))
                return Path.GetFullPath(specific);

            // 2. Otherwise, combine directory + filename.
            var fileName = string.IsNullOrWhiteSpace(specific) ? defaultFileName : specific;
            var directory = string.IsNullOrWhiteSpace(dataDir)
                ? AppContext.BaseDirectory
                : dataDir;

            return Path.GetFullPath(Path.Combine(directory, fileName));
        }
    }
}
