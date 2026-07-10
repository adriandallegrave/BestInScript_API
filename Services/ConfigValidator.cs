using BestInScript.API.Models;
using BestInScript.API.Persistence;

namespace BestInScript.API.Services
{
    /// <summary>
    /// Shared validation for script and preset writes, used by both
    /// controllers. Returns null when valid, otherwise the exact message to
    /// send as a 400 (the web UI displays these strings verbatim).
    /// </summary>
    public sealed class ConfigValidator
    {
        private readonly IScriptRepository _scriptRepo;
        private readonly IPresetRepository _presetRepo;

        public ConfigValidator(IScriptRepository scriptRepo, IPresetRepository presetRepo)
        {
            _scriptRepo = scriptRepo;
            _presetRepo = presetRepo;
        }

        public string? ValidateScript(ScriptConfig script)
        {
            if (string.IsNullOrWhiteSpace(script.Name))
                return "Script name is required.";

            if (!InputSimulatorService.IsValidTriggerKey(script.TriggerKey))
                return $"Invalid trigger key '{script.TriggerKey}'. " +
                       "Must be a keyboard key (not a mouse button). " +
                       "Call GET /api/scripts/valid-keys for the full list.";

            // Trigger key cannot collide with a preset's trigger.
            var presetCollision = _presetRepo.GetAll()
                .FirstOrDefault(p => string.Equals(
                    p.TriggerKey, script.TriggerKey, StringComparison.OrdinalIgnoreCase));
            if (presetCollision != null)
                return $"Trigger key '{script.TriggerKey}' is already used by preset '{presetCollision.Name}'. " +
                       "Pick a different key.";

            // Fair-play floor: delays must stay within humanlike bounds.
            if (script.DelayMin < 0.1 || script.DelayMax > 5.0 || script.DelayMin > script.DelayMax)
                return "DelayMin must be ≥0.1 s, DelayMax must be ≤5.0 s, and Min must be ≤Max.";

            foreach (var step in script.Steps)
            {
                foreach (var key in step.Hold.Concat(step.Press))
                {
                    if (!InputSimulatorService.IsValidKey(key))
                        return $"Unknown key name '{key}' in a step. " +
                               "Call GET /api/scripts/valid-keys for the full list.";
                }
            }

            var overlayError = ValidateOverlayStyle(script.OverlayColor, script.OverlayIcon);
            if (overlayError is not null) return overlayError;

            return null;
        }

        /// <summary>
        /// Shared check for the optional per-entry overlay accent color and icon.
        /// Both live on scripts and presets. Returns null when valid.
        /// </summary>
        private static string? ValidateOverlayStyle(int[]? color, string? icon)
        {
            if (color is { } c && (c.Length != 3 || c.Any(v => v < 0 || v > 255)))
                return "Overlay color must be three values 0–255 [R,G,B].";

            if (!string.IsNullOrEmpty(icon) && icon.Length > 8)
                return "Overlay icon must be at most 8 characters.";

            return null;
        }

        public string? ValidatePreset(Preset preset)
        {
            if (string.IsNullOrWhiteSpace(preset.Name))
                return "Preset name is required.";

            if (!InputSimulatorService.IsValidTriggerKey(preset.TriggerKey))
                return $"Invalid trigger key '{preset.TriggerKey}'. " +
                       "Must be a keyboard key (not a mouse button).";

            // Trigger key cannot collide with any script's trigger.
            var triggerCollision = _scriptRepo.GetAll()
                .FirstOrDefault(s => string.Equals(
                    s.TriggerKey, preset.TriggerKey, StringComparison.OrdinalIgnoreCase));
            if (triggerCollision != null)
                return $"Trigger key '{preset.TriggerKey}' is already used by script '{triggerCollision.Name}'. " +
                       "Pick a different key.";

            // …or any other preset's trigger.
            var presetCollision = _presetRepo.GetAll()
                .FirstOrDefault(p => p.Id != preset.Id
                    && string.Equals(p.TriggerKey, preset.TriggerKey, StringComparison.OrdinalIgnoreCase));
            if (presetCollision != null)
                return $"Trigger key '{preset.TriggerKey}' is already used by preset '{presetCollision.Name}'. " +
                       "Pick a different key.";

            // Every referenced script id must exist.
            var existingIds = _scriptRepo.GetAll().Select(s => s.Id).ToHashSet();
            var unknown = preset.ScriptIds.Where(id => !existingIds.Contains(id)).ToList();
            if (unknown.Count > 0)
                return $"Preset references {unknown.Count} script id(s) that no longer exist: " +
                       string.Join(", ", unknown);

            // Deduplicate member ids while preserving order.
            preset.ScriptIds = preset.ScriptIds.Distinct().ToList();

            var overlayError = ValidateOverlayStyle(preset.OverlayColor, preset.OverlayIcon);
            if (overlayError is not null) return overlayError;

            return null;
        }
    }
}
