# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Design principles — fair play

This app exists solely to prevent repetitive-strain injury (the user plays Diablo 4 hundreds of hours per season), **not** to gain an unfair advantage. Non-negotiable rules for every change:

- All synthetic input must remain indistinguishable from real keyboard input (scan-code `SendInput`, trigger passthrough).
- Humanlike randomized delays are **mandatory** — never remove them, shrink them below human speed, or add a "fast mode".
- When choosing between faster/robotic and slower/humanlike behavior, always pick humanlike, even at an efficiency cost.

## Versioning & release workflow

Semantic versioning, tracked in the README's **Version history** table. Current: **1.12.1** (baseline **1.0.0** = commit `eb615c5`).

- **Patch** `1.0.+1` — bug fix, no new behavior.
- **Minor** `1.+1.0` — new feature, backwards compatible.
- **Major** `+1.0.0` — breaking: existing data files (`scripts.json` / `presets.json` / `overlay-settings.json`) or setup stop working and the user must reconfigure.
- Docs-only changes: no bump.

For every user-requested feature: implement it, then (1) write a short feature description, (2) write a one-line commit message, (3) append a row to the README version table (version, date, commit, description), (4) if the feature is a `BACKLOG.md` item, mark its **Shipped** column `✅ vX.Y.Z`, (5) commit, (6) `git tag vX.Y.Z`.

## Key File Locations

Sources are grouped in folders whose names match their namespaces (`BestInScript.API.<Folder>`); the app csproj stays at the repo root. Skip `bin/`, `obj/`, `.vs/` in searches (build output only). The Architecture section below is authoritative; don't re-explore the pipeline.

| Area | Files |
|------|-------|
| Entry point / DI / `MapGet("/")` | `Program.cs` |
| Engine | `Engine/` — `HotkeyEngine.cs` (hosted-service façade), `KeyboardHook.cs` (WH_KEYBOARD_LL thread + pump), `ScriptCoordinator.cs` (registries + ownership), `ScriptExecutor.cs` (run loops + step executor), `PixelReadyEvaluator.cs` (two-color ready rule), `ScriptEntry.cs` / `PresetEntry.cs`, seams `IScriptRunner.cs`, `IDelayScheduler.cs`, `IRandomSource.cs` |
| Input synthesis (`SendInput`, key-name→VK map) | `Services/InputSimulatorService.cs` + `IInputSimulator.cs`; key catalog `Services/KeyNames.cs` |
| Pixel reading (GDI, color distance) | `Services/ScreenColorService.cs` + `IScreenSampler.cs` |
| Guided in-game capture (2-pass hotkey + coordinate nudge) | `Services/PixelCaptureService.cs` (armed via `ScriptCoordinator.HandleTriggerKey`; endpoints on `ScreenController`) |
| Diablo 4 event timers (world boss / helltide / legion) | `Services/EventScheduleService.cs` (one startup HTTP fetch of `helltides.com/api/schedule`, cached), `Engine/EventScheduleCalculator.cs` (pure next-event selection + `H:MM` formatting), models `Models/EventSchedule.cs` / `EventSnapshot.cs` / `EventOverlayConfig.cs`. Rendered by `OverlayWindow`, configured in the overlay panel |
| Validation (shared by both controllers) | `Services/ConfigValidator.cs` |
| Controllers | `Controllers/` — `ScriptsController.cs`, `PresetsController.cs`, `EngineController.cs`, `OverlayController.cs`, `ScreenController.cs`, `ProfilesController.cs` |
| Models | `Models/` — `ScriptConfig.cs`, `ScriptStep.cs`, `PixelTrigger.cs`, `Preset.cs`, `OverlaySettings.cs`, `ScriptStatus.cs`, `PresetStatus.cs`, `PixelOverlayState.cs`, `EventSchedule.cs`, `EventSnapshot.cs`, `EventOverlayConfig.cs` |
| Persistence | `Persistence/` — `JsonListFileStore.cs` (generic base), `ScriptRepository.cs`, `PresetRepository.cs`, `OverlaySettingsStore.cs`, `DataFilePathResolver.cs`, `ProfileManager.cs` (+ `IProfileScopedStore.cs`), `IScriptRepository.cs`, `IPresetRepository.cs` |
| Overlay (WPF) | `Overlay/` — `OverlayWindow.xaml(.cs)`, `OverlayHostedService.cs`; drag-to-position: `Services/OverlayEditModeSignal.cs` (web→overlay arm) + pure geometry `Engine/OverlayPositionCalculator.cs` |
| Tray icon | `Tray/TrayIconHostedService.cs` — NotifyIcon on dedicated STA thread (open UI / stop-all / exit); also hosts the single-instance activation listener |
| Single-instance guard | `Services/SingleInstanceGuard.cs` — `Local\` named mutex gate + auto-reset activation event; gated at the top of `Program.cs`, listener attached by the tray service |
| Tests | `BestInScript.Tests/` — xUnit, one file per subject; hand-rolled fakes in `Fakes/` |
| Web UI | `wwwroot/index.html` (+ `overlay-settings-panel.html`, manually pasted in); app icon `wwwroot/app.ico` (tray + favicon + exe), located at runtime by `Services/WebAssetLocator.cs` |
| Config | `appsettings.json` (`BestInScript:DataDirectory` → `C:\temp`) |
| Data (runtime) | `C:\temp\profiles\<name>\scripts.json` + `presets.json` (per profile), `C:\temp\profiles.json` (active pointer), `C:\temp\overlay-settings.json` (global) |

## Commands

```powershell
# Run the app in dev mode (ports from Properties/launchSettings.json:
# https://localhost:57997, http://localhost:57998; Swagger at /swagger)
dotnet run

