using BestInScript.API.Models;
using BestInScript.API.Services;

namespace BestInScript.API.Engine
{
    /// <summary>
    /// Runs a toggled-on script until cancelled: either the blind loop or the
    /// pixel-gated loop, both driving <see cref="IInputSimulator"/> with the
    /// mandatory humanlike randomized delays between steps.
    /// </summary>
    public sealed class ScriptExecutor : IScriptRunner
    {
        private readonly ILogger<ScriptExecutor> _logger;
        private readonly IInputSimulator _inputSim;
        private readonly IScreenSampler _screen;
        private readonly IDelayScheduler _delays;
        private readonly IRandomSource _random;

        public ScriptExecutor(
            ILogger<ScriptExecutor> logger,
            IInputSimulator inputSim,
            IScreenSampler screen,
            IDelayScheduler delays,
            IRandomSource random)
        {
            _logger = logger;
            _inputSim = inputSim;
            _screen = screen;
            _delays = delays;
            _random = random;
        }

        /// <summary>
        /// Entry point for a toggled-on script. Dispatches to the blind loop or
        /// the pixel-gated loop depending on whether a PixelTrigger is set.
        /// </summary>
        public Task RunAsync(ScriptEntry entry, CancellationToken ct)
            => entry.Config.PixelTrigger is null
                ? RunBlindLoopAsync(entry, ct)
                : RunPixelGatedAsync(entry, ct);

        /// <summary>
        /// Default mode: loop the step sequence continuously until cancelled.
        /// Held keys persist across steps AND loop iterations — a key listed in
        /// consecutive steps stays physically down instead of fluttering.
        /// </summary>
        private async Task RunBlindLoopAsync(ScriptEntry entry, CancellationToken ct)
        {
            var config = entry.Config;
            var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                while (!ct.IsCancellationRequested)
                    await RunStepsOnceAsync(config, heldKeys, ct);
            }
            catch (OperationCanceledException)
            {
                // Normal stop path – swallow
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in script '{Name}'", config.Name);
            }
            finally
            {
                ReleaseHeld(heldKeys);
                entry.Cts = null;
            }
        }

        /// <summary>
        /// Pixel-gated mode: watch one screen pixel and run the step sequence
        /// when the pixel reads as "ready". Behavior depends on
        /// <see cref="PixelTrigger.RequireReset"/>:
        ///
        /// • false (continuous autocast) — fires every time a "ready" sample
        ///   is observed, gated only by the re-arm delay. If the pixel stays
        ///   ready, it keeps firing.
        ///
        /// • true (one-shot per cycle) — fires once when the pixel becomes
        ///   ready, then disarms until at least one non-ready sample resets
        ///   it. The pixel must return to a non-ready state (typically the
        ///   cooldown color) before another fire can happen.
        /// </summary>
        private async Task RunPixelGatedAsync(ScriptEntry entry, CancellationToken ct)
        {
            var config = entry.Config;
            var pt = config.PixelTrigger!;
            var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Clamp config to sane floors so a bad value can't peg the CPU.
            int pollMs = Math.Max(15, pt.PollIntervalMs);
            int reArmMs = Math.Max(0, pt.ReArmDelayMs);
            int radius = Math.Clamp(pt.SampleRadius, 0, 8);
            bool warnedUnreadable = false;

            // One-shot state machine. When RequireReset is false this stays
            // true forever and the loop behaves like pure autocast. When true,
            // it goes false after each fire and only comes back true once we
            // observe the pixel in a non-ready state — i.e. the user's
            // "reset to initial state" requirement.
            bool armed = true;

            // Initialize the overlay state to "waiting" so the user immediately
            // sees the script as watching, rather than NotApplicable.
            entry.PixelState = PixelOverlayState.Waiting;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var color = _screen.ColorAtAveraged(pt.X, pt.Y, radius);

                    if (color is null)
                    {
                        entry.PixelState = PixelOverlayState.Unreadable;

                        // Screen unreadable (e.g. fullscreen-exclusive game).
                        // Warn once, then back off harder than the poll interval
                        // so we don't spin on a black screen.
                        if (!warnedUnreadable)
                        {
                            _logger.LogWarning(
                                "PixelTrigger '{Name}': cannot read screen at ({X},{Y}). " +
                                "Is the game in fullscreen-exclusive mode? Switch to borderless.",
                                config.Name, pt.X, pt.Y);
                            warnedUnreadable = true;
                        }
                        await _delays.Delay(TimeSpan.FromMilliseconds(Math.Max(pollMs, 250)), ct);
                        continue;
                    }
                    warnedUnreadable = false;

                    // Two-color decision (ready AND closer-to-ready-than-cooldown);
                    // the rule lives in PixelReadyEvaluator — don't inline it back.
                    bool ready = PixelReadyEvaluator.IsReady(color.Value, pt);

                    if (ready && armed)
                    {
                        entry.PixelState = PixelOverlayState.Ready;
                        await RunStepsOnceAsync(config, heldKeys, ct);
                        ReleaseHeld(heldKeys);          // each cast is a discrete burst
                        if (pt.RequireReset) armed = false;
                        if (reArmMs > 0)
                            await _delays.Delay(TimeSpan.FromMilliseconds(reArmMs), ct);
                    }
                    else
                    {
                        // Any non-ready observation re-arms the trigger. (When
                        // RequireReset is false, armed is already true and this
                        // is a no-op — so the autocast path is unchanged.)
                        entry.PixelState = PixelOverlayState.Waiting;
                        if (!ready) armed = true;
                        await _delays.Delay(TimeSpan.FromMilliseconds(pollMs), ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal stop path – swallow
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in pixel-gated script '{Name}'", config.Name);
            }
            finally
            {
                ReleaseHeld(heldKeys);
                entry.Cts = null;
            }
        }

        /// <summary>
        /// Runs every step of a script exactly once. <paramref name="heldKeys"/>
        /// tracks which keys are physically down so a key common to consecutive
        /// steps is not released and re-pressed — it just stays held.
        /// Public so tests can pin the step/delay behavior directly.
        /// </summary>
        public async Task RunStepsOnceAsync(
            ScriptConfig config, HashSet<string> heldKeys, CancellationToken ct)
        {
            foreach (var step in config.Steps)
            {
                if (ct.IsCancellationRequested) break;

                // 1. Release only keys held previously that this step no longer needs
                foreach (var key in heldKeys
                             .Where(k => !step.Hold.Contains(k, StringComparer.OrdinalIgnoreCase))
                             .ToList())
                {
                    _inputSim.KeyUp(key);
                    heldKeys.Remove(key);
                }

                // 2. Press hold keys not already down; keys still held just stay down
                foreach (var key in step.Hold)
                {
                    if (heldKeys.Add(key))
                        _inputSim.KeyDown(key);
                }

                // 3. Tap each press key
                foreach (var key in step.Press)
                    _inputSim.KeyPress(key);

                // 4. Random delay in [DelayMin, DelayMax] — the mandatory
                //    humanlike jitter; never remove or restructure this.
                var delay = config.DelayMin
                    + (config.DelayMax - config.DelayMin) * _random.NextDouble();
                await _delays.Delay(TimeSpan.FromSeconds(delay), ct);
            }
        }

        /// <summary>Releases every key in the set and clears it.</summary>
        private void ReleaseHeld(HashSet<string> heldKeys)
        {
            foreach (var key in heldKeys)
                _inputSim.KeyUp(key);
            heldKeys.Clear();
        }
    }
}
