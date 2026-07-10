using System.Collections.Concurrent;
using BestInScript.API.Models;
using BestInScript.API.Services;

namespace BestInScript.API.Engine
{
    /// <summary>
    /// Owns the script/preset registries and the ownership model.
    ///
    /// A script runs iff at least one "owner" has it claimed. Owners are:
    ///   • <see cref="UserOwnerId"/> — added when the user presses the script's
    ///     own trigger key directly, removed on the next press.
    ///   • Any active preset's <c>Preset.Id</c> — added for every member when a
    ///     preset is toggled on, removed when it is toggled off.
    /// A script starts when its owner set goes from empty to non-empty and stops
    /// when it goes from non-empty to empty. Two presets sharing a script both
    /// own it, so turning one off does not stop the script while the other is
    /// still on.
    ///
    /// All owner-set mutations and start/stop transitions happen under
    /// <see cref="_toggleLock"/>.
    /// </summary>
    public sealed class ScriptCoordinator
    {
        private readonly ILogger<ScriptCoordinator> _logger;
        private readonly IScriptRunner _runner;
        private readonly PixelCaptureService _capture;

        /// <summary>Sentinel owner id meaning "the user pressed this script's own trigger key directly".</summary>
        public static readonly Guid UserOwnerId = Guid.Empty;

        // vkCode → script entry
        private readonly ConcurrentDictionary<ushort, ScriptEntry> _scriptRegistry = new();
        // vkCode → preset entry
        private readonly ConcurrentDictionary<ushort, PresetEntry> _presetRegistry = new();

        // Guards all owner-set mutations + start/stop transitions.
        private readonly object _toggleLock = new();

        public ScriptCoordinator(ILogger<ScriptCoordinator> logger, IScriptRunner runner, PixelCaptureService capture)
        {
            _logger = logger;
            _runner = runner;
            _capture = capture;
        }

        public int ScriptCount => _scriptRegistry.Count;
        public int PresetCount => _presetRegistry.Count;

        // ── Scripts ────────────────────────────────────────────────────────────

        /// <summary>Add or replace a script. Stops any currently running version first.</summary>
        public void RegisterScript(ScriptConfig config)
        {
            var vk = InputSimulatorService.ResolveVk(config.TriggerKey);
            if (vk == 0)
            {
                _logger.LogWarning("RegisterScript: cannot resolve trigger key '{Key}'", config.TriggerKey);
                return;
            }

            lock (_toggleLock)
            {
                // Remove any existing entry mapped to this id (may be on a different VK if trigger changed).
                // We drop its run state entirely — preset ownership for this script id is re-applied below
                // so an active preset that owns this script keeps it running across edits.
                var old = _scriptRegistry.Values.FirstOrDefault(e => e.Config.Id == config.Id);
                if (old != null)
                {
                    StopEntry(old);
                    old.Owners.Clear();
                    _scriptRegistry.TryRemove(InputSimulatorService.ResolveVk(old.Config.TriggerKey), out _);
                }

                if (_presetRegistry.ContainsKey(vk))
                    _logger.LogWarning(
                        "RegisterScript: trigger key '{Key}' (VK=0x{VK:X2}) is also bound to a preset. " +
                        "The script will take precedence in the hook callback.",
                        config.TriggerKey, vk);

                var entry = new ScriptEntry(config);

                // Re-apply ownership from any currently-active preset that lists this script.
                foreach (var preset in _presetRegistry.Values)
                {
                    if (preset.IsActive && preset.Config.ScriptIds.Contains(config.Id))
                        entry.Owners.Add(preset.Config.Id);
                }

                _scriptRegistry[vk] = entry;
                EnsureRunState(entry);

                _logger.LogInformation(
                    "Registered script '{Name}' on key '{Key}' (VK=0x{VK:X2})",
                    config.Name, config.TriggerKey, vk);
            }
        }

        /// <summary>Remove a script, stopping it first if running.</summary>
        public void UnregisterScript(Guid id)
        {
            lock (_toggleLock)
            {
                var entry = _scriptRegistry.Values.FirstOrDefault(e => e.Config.Id == id);
                if (entry == null) return;

                StopEntry(entry);
                entry.Owners.Clear();
                _scriptRegistry.TryRemove(InputSimulatorService.ResolveVk(entry.Config.TriggerKey), out _);
                _logger.LogInformation("Unregistered script '{Name}'", entry.Config.Name);
            }
        }

        // ── Presets ────────────────────────────────────────────────────────────

