namespace BestInScript.API.Services
{
    /// <summary>
    /// Resolves files under <c>wwwroot</c>. No static-file middleware is used,
    /// so the handful of assets (index.html, app.ico) are located manually:
    /// project root under <c>dotnet run</c> (BaseDirectory is bin\...\net10.0\),
    /// beside the exe when published.
    /// </summary>
    public static class WebAssetLocator
    {
        /// <summary>Full path of <c>wwwroot/&lt;fileName&gt;</c>, or null if absent.</summary>
        public static string? Find(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", fileName),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", fileName),
            };
            return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        }
    }
}
