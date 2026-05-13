using System.IO;
using System.Text.Json;

namespace BestInScript.API.Overlay
{
    /// <summary>
    /// Loads / saves <see cref="OverlaySettings"/> to a JSON file beside the
    /// application's other data (same folder strategy as ScriptRepository).
    ///
    /// Raises <see cref="Changed"/> whenever Save() is called so the live
    /// overlay window can reposition itself without a restart.
    /// </summary>
    public sealed class OverlaySettingsStore
    {
        private readonly string _path;
        private readonly ILogger<OverlaySettingsStore> _logger;
        private readonly object _lock = new();
        private OverlaySettings _current;

        public event Action<OverlaySettings>? Changed;

        public OverlaySettingsStore(
            IConfiguration config,
            ILogger<OverlaySettingsStore> logger)
        {
            _logger = logger;
            var configured = config["BestInScript:OverlaySettingsPath"];
            _path = !string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(configured)
                : Path.Combine(AppContext.BaseDirectory, "overlay-settings.json");

            _current = Load();
        }

        public OverlaySettings Get()
        {
            lock (_lock) return Clone(_current);
        }

        public void Save(OverlaySettings settings)
        {
            // Clamp values to sane ranges so a bad PUT can't make the overlay
            // invisible or absurdly huge.
            settings.Opacity = Math.Clamp(settings.Opacity, 0.10, 1.00);
            settings.FontSize = Math.Clamp(settings.FontSize, 8, 32);
            settings.Margin = Math.Clamp(settings.Margin, 0, 400);

            OverlaySettings snapshot;
            lock (_lock)
            {
                _current = settings;
                snapshot = Clone(_current);
                try
                {
                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_path, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write overlay-settings.json");
                }
            }

            try { Changed?.Invoke(snapshot); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OverlaySettingsStore.Changed handler threw");
            }
        }

        private OverlaySettings Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var loaded = JsonSerializer.Deserialize<OverlaySettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load overlay-settings.json; using defaults");
            }
            return new OverlaySettings();
        }

        private static OverlaySettings Clone(OverlaySettings s) => new()
        {
            Enabled = s.Enabled,
            ScreenIndex = s.ScreenIndex,
            Anchor = s.Anchor,
            Margin = s.Margin,
            Opacity = s.Opacity,
            FontSize = s.FontSize,
            HideWhenIdle = s.HideWhenIdle
        };
    }
}