        /// <summary>Add or replace a preset. If a previous version was active, it is deactivated first
        /// (releasing its claim on member scripts) so the new config starts inactive.</summary>
        public void RegisterPreset(Preset preset)
        {
            var vk = InputSimulatorService.ResolveVk(preset.TriggerKey);
            if (vk == 0)
            {
                _logger.LogWarning("RegisterPreset: cannot resolve trigger key '{Key}'", preset.TriggerKey);
                return;
            }

            lock (_toggleLock)
            {
                var old = _presetRegistry.Values.FirstOrDefault(e => e.Config.Id == preset.Id);
                if (old != null)
                {
                    if (old.IsActive)
                        DeactivatePresetLocked(old);
                    _presetRegistry.TryRemove(InputSimulatorService.ResolveVk(old.Config.TriggerKey), out _);
                }

                if (_scriptRegistry.ContainsKey(vk))
                    _logger.LogWarning(
                        "RegisterPreset: trigger key '{Key}' (VK=0x{VK:X2}) is also bound to a script. " +
                        "The script will take precedence in the hook callback.",
                        preset.TriggerKey, vk);

                _presetRegistry[vk] = new PresetEntry(preset);

                _logger.LogInformation(
                    "Registered preset '{Name}' on key '{Key}' (VK=0x{VK:X2}) with {Count} member(s)",
                    preset.Name, preset.TriggerKey, vk, preset.ScriptIds.Count);
            }
        }

        /// <summary>Remove a preset, deactivating it first if active.</summary>
        public void UnregisterPreset(Guid id)
        {
            lock (_toggleLock)
            {
                var entry = _presetRegistry.Values.FirstOrDefault(e => e.Config.Id == id);
                if (entry == null) return;

                if (entry.IsActive)
                    DeactivatePresetLocked(entry);
                _presetRegistry.TryRemove(InputSimulatorService.ResolveVk(entry.Config.TriggerKey), out _);
                _logger.LogInformation("Unregistered preset '{Name}'", entry.Config.Name);
            }
        }

        // ── Status ─────────────────────────────────────────────────────────────

        /// <summary>Returns a snapshot of runtime status for all registered scripts.</summary>
        public IEnumerable<ScriptStatus> GetStatus()
        {
            lock (_toggleLock)
            {
                return _scriptRegistry.Values.Select(e => new ScriptStatus
                {
                    Id = e.Config.Id,
                    Name = e.Config.Name,
                    TriggerKey = e.Config.TriggerKey,
                    IsRunning = e.Owners.Count > 0,
                    ShowInOverlay = e.Config.ShowInOverlay,
                    HasPixelTrigger = e.Config.PixelTrigger != null,
                    PixelState = e.PixelState,
                    OverlayColor = e.Config.OverlayColor,
                    OverlayIcon = e.Config.OverlayIcon
                }).ToList();
            }
        }

        /// <summary>Returns a snapshot of runtime status for all registered presets.</summary>
        public IEnumerable<PresetStatus> GetPresetStatus()
        {
            lock (_toggleLock)
            {
                return _presetRegistry.Values.Select(e => new PresetStatus
                {
                    Id = e.Config.Id,
                    Name = e.Config.Name,
                    TriggerKey = e.Config.TriggerKey,
                    IsActive = e.IsActive,
                    ShowInOverlay = e.Config.ShowInOverlay,
                    ScriptIds = new List<Guid>(e.Config.ScriptIds),
                    OverlayColor = e.Config.OverlayColor,
                    OverlayIcon = e.Config.OverlayIcon
                }).ToList();
            }
        }

        /// <summary>Stops all running scripts immediately. Deactivates all presets.</summary>
        public void StopAll()
        {
            lock (_toggleLock)
            {
                foreach (var preset in _presetRegistry.Values)
                    if (preset.IsActive) preset.IsActive = false;

                foreach (var entry in _scriptRegistry.Values)
                {
                    entry.Owners.Clear();
                    StopEntry(entry);
                }
            }
        }

        /// <summary>
        /// Shutdown path: cancel every running task WITHOUT clearing owners or
        /// deactivating presets — distinct from <see cref="StopAll"/>, which is
        /// the user-facing emergency stop.
        /// </summary>
        public void CancelAllRunning()
        {
            foreach (var entry in _scriptRegistry.Values)
                StopEntry(entry);
        }

        /// <summary>
        /// Drop every registration entirely: stop all runs, clear owners, and empty
        /// both registries. Used when switching profiles, before the new profile's
        /// scripts/presets are loaded. Unlike <see cref="StopAll"/> this also removes
        /// the entries themselves so a different config can be loaded cleanly.
        /// </summary>
        public void ClearAll()
        {
            lock (_toggleLock)
            {
                foreach (var entry in _scriptRegistry.Values)
                {
                    entry.Owners.Clear();
                    StopEntry(entry);
                }
                _scriptRegistry.Clear();
                _presetRegistry.Clear();
            }
        }

