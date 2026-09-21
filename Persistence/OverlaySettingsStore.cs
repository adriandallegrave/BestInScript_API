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
        private readonly ConfigSnapshotService _snapshots;
        private readonly object _lock = new();
        private OverlaySettings _current;

        public event Action<OverlaySettings>? Changed;

        public OverlaySettingsStore(
            IConfiguration config,
            ILogger<OverlaySettingsStore> logger,
            ConfigSnapshotService snapshots)
        {
            _logger = logger;
            _snapshots = snapshots;
            _path = DataFilePathResolver.Resolve(
                config, "BestInScript:OverlaySettingsPath", DefaultFileName);
            EnsureDirectory(_path);
            _logger.LogInformation("Overlay settings file: {Path}", _path);

            _current = Load();
        }

        /// <summary>Fully resolved path of overlay-settings.json. Fixed for the process —
        /// these settings are global, not profile-scoped, so there is no Rebind.</summary>
        public string FilePath => _path;

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

            // Same reasoning for the build panel: nothing else bounds its size, and it
            // renders a full-size image rather than a couple of text rows.
            settings.BuildPanel ??= new BuildPanelConfig();
            settings.BuildPanel.Opacity = Math.Clamp(settings.BuildPanel.Opacity, 0.10, 1.00);
            settings.BuildPanel.FontSize = Math.Clamp(settings.BuildPanel.FontSize, 8, 32);
            settings.BuildPanel.Margin = Math.Clamp(settings.BuildPanel.Margin, 0, 400);
            settings.BuildPanel.MaxWidth = Math.Clamp(settings.BuildPanel.MaxWidth, 100, 4000);
            settings.BuildPanel.MaxHeight = Math.Clamp(settings.BuildPanel.MaxHeight, 100, 4000);

            OverlaySettings snapshot;
            lock (_lock)
            {
                _current = settings;
                snapshot = Clone(_current);
                try
                {
                    EnsureDirectory(_path);

                    // BACKLOG 3.4. This PUT is a full replace with no merge, so a web-UI save
                    // that forgets to spread the current settings silently drops fields — the
                    // exact bug fixed in v1.14.0. Keep the previous file so it can be undone.
                    _snapshots.Capture(_path);

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

        /// <summary>
        /// Re-read the file from disk, replacing the cached settings, and publish them through
        /// <see cref="Changed"/>. Used after a snapshot restore rewrites the file underneath us.
        /// Firing <see cref="Changed"/> is load-bearing: it is what re-arms the global hotkeys
        /// (HotkeyEngine.ApplyGlobalHotkeys) and restyles the live overlay windows. Without it a
        /// restored file would sit on disk with nothing reading it until the next restart.
        /// </summary>
        public void Reload()
        {
            OverlaySettings snapshot;
            lock (_lock)
            {
                _current = Load();
                snapshot = Clone(_current);
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
            Legion = CloneEvent(s.Legion),
            BuildPanel = CloneBuildPanel(s.BuildPanel)
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

        private static BuildPanelConfig CloneBuildPanel(BuildPanelConfig? b)
        {
            b ??= new BuildPanelConfig();
            return new BuildPanelConfig
            {
                Enabled = b.Enabled,
                CycleHotkey = b.CycleHotkey,
                ScreenIndex = b.ScreenIndex,
                Anchor = b.Anchor,
                Margin = b.Margin,
                PositionX = b.PositionX,
                PositionY = b.PositionY,
                MaxWidth = b.MaxWidth,
                MaxHeight = b.MaxHeight,
                Opacity = b.Opacity,
                FontSize = b.FontSize
            };
        }
    }
}