# Build (app + tests)
dotnet build

# Run the test suite (BestInScript.Tests, xUnit)
dotnet test

# Publish self-contained Windows x64 executable
dotnet publish -c Release -r win-x64 --self-contained
```

The **published exe** (the user's real usage — a Start-menu shortcut) has no URL config, so it serves on Kestrel's default **http://localhost:5000**. Release builds are `WinExe` (conditional `OutputType` in the csproj): no console window, the tray icon is the only shell UI; Debug keeps the console for dev logs. The root-level `launchSettings.json` (port 5238) is dead — tooling only reads `Properties/launchSettings.json`.

Tests are pure unit tests over the DI seams (fakes for input, delays, randomness, screen, runner) — no Win32 is touched and no app instance is started. No linter is configured.

> Run as a normal user unless the game runs as Administrator, in which case this app must also run as Administrator for the global keyboard hook (`WH_KEYBOARD_LL`) to fire while the game window is focused.

## Architecture

This is an **ASP.NET Core + WPF** Windows-only app (`net10.0-windows`): one app project (root csproj) plus `BestInScript.Tests`. The app enables both `UseWPF` and `UseWindowsForms` — WPF for the overlay window, WinForms only to enumerate monitors via `System.Windows.Forms.Screen`.

### Core pipeline

`HotkeyEngine` is the `IHostedService` façade wiring three parts (all in `Engine/`): `KeyboardHook` (Win32 hook), `ScriptCoordinator` (ownership), `ScriptExecutor` (run loops, behind `IScriptRunner`). Controllers and the overlay only talk to `HotkeyEngine`.

```
Trigger key pressed in-game
  └─▶ KeyboardHook (WH_KEYBOARD_LL on dedicated thread BIS_HookThread)
        └─▶ KeyPressed event → ScriptCoordinator.HandleTriggerKey
              ├─▶ Script trigger  → ToggleUserOwnership → EnsureRunState
              └─▶ Preset trigger  → TogglePreset → claims/releases every member script
                    │
                    ▼
              ScriptExecutor.RunAsync (Task, started when owner set goes empty → non-empty)
                    ├─▶ RunBlindLoopAsync      — loops steps until owner set empties
                    └─▶ RunPixelGatedAsync     — polls a screen pixel; fires when
                          PixelReadyEvaluator.IsReady says the pixel matches "ready"
                          └─▶ ScreenColorService.ColorAtAveraged (GDI GetPixel, averaged over NxN radius)
                    both call:
                    └─▶ RunStepsOnceAsync → InputSimulatorService.KeyDown/KeyUp/KeyPress
                                              (Win32 SendInput with KEYEVENTF_SCANCODE)
