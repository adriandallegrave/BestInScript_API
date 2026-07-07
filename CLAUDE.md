# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Design principles — fair play

This app exists solely to prevent repetitive-strain injury (the user plays Diablo 4 hundreds of hours per season), **not** to gain an unfair advantage. Non-negotiable rules for every change:

- All synthetic input must remain indistinguishable from real keyboard input (scan-code `SendInput`, trigger passthrough).
- Humanlike randomized delays are **mandatory** — never remove them, shrink them below human speed, or add a "fast mode".
- When choosing between faster/robotic and slower/humanlike behavior, always pick humanlike, even at an efficiency cost.

## Versioning & release workflow

Semantic versioning, tracked in the README's **Version history** table. Current baseline: **1.0.0** = commit `eb615c5`.

- **Patch** `1.0.+1` — bug fix, no new behavior.
- **Minor** `1.+1.0` — new feature, backwards compatible.
- **Major** `+1.0.0` — breaking: existing data files (`scripts.json` / `presets.json` / `overlay-settings.json`) or setup stop working and the user must reconfigure.
- Docs-only changes: no bump.

For every user-requested feature: implement it, then (1) write a short feature description, (2) write a one-line commit message, (3) append a row to the README version table (version, date, commit, description), (4) commit, (5) `git tag vX.Y.Z`.

## Key File Locations

Flat single-project layout — **all source files sit at the repo root**, no subdirectories. Skip `bin/`, `obj/`, `.vs/` in searches (build output only). The Architecture section below is authoritative; don't re-explore the pipeline.

| Area | Files |
|------|-------|
| Entry point / DI / `MapGet("/")` | `Program.cs` |
| Core engine (hook thread, ownership, run loops) — the only large file | `HotkeyEngine.cs` |
| Input synthesis (`SendInput`, key-name→VK map) | `InputSimulatorService.cs` |
| Pixel reading (GDI, color distance) | `ScreenColorService.cs` |
| Controllers | `ScriptsController.cs`, `PresetsController.cs`, `EngineController.cs`, `OverlayController.cs`, `ScreenController.cs` |
| Models | `ScriptConfig.cs`, `ScriptStep.cs`, `PixelTrigger.cs`, `Preset.cs`, `OverlaySettings.cs` |
| Persistence | `ScriptRepository.cs`, `PresetRepository.cs`, `OverlaySettingsStore.cs` |
| Overlay (WPF) | `OverlayWindow.xaml`, `OverlayWindow.xaml.cs`, `OverlayHostedService.cs` |
| Web UI | `wwwroot/index.html` (+ `overlay-settings-panel.html`, manually pasted in) |
| Config | `appsettings.json` (`BestInScript:DataDirectory` → `C:\temp`) |
| Data (runtime) | `C:\temp\scripts.json`, `presets.json`, `overlay-settings.json` |

## Commands

```powershell
# Run the app in dev mode (ports from Properties/launchSettings.json:
# https://localhost:57997, http://localhost:57998; Swagger at /swagger)
dotnet run

# Build
dotnet build

# Publish self-contained Windows x64 executable
dotnet publish -c Release -r win-x64 --self-contained
```

