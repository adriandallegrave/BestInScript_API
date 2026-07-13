using System.Text.Json;
using BestInScript.API.Models;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Loads / saves <see cref="OverlaySettings"/> to a JSON file.
    ///
    /// File location resolution (highest to lowest precedence):
    ///   1. BestInScript:OverlaySettingsPath if set AND absolute.
    ///   2. BestInScript:DataDirectory + (OverlaySettingsPath or "overlay-settings.json").
    ///   3. AppContext.BaseDirectory + (OverlaySettingsPath or "overlay-settings.json").
    ///
    /// Mirrors <see cref="ScriptRepository"/> so both files land in the same
    /// directory unless one is explicitly overridden.
    ///
    /// Raises <see cref="Changed"/> whenever Save() is called so the live
    /// overlay window can reposition itself without a restart.
    /// </summary>
    public sealed class OverlaySettingsStore
    {
        private const string DefaultFileName = "overlay-settings.json";

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
            _path = DataFilePathResolver.Resolve(
                config, "BestInScript:OverlaySettingsPath", DefaultFileName);
            EnsureDirectory(_path);
            _logger.LogInformation("Overlay settings file: {Path}", _path);

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
                    EnsureDirectory(_path);
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

        private void EnsureDirectory(string filePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not create overlay-settings directory for '{Path}'. Saves may fail.",
                    filePath);
            }
        }

        private static OverlaySettings Clone(OverlaySettings s) => new()
        {
            Enabled = s.Enabled,
            ScreenIndex = s.ScreenIndex,
            Anchor = s.Anchor,
            Margin = s.Margin,
            PositionX = s.PositionX,
            PositionY = s.PositionY,
            Opacity = s.Opacity,
            FontSize = s.FontSize,
            HideWhenIdle = s.HideWhenIdle,
            StopAllHotkey = s.StopAllHotkey,
            EventsEnabled = s.EventsEnabled,
            WorldBoss = CloneEvent(s.WorldBoss),
            Helltide = CloneEvent(s.Helltide),
            Legion = CloneEvent(s.Legion)
        };

        private static EventOverlayConfig CloneEvent(EventOverlayConfig? e)
        {
            e ??= new EventOverlayConfig();
            return new EventOverlayConfig
            {
                Show = e.Show,
                AlarmEnabled = e.AlarmEnabled,
                WarningLeadMinutes = e.WarningLeadMinutes,
                AlarmLeadMinutes = e.AlarmLeadMinutes,
                Color = e.Color is { Length: 3 }
                    ? new[] { e.Color[0], e.Color[1], e.Color[2] }
                    : null,
                WarningColor = e.WarningColor is { Length: 3 }
                    ? new[] { e.WarningColor[0], e.WarningColor[1], e.WarningColor[2] }
                    : null
            };
        }
    }
}