using BestInScript.API.Models;
using BestInScript.API.Persistence;

namespace BestInScript.API.Engine
{
    /// <summary>
    /// Hosted-service façade over the engine parts:
    ///   • <see cref="KeyboardHook"/> — global WH_KEYBOARD_LL hook thread.
    ///     The trigger key is NOT suppressed – the game still receives it.
    ///   • <see cref="ScriptCoordinator"/> — registries + ownership model.
    ///   • <see cref="ScriptExecutor"/> (via <see cref="IScriptRunner"/>) —
    ///     blind-loop / pixel-gated execution.
    ///
    /// Controllers and the overlay talk to this class; it delegates.
    /// </summary>
    public class HotkeyEngine : IHostedService, IDisposable
    {
        private readonly ILogger<HotkeyEngine> _logger;
        private readonly IScriptRepository _repo;
        private readonly IPresetRepository _presetRepo;
        private readonly KeyboardHook _hook;
        private readonly ScriptCoordinator _coordinator;

        public HotkeyEngine(
            ILogger<HotkeyEngine> logger,
            IScriptRepository repo,
            IPresetRepository presetRepo,
            KeyboardHook hook,
            ScriptCoordinator coordinator)
        {
            _logger = logger;
            _repo = repo;
            _presetRepo = presetRepo;
            _hook = hook;
            _coordinator = coordinator;
        }

        // ── IHostedService ─────────────────────────────────────────────────────
        public Task StartAsync(CancellationToken cancellationToken)
        {
            LoadFromRepository();

            _hook.KeyPressed += _coordinator.HandleTriggerKey;
            _hook.Start();

            _logger.LogInformation(
                "HotkeyEngine started. {Scripts} script(s), {Presets} preset(s) registered.",
                _coordinator.ScriptCount, _coordinator.PresetCount);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("HotkeyEngine stopping…");

            // Cancel all running scripts (owners stay — this is shutdown, not stop-all),
            // then signal the hook thread's message loop to exit.
            _coordinator.CancelAllRunning();
            _hook.Stop();
            return Task.CompletedTask;
        }

        public void Dispose() { /* resources freed in StopAsync / hook thread */ }

        // ── Delegations (public surface unchanged) ─────────────────────────────

        /// <summary>Add or replace a script. Stops any currently running version first.</summary>
        public void RegisterScript(ScriptConfig config) => _coordinator.RegisterScript(config);

        /// <summary>Remove a script, stopping it first if running.</summary>
        public void UnregisterScript(Guid id) => _coordinator.UnregisterScript(id);

        /// <summary>Add or replace a preset. A previously active version is deactivated first.</summary>
        public void RegisterPreset(Preset preset) => _coordinator.RegisterPreset(preset);

        /// <summary>Remove a preset, deactivating it first if active.</summary>
        public void UnregisterPreset(Guid id) => _coordinator.UnregisterPreset(id);

        /// <summary>Returns a snapshot of runtime status for all registered scripts.</summary>
        public IEnumerable<ScriptStatus> GetStatus() => _coordinator.GetStatus();

        /// <summary>Returns a snapshot of runtime status for all registered presets.</summary>
        public IEnumerable<PresetStatus> GetPresetStatus() => _coordinator.GetPresetStatus();

        /// <summary>Stops all running scripts immediately. Deactivates all presets.</summary>
        public void StopAll() => _coordinator.StopAll();

        private void LoadFromRepository()
        {
            // Order matters: load scripts first so presets can find their members.
            foreach (var s in _repo.GetAll())
                _coordinator.RegisterScript(s);

            foreach (var p in _presetRepo.GetAll())
                _coordinator.RegisterPreset(p);
        }
    }
}