```

Key design decisions:
- **Keyboard events use scan codes** (`KEYEVENTF_SCANCODE`, not `KEYEVENTF_KEYDOWN`) so they are hardware-identical and pass anti-cheat heuristics that compare VK vs scan code pairs.
- **Screen pixel reads** use `GetDC(IntPtr.Zero)` + `GetPixel` — passive, never touches the game process, invisible to anti-cheat.
- **The trigger key is never suppressed** — `KeyboardHook.HookCallback` always returns `CallNextHookEx` so the game receives it normally.
- **Held keys persist across steps** in `ScriptExecutor`: a key common to consecutive steps stays physically down instead of being released and re-pressed between steps.

### Ownership model

A script runs iff its **owner set** is non-empty. The set is mutated only under `ScriptCoordinator._toggleLock`; `ScriptCoordinator.EnsureRunState` reconciles owners → running task.

Owners are either:
- `ScriptCoordinator.UserOwnerId` (sentinel `Guid.Empty`) — added/removed when the user presses the script's own trigger key directly.
- A `Preset.Id` — added for every member when a preset is toggled on, removed when toggled off.

Consequences:
- Two presets sharing a member both own it; turning one off does not stop the script while the other is still on.
- Pressing a script's own trigger while a preset already owns it just adds the user as a second owner — the script keeps running until *both* are released.
- Editing a script's trigger or steps via the API stops the old run, but any active preset's claim on the same script id is re-applied so the script keeps running across edits (`ScriptCoordinator.RegisterScript`).
- Shutdown (`HotkeyEngine.StopAsync` → `ScriptCoordinator.CancelAllRunning`) cancels tasks but keeps owners; the user-facing emergency stop (`StopAll`) clears owners and deactivates presets. Don't merge the two.

### Pixel-gated mode

For scripts with a `PixelTrigger`, the engine watches one screen pixel and fires the step sequence when the pixel reads as "ready". The "ready" predicate is intentionally two-color:

```
ready = (dReady <= Tolerance) && (dReady < dCool)   // PixelReadyEvaluator.IsReady
```

Comparing to *both* `ReadyColor` and `CooldownColor` (rather than a single threshold) is what makes the trigger robust to the lighting/animation drift a single-threshold check can't handle. Don't simplify it back to one comparison.

`PixelTrigger.RequireReset` selects between continuous autocast (always armed, gated only by `ReArmDelayMs`) and one-shot-per-cycle (must observe a non-ready sample before the next fire).

The live verdict surfaces to the overlay via `PixelOverlayState` (`NotApplicable | Ready | Waiting | Unreadable`), stored in an `int` field with `Volatile` reads/writes so the WPF dispatcher's 200 ms poll is cheap and lock-free.

### Services (all singletons)

| Class (interface) | Role |
|-------------------|------|
| `ScriptRepository` (`IScriptRepository`) | `scripts.json` persistence — thin subclass of `JsonListFileStore<ScriptConfig>`. |
| `PresetRepository` (`IPresetRepository`) | `presets.json` persistence — thin subclass of `JsonListFileStore<Preset>`. |
| `InputSimulatorService` (`IInputSimulator`) | Win32 `SendInput` wrapper. Pure statics `ResolveVk` (key name → VK), `IsValidKey`, `IsValidTriggerKey` (rejects mouse buttons). |
| `ScreenColorService` (`IScreenSampler`) | GDI pixel reader + static Euclidean `Distance` + cursor position. |
| `PixelCaptureService`  | Guided in-game pixel capture (BACKLOG 2.1). Armed state machine consulted first in `ScriptCoordinator.HandleTriggerKey`; on the capture hotkey grabs ready then cooldown color at the cursor + a neighborhood grid, and recommends a nearby coordinate when the two colors are too close. Passive (reuses `IScreenSampler` + the passive hook). |
| `ConfigValidator`      | Script/preset validation shared by both controllers; returns the exact 400 message or null. |
| `KeyboardHook`         | WH_KEYBOARD_LL hook thread + message pump; raises `KeyPressed(vk)`; never suppresses keys. |
| `ScriptCoordinator`    | Script/preset registries keyed by VK, ownership model, `_toggleLock`, per-script `CancellationTokenSource`. `HandleTriggerKey` also checks the global emergency-stop hotkey (`_stopAllVk`, set via `SetStopAllKey`) — a match fires `StopAll` and takes precedence over any script/preset on that key (BACKLOG 1.1). |
| `ScriptExecutor` (`IScriptRunner`) | Blind-loop / pixel-gated run loops + step executor with the mandatory randomized delays. |
| `HotkeyEngine`         | `IHostedService` façade: loads repos, wires hook → coordinator, delegates the public API. Also resolves `OverlaySettings.StopAllHotkey` → `ScriptCoordinator.SetStopAllKey` at startup and on every `OverlaySettingsStore.Changed`, keeping the panic key live. |
| `OverlaySettingsStore` | Holds + persists overlay settings (`overlay-settings.json`). Fires `Changed` event on save. Global — NOT profile-scoped. |
| `OverlayEditModeSignal` | One-way web→overlay signal (`EnterRequested`) that arms drag-to-position edit mode; the commit direction is handled in-window via `OverlayWindow.PositionCommitted`. |
| `EventScheduleService` (`IHostedService`) | Diablo 4 event timers. One outbound HTTP GET of `helltides.com/api/schedule` at startup (the app's ONLY network call), cached in memory; `GetSnapshot(now)` returns the next world boss / helltide / legion via `EventScheduleCalculator`. Passive/informational; a failed fetch just hides the rows. Injected into `OverlayWindow` (via `OverlayHostedService`) and `OverlayController`. |
| `ProfileManager` | Owns the named config profiles and the active pointer. Migrates legacy files into a `Default` profile, then repoints the profile-scoped stores (`IProfileScopedStore`: the two repos) at the active `profiles/<name>/` dir. `HotkeyEngine.SwitchProfile` calls `ClearAll` → `Activate` → reload. |
| `OverlayHostedService` | `IHostedService`. Spins up `OverlayWindow` (WPF) on a dedicated STA thread. |
| `TrayIconHostedService` | `IHostedService`. System-tray `NotifyIcon` on a dedicated STA thread; menu: open UI, stop-all, exit. Resolves the UI URL lazily from `IServerAddressesFeature`. Attaches `SingleInstanceGuard.ListenForActivation(OpenUi)` so a second launch surfaces this instance's UI. |
| `SingleInstanceGuard` | Single-instance gate (BACKLOG 6.2). `Local\` named mutex + auto-reset event. `Acquire()` runs at the top of `Program.cs` before the web host; a non-primary launch signals the primary to open its UI and exits. Registered as a singleton so DI owns disposal. Windows-only (no-op elsewhere). |

Testing seams (`IInputSimulator`, `IScreenSampler`, `IDelayScheduler`, `IRandomSource`, `IScriptRunner`, the repo interfaces) each have exactly one production implementation; tests swap in hand-rolled fakes from `BestInScript.Tests/Fakes/`. `TaskDelayScheduler` and `SharedRandomSource` forward to `Task.Delay` / `Random.Shared`, so production timing is unchanged.

### REST surface

All controllers are under `/api/[controller]`. Swagger UI at `/swagger`.

| Route                                                | Purpose |
|------------------------------------------------------|---------|
| `/api/scripts` (CRUD + `/status`)                    | `ScriptConfig` CRUD. Each write re-registers with `HotkeyEngine`. |
| `/api/presets` (CRUD + `/status`)                    | `Preset` CRUD. Trigger keys validated as globally unique across scripts and presets. |
| `/api/engine/status`, `/api/engine/stop-all`         | Aggregate script status; emergency stop. |
| `/api/overlay/settings`, `/api/overlay/screens`      | Overlay placement + monitor enumeration (`System.Windows.Forms.Screen`). |
| `/api/overlay/edit-mode` (POST)                      | Arm drag-to-position edit mode on the live pill (via `OverlayEditModeSignal`). |
| `/api/overlay/events`                                | Live "next event" snapshot (world boss / helltide / legion) with `H:MM` countdowns — read-only preview for the config UI. |
| `/api/screen/color`, `/api/screen/cursor`            | Pixel + cursor-position sampling used by the config UI. |
| `/api/screen/capture/arm`, `/disarm`, `/state`       | Guided in-game capture: arm a hotkey, then two in-game presses grab ready/cooldown color + a coordinate-nudge suggestion; UI polls `/state`. |
| `/api/profiles` (list, create, `/{name}/activate`, rename, delete) | Named config profiles. `activate` routes through `HotkeyEngine.SwitchProfile` (stops runs, repoints stores, reloads). |

### Overlay

The WPF overlay window (`OverlayWindow`) is a topmost, click-through, taskbar-hidden pill (`WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`). A `DispatcherTimer` polls `HotkeyEngine.GetStatus()` / `GetPresetStatus()` every 200 ms in-process — no HTTP roundtrip. Settings changes from the web UI fire `OverlaySettingsStore.Changed`, which marshals onto the WPF dispatcher to reposition/restyle live.

**Drag-to-position (BACKLOG 4.1):** the web panel's "Reposition by dragging" button `POST`s `/api/overlay/edit-mode`, which raises `OverlayEditModeSignal.EnterRequested`; `OverlayHostedService` marshals `OverlayWindow.EnterEditMode`, which temporarily **drops `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`** so the pill takes the mouse, shows ✓/✕ chrome (kept outside `RowsPanel` so the 200 ms refresh never clobbers it), and enables `DragMove`. ✓ commits the drag → `PositionCommitted(screenIndex, x, y)` → the hosted service persists `Anchor = Custom` + `PositionX/Y` (DIP offsets from the target screen's top-left); ✕ reverts. `OverlayAnchor.Custom` in `ApplyPosition` clamps to the screen so the pill can't park off-screen. Pure geometry lives in `Engine/OverlayPositionCalculator.cs` (screen-under-point, relative↔absolute, clamp) so it's unit-testable without Win32. The window never writes settings itself.

Only entries with `ShowInOverlay = true` appear. Pixel-triggered scripts show their live pixel state (`READY` / `waiting` / `unreadable`); blind-loop scripts show `ON`.

Below the script/preset rows, the enabled Diablo 4 event timers render as an aligned three-column block (name / region / `H:MM` countdown) using WPF shared-size columns (`Grid.IsSharedSizeScope` on `RowsPanel`). Helltide shows `active` (time until it ends) or `locked` (time until the next start). Each event's alarm is a **text-only, staged** color (no sound, no background): the whole row is the **main color** until `WarningLeadMinutes` out, switches to the **warning color** (solid) inside that window, then **blinks** (alternating warning ↔ main at ~1 Hz) inside the closer `AlarmLeadMinutes`. Helltide keeps its green (active) / red (locked) state color as its *main* base; the warning/blink layer on top. `MakeEventRow` computes the staged brush; the blink is folded into the row `Fg` (which the row signature hashes), so `ApplyRows` rebuilds at the blink cadence off the existing 200 ms poll (no Storyboard).

### Models

- `ScriptConfig` — a named macro script with `TriggerKey`, `Steps`, `DelayMin/Max`, optional `PixelTrigger`, and `ShowInOverlay`.
- `ScriptStep` — a single step: `Hold[]` (keys kept down) + `Press[]` (keys tapped once).
- `PixelTrigger` — screen-color gate: pixel coordinate, ready/cooldown RGB, tolerance, poll interval, re-arm delay, sample radius, and `RequireReset` (one-shot vs continuous autocast).
- `Preset` — name, trigger key, member `ScriptIds`, `ShowInOverlay`.

### Web UI

Static `wwwroot/index.html` served directly (no static-file middleware — a manual `MapGet("/")` handler resolves the file from project root under `dotnet run` and from beside the exe when published). The overlay settings panel lives in `overlay-settings-panel.html` and must be manually pasted into `index.html`.

### Data files

Per-profile: `profiles/<name>/scripts.json` + `profiles/<name>/presets.json`. Global: `overlay-settings.json` (at the base dir) and `profiles.json` (the active-profile pointer).

`ProfileManager` computes the base dir from `DataFilePathResolver.ResolveBaseDirectory` (`BestInScript:DataDirectory` or `AppContext.BaseDirectory`) and, at startup + on every switch, calls `Rebind` on each `IProfileScopedStore` (the two repos) to point it at `<base>/profiles/<active>/<file>`. The two repos implement `IProfileScopedStore`; `OverlaySettingsStore` does not (it stays at the base dir, still via `DataFilePathResolver.Resolve`). **Migration:** on first run after upgrade, loose `scripts.json`/`presets.json` in the base dir are moved into a `Default` profile.

The stores are registered as concrete singletons in `Program.cs` with their interfaces forwarded to the same instance, so `ProfileManager` repoints the exact stores that controllers, the validator, and the engine all use. `DataFilePathResolver.Resolve`'s absolute per-file overrides (`BestInScript:DataFilePath` etc.) still set each store's *initial* path but are superseded once `ProfileManager` rebinds to the active profile.

Config keys: `BestInScript:DataDirectory`, `BestInScript:DataFilePath`, `BestInScript:PresetsFilePath`, `BestInScript:OverlaySettingsPath`, `BestInScript:ScheduleApiUrl` (event-timer source, default `https://helltides.com/api/schedule`), `BestInScript:EventsEnabled` (default true). The default `appsettings.json` ships `DataDirectory: C:\temp`.