        // ── Key routing ────────────────────────────────────────────────────────

        /// <summary>
        /// Routes a key press to whichever script or preset claims that VK.
        /// Scripts take precedence if both exist on the same VK — but validation
        /// at the API layer prevents that case in normal use.
        /// </summary>
        public void HandleTriggerKey(ushort vkCode)
        {
            // While a guided in-game capture is armed, its hotkey is consumed here — it
            // samples the screen instead of toggling any script/preset bound to the same key.
            if (_capture.TryConsumeKey(vkCode))
                return;

            if (_scriptRegistry.TryGetValue(vkCode, out var script))
            {
                ToggleUserOwnership(script);
                return;
            }
            if (_presetRegistry.TryGetValue(vkCode, out var preset))
            {
                TogglePreset(preset);
            }
        }

        // ── Toggle logic ───────────────────────────────────────────────────────

        /// <summary>Flip the user's direct claim on a script (the legacy "press to toggle" behavior).</summary>
        private void ToggleUserOwnership(ScriptEntry entry)
        {
            lock (_toggleLock)
            {
                if (entry.Owners.Contains(UserOwnerId))
                {
                    entry.Owners.Remove(UserOwnerId);
                    _logger.LogInformation(
                        "User released script '{Name}' (remaining owners: {Owners})",
                        entry.Config.Name, entry.Owners.Count);
                }
                else
                {
                    entry.Owners.Add(UserOwnerId);
                    _logger.LogInformation(
                        "User claimed script '{Name}' (total owners: {Owners})",
                        entry.Config.Name, entry.Owners.Count);
                }
                EnsureRunState(entry);
            }
        }

        /// <summary>Toggle a preset on/off, propagating ownership to every member script.</summary>
        private void TogglePreset(PresetEntry preset)
        {
            lock (_toggleLock)
            {
                if (preset.IsActive)
                {
                    DeactivatePresetLocked(preset);
                    _logger.LogInformation("Deactivated preset '{Name}'", preset.Config.Name);
                }
                else
                {
                    ActivatePresetLocked(preset);
                    _logger.LogInformation(
                        "Activated preset '{Name}' ({Count} member(s))",
                        preset.Config.Name, preset.Config.ScriptIds.Count);
                }
            }
        }

        // Caller MUST hold _toggleLock.
        private void ActivatePresetLocked(PresetEntry preset)
        {
            preset.IsActive = true;
            foreach (var scriptId in preset.Config.ScriptIds)
            {
                var entry = _scriptRegistry.Values.FirstOrDefault(e => e.Config.Id == scriptId);
                if (entry == null)
                {
                    _logger.LogDebug(
                        "Preset '{Preset}': member script {Id} not registered — skipping",
                        preset.Config.Name, scriptId);
                    continue;
                }
                entry.Owners.Add(preset.Config.Id);
                EnsureRunState(entry);
            }
        }

        // Caller MUST hold _toggleLock.
        private void DeactivatePresetLocked(PresetEntry preset)
        {
            preset.IsActive = false;
            foreach (var scriptId in preset.Config.ScriptIds)
            {
                var entry = _scriptRegistry.Values.FirstOrDefault(e => e.Config.Id == scriptId);
                if (entry == null) continue;
                entry.Owners.Remove(preset.Config.Id);
                EnsureRunState(entry);
            }
        }

        /// <summary>
        /// Reconcile a script's run state against its owner set:
        /// owners non-empty + not running → start; owners empty + running → stop.
        /// Caller MUST hold _toggleLock.
        /// </summary>
        private void EnsureRunState(ScriptEntry entry)
        {
            bool shouldRun = entry.Owners.Count > 0;
            bool isRunning = entry.Cts != null && !entry.Cts.IsCancellationRequested;

            if (shouldRun && !isRunning)
            {
                _logger.LogInformation("Starting script '{Name}'", entry.Config.Name);
                var cts = new CancellationTokenSource();
                entry.Cts = cts;
                _ = _runner.RunAsync(entry, cts.Token);
            }
            else if (!shouldRun && isRunning)
            {
                _logger.LogInformation("Stopping script '{Name}'", entry.Config.Name);
                StopEntry(entry);
            }
        }

        private static void StopEntry(ScriptEntry entry)
        {
            var cts = entry.Cts;
            if (cts == null) return;

            try { cts.Cancel(); }
            catch { /* already cancelled */ }
            entry.Cts = null;
        }
    }
}