The **published exe** (the user's real usage — a Start-menu shortcut) has no URL config, so it serves on Kestrel's default **http://localhost:5000**. The root-level `launchSettings.json` (port 5238) is dead — tooling only reads `Properties/launchSettings.json`.

No test project exists yet. No linter is configured.

> Run as a normal user unless the game runs as Administrator, in which case this app must also run as Administrator for the global keyboard hook (`WH_KEYBOARD_LL`) to fire while the game window is focused.

## Architecture

This is a single-project **ASP.NET Core + WPF** Windows-only app (`net10.0-windows`). The project enables both `UseWPF` and `UseWindowsForms` — WPF for the overlay window, WinForms only to enumerate monitors via `System.Windows.Forms.Screen`.

### Core pipeline

```
Trigger key pressed in-game
  └─▶ HotkeyEngine (WH_KEYBOARD_LL hook on dedicated thread BIS_HookThread)
        ├─▶ Script trigger  → ToggleUserOwnership → EnsureRunState
        └─▶ Preset trigger  → TogglePreset → claims/releases every member script
              │
              ▼
        RunScriptAsync (Task, started when owner set goes empty → non-empty)
              ├─▶ RunBlindLoopAsync      — loops steps until owner set empties
              └─▶ RunPixelGatedAsync     — polls a screen pixel; fires when pixel matches "ready" color
                    └─▶ ScreenColorService.ColorAtAveraged (GDI GetPixel, averaged over NxN radius)
              both call:
              └─▶ RunStepsOnceAsync → InputSimulatorService.KeyDown/KeyUp/KeyPress
                                        (Win32 SendInput with KEYEVENTF_SCANCODE)
```

Key design decisions:
- **Keyboard events use scan codes** (`KEYEVENTF_SCANCODE`, not `KEYEVENTF_KEYDOWN`) so they are hardware-identical and pass anti-cheat heuristics that compare VK vs scan code pairs.
- **Screen pixel reads** use `GetDC(IntPtr.Zero)` + `GetPixel` — passive, never touches the game process, invisible to anti-cheat.
- **The trigger key is never suppressed** — `CallNextHookEx` always passes it through so the game receives it normally.
- **Held keys persist across steps** in `RunBlindLoopAsync`: a key common to consecutive steps stays physically down instead of being released and re-pressed between steps.

### Ownership model

A script runs iff its **owner set** is non-empty. The set is mutated only under `HotkeyEngine._toggleLock`; `EnsureRunState` reconciles owners → running task (`HotkeyEngine.cs:483`).

Owners are either:
- `HotkeyEngine.UserOwnerId` (sentinel `Guid.Empty`) — added/removed when the user presses the script's own trigger key directly.
- A `Preset.Id` — added for every member when a preset is toggled on, removed when toggled off.

Consequences:
- Two presets sharing a member both own it; turning one off does not stop the script while the other is still on.
- Pressing a script's own trigger while a preset already owns it just adds the user as a second owner — the script keeps running until *both* are released.
- Editing a script's trigger or steps via the API stops the old run, but any active preset's claim on the same script id is re-applied so the script keeps running across edits (`HotkeyEngine.cs:203`).

### Pixel-gated mode

For scripts with a `PixelTrigger`, the engine watches one screen pixel and fires the step sequence when the pixel reads as "ready". The "ready" predicate is intentionally two-color:

```
ready = (dReady <= Tolerance) && (dReady < dCool)   // HotkeyEngine.cs:617
```

Comparing to *both* `ReadyColor` and `CooldownColor` (rather than a single threshold) is what makes the trigger robust to the lighting/animation drift a single-threshold check can't handle. Don't simplify it back to one comparison.

`PixelTrigger.RequireReset` selects between continuous autocast (always armed, gated only by `ReArmDelayMs`) and one-shot-per-cycle (must observe a non-ready sample before the next fire).

The live verdict surfaces to the overlay via `PixelOverlayState` (`NotApplicable | Ready | Waiting | Unreadable`), stored in an `int` field with `Volatile` reads/writes so the WPF dispatcher's 200 ms poll is cheap and lock-free.

### Services (all singletons)

| Class | Role |
|-------|------|
| `ScriptRepository`     | JSON file persistence for `ScriptConfig` (`scripts.json`). Owns the shared path-resolution helper `ResolveDataFilePath`. |
| `PresetRepository`     | JSON file persistence for `Preset` (`presets.json`). Reuses `ScriptRepository.ResolveDataFilePath`. |
| `InputSimulatorService`| Win32 `SendInput` wrapper. `ResolveVk(string)` maps key names → VK codes; `IsValidTriggerKey` rejects mouse buttons. |
| `ScreenColorService`   | GDI pixel reader + Euclidean color distance math + cursor position. |
| `HotkeyEngine`         | `IHostedService`. Owns the hook thread, per-script `CancellationTokenSource`/`Task`, and the script/preset registries keyed by VK code. |
| `OverlaySettingsStore` | Holds + persists overlay settings (`overlay-settings.json`). Fires `Changed` event on save. |
| `OverlayHostedService` | `IHostedService`. Spins up `OverlayWindow` (WPF) on a dedicated STA thread. |

### REST surface

All controllers are under `/api/[controller]`. Swagger UI at `/swagger`.

| Route                                                | Purpose |
|------------------------------------------------------|---------|
| `/api/scripts` (CRUD + `/status`)                    | `ScriptConfig` CRUD. Each write re-registers with `HotkeyEngine`. |
| `/api/presets` (CRUD + `/status`)                    | `Preset` CRUD. Trigger keys validated as globally unique across scripts and presets. |
| `/api/engine/status`, `/api/engine/stop-all`         | Aggregate script status; emergency stop. |
| `/api/overlay/settings`, `/api/overlay/screens`      | Overlay placement + monitor enumeration (`System.Windows.Forms.Screen`). |
| `/api/screen/color`, `/api/screen/cursor`            | Pixel + cursor-position sampling used by the config UI. |

### Overlay

The WPF overlay window (`OverlayWindow`) is a topmost, click-through, taskbar-hidden pill (`WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`). A `DispatcherTimer` polls `HotkeyEngine.GetStatus()` / `GetPresetStatus()` every 200 ms in-process — no HTTP roundtrip. Settings changes from the web UI fire `OverlaySettingsStore.Changed`, which marshals onto the WPF dispatcher to reposition/restyle live.

Only entries with `ShowInOverlay = true` appear. Pixel-triggered scripts show their live pixel state (`READY` / `waiting` / `unreadable`); blind-loop scripts show `ON`.

### Models

- `ScriptConfig` — a named macro script with `TriggerKey`, `Steps`, `DelayMin/Max`, optional `PixelTrigger`, and `ShowInOverlay`.
- `ScriptStep` — a single step: `Hold[]` (keys kept down) + `Press[]` (keys tapped once).
- `PixelTrigger` — screen-color gate: pixel coordinate, ready/cooldown RGB, tolerance, poll interval, re-arm delay, sample radius, and `RequireReset` (one-shot vs continuous autocast).
- `Preset` — name, trigger key, member `ScriptIds`, `ShowInOverlay`.

### Web UI

Static `wwwroot/index.html` served directly (no static-file middleware — a manual `MapGet("/")` handler resolves the file from project root under `dotnet run` and from beside the exe when published). The overlay settings panel lives in `overlay-settings-panel.html` and must be manually pasted into `index.html`.

### Data files

Three JSON files: `scripts.json`, `presets.json`, `overlay-settings.json`.

Path resolution (`ScriptRepository.ResolveDataFilePath`, reused by `PresetRepository`):
1. If `BestInScript:<SpecificKey>` is set **and absolute** → used verbatim.
2. Otherwise the directory is `BestInScript:DataDirectory` (or `AppContext.BaseDirectory` if unset), and the filename is the SpecificKey value if set or the default (`scripts.json` / `presets.json`).

Config keys: `BestInScript:DataDirectory`, `BestInScript:DataFilePath`, `BestInScript:PresetsFilePath`, `BestInScript:OverlaySettingsPath`. The default `appsettings.json` ships `DataDirectory: C:\temp`.

## Constraints

- Windows 10/11 x64 only — uses `WH_KEYBOARD_LL`, `SendInput`, `GetPixel`, and WPF/WinForms.
- Fullscreen-exclusive DirectX games block overlay rendering and GDI pixel reads; borderless-windowed mode is required for both features.
- Mouse buttons (`Mouse1`–`Mouse5`) are valid in step `hold`/`press` lists but cannot be trigger keys (rejected by `InputSimulatorService.IsValidTriggerKey`).
- Trigger keys are globally unique across scripts and presets — enforced in both `ScriptsController` and `PresetsController`.
- `DelayMin` ≥ 0.1 s, `DelayMax` ≤ 5.0 s, enforced at the API layer.