`overlay-settings.json` also carries the event-timer config (`EventsEnabled` master switch + per-event `WorldBoss` / `Helltide` / `Legion` blocks: `Show`, `AlarmEnabled`, `WarningLeadMinutes`, `AlarmLeadMinutes`, `Color` [main], `WarningColor` [warning/blink, null = amber]) and the global emergency-stop hotkey (`StopAllHotkey`, defaults to `Pause`; null/empty disables it — BACKLOG 1.1). These are additive — old files load with defaults (world-boss alarm on: warn at 30 min, blink last 5; helltide/legion alarms off; missing `WarningLeadMinutes` = 30, `WarningColor` = amber; `StopAllHotkey` = `Pause`). `OverlaySettingsStore.Clone` deep-copies them.

## Constraints

- Windows 10/11 x64 only — uses `WH_KEYBOARD_LL`, `SendInput`, `GetPixel`, and WPF/WinForms.
- Fullscreen-exclusive DirectX games block overlay rendering and GDI pixel reads; borderless-windowed mode is required for both features.
- Mouse buttons (`Mouse1`–`Mouse5`) are valid in step `hold`/`press` lists but cannot be trigger keys (rejected by `InputSimulatorService.IsValidTriggerKey`).
- Trigger keys are globally unique across scripts and presets — enforced in both `ScriptsController` and `PresetsController`.
- `DelayMin` ≥ 0.1 s, `DelayMax` ≤ 5.0 s, enforced at the API layer.
