# BestInScript

A personal macro/autocast assistant for Diablo 4 on Windows. It exists for one reason: to prevent repetitive-strain injury (RSI / carpal tunnel) from hundreds of hours of play per season — not to gain an unfair advantage.

## Purpose & fair play

This tool replaces *repetitive* keystrokes, not skill. Its design deliberately favors humanlike behavior over efficiency:

- **Keystrokes are hardware-identical.** Input goes through Win32 `SendInput` with `KEYEVENTF_SCANCODE`, so every event looks exactly like a real key press from the physical keyboard.
- **Humanlike delays are mandatory.** Every script step waits a randomized delay between `DelayMin` and `DelayMax` (0.1–5.0 s, enforced at the API layer). Delays must never be removed or shrunk below human speed.
- **Conservative over efficient.** When there is a choice between faster/robotic and slower/humanlike behavior, the humanlike option always wins.
- **The trigger key is never suppressed** — the game always receives your real key press.
- **Pixel reads are passive.** Cooldown detection uses GDI `GetPixel` on the screen; the game process is never touched.

## Features

- **Scripts** — a named sequence of steps, each holding (`hold`) and/or tapping (`press`) keys, toggled on/off by a global trigger key that works while the game is focused.
- **Presets** — one trigger key toggles a whole group of scripts at once (e.g. a build's full rotation).
- **Pixel-gated autocast** — a script can watch one screen pixel (e.g. a skill icon) and only fire when it reads as "ready", using a two-color ready/cooldown comparison that is robust to lighting drift.
- **Overlay** — a topmost, click-through pill showing which scripts/presets are active and the live pixel state (`READY` / `waiting` / `unreadable`).
- **Web UI + REST API** — configure everything in the browser; Swagger UI available for the raw API.
- **Tray icon** — the published app runs without a console window; a system-tray icon offers quick actions: open the web UI (also on double-click), stop all scripts, exit.

## Requirements

- Windows 10/11 x64.
- Diablo 4 in **borderless-windowed** mode (fullscreen-exclusive blocks both the overlay and pixel reads).
- Run as Administrator **only if the game does** — otherwise the global keyboard hook won't fire while the game is focused.
- [.NET SDK](https://dotnet.microsoft.com/download) is only needed to build; the published exe is self-contained.

## Getting started

1. Publish a self-contained build:

   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained
   ```

2. Create a shortcut to the published exe (e.g. pin it to the Start menu).
3. Launch it — no console window opens; a tray icon appears and the server starts at **http://localhost:5000**.
4. Double-click the tray icon (or open that address in a browser) to configure scripts, presets, and the overlay. Swagger UI is at `/swagger`. The tray icon's right-click menu also offers **Stop all scripts** and **Exit**.

For development, `dotnet run` serves on the ports from `Properties/launchSettings.json` (https 57997 / http 57998) instead.

## Scripts

A script is a trigger key + a sequence of steps + a delay window. Press the trigger in-game to toggle it on; the steps loop with a random humanlike delay between each until you toggle it off.

```jsonc
{
  "name": "Hammerwing Rotation",
  "triggerKey": "3",        // keyboard key that toggles on/off
  "delayMin": 0.4,          // minimum seconds between steps
  "delayMax": 0.6,          // maximum seconds (actual is random in window)
  "steps": [
    {
      "hold":  ["Shift"],   // held down for the duration of this step
      "press": ["Q", "E"]   // tapped once (down+up)
    },
    {
      "hold":  [],
      "press": ["Mouse1", "R"]
    }
  ]
}
```

### Valid key names

| Category   | Values |
|------------|--------|
| Letters    | A – Z |
| Numbers    | 0 – 9 |
| Function   | F1 – F12 |
| Modifiers  | Shift, LShift, RShift, Ctrl, LCtrl, RCtrl, Alt, LAlt, RAlt |
| Navigation | Up, Down, Left, Right, Home, End, PageUp, PageDown, Insert, Delete |
| Special    | Space, Enter, Tab, Escape, Backspace |
| Numpad     | NumPad0 – NumPad9, Multiply, Add, Subtract, Decimal, Divide |
| Mouse      | Mouse1 (left), Mouse2 (right), Mouse3 (middle), Mouse4, Mouse5 |

> Mouse buttons can be used **in steps** (press/hold) but **not as trigger keys**. Trigger keys must be globally unique across scripts and presets.

## REST API

All under `/api/[controller]`; full schema in Swagger at `/swagger`.

| Route | Purpose |
|-------|---------|
| `/api/scripts` (CRUD + `/status`, `/valid-keys`) | Script definitions |
| `/api/presets` (CRUD + `/status`) | Preset definitions |
| `/api/engine/status`, `/api/engine/stop-all` | Aggregate status; emergency stop |
| `/api/overlay/settings`, `/api/overlay/screens` | Overlay placement + monitor list |
| `/api/screen/color`, `/api/screen/cursor` | Pixel/cursor sampling for pixel-trigger setup |
| `/api/profiles` (list, create, `/{name}/activate`, rename, delete) | Named config profiles (per character/build/season) |

## Configuration & data

Configuration lives in JSON files stored under `C:\temp` by default:

| File | Contents |
|------|----------|
| `profiles/<name>/scripts.json` | Script definitions (trigger key, steps, delays, pixel trigger) — per profile |
| `profiles/<name>/presets.json` | Preset definitions (trigger key, member scripts) — per profile |
| `profiles.json` | Which profile is active |
| `overlay-settings.json` | Overlay placement and style — **global** (shared by all profiles) |

### Profiles

A **profile** is a named config set (per character / build / season) switchable from the
dropdown in the header. Each profile is its own `profiles/<name>/` folder holding one
`scripts.json` + `presets.json`; switching stops any running scripts and loads that profile's
config. Creating a profile can **copy the current one** as a starting point — the usual new-season
flow is *copy last season → re-capture each skill's pixel color* (the build structure and trigger
keys carry over). Overlay placement is a display preference, so it stays global.

On first run after upgrading, any pre-profiles `scripts.json` / `presets.json` sitting directly
in the data directory are **automatically migrated** into a `Default` profile — no manual step.

The location is configurable in `appsettings.json` via `BestInScript:DataDirectory`. Everything reloads on startup; if the app crashes or closes, held keys are released.

## Versioning

Semantic versioning, starting at **1.0.0** (the current state):

- **Patch** (`1.0.+1`) — bug fix, no new behavior.
- **Minor** (`1.+1.0`) — new feature, backwards compatible.
- **Major** (`+1.0.0`) — breaking change: existing data files or setup no longer work and reconfiguration is required.

Every new feature ships with a description, a one-line commit message, a new row in the version history below, and a matching git tag (`vX.Y.Z`). Documentation-only changes do not bump the version.

## Version history

| Version | Date | Commit | Description |
|---------|------------|---------|-------------|
| 1.12.0 | 2026-07-13 | 338df88 | Staged overlay **alarm colors**: each event timer's alarm is now one text-only, three-stage color instead of the old boss-vs-helltide split. The whole row is the **main color** until `WarningLeadMinutes` out, switches to the **warning color** (solid) inside that window, then **blinks** — alternating warning ↔ main at ~1 Hz — inside the closer `AlarmLeadMinutes`. World Boss now **blinks** like the others (no more solid-amber-only heads-up), and **no cell background ever changes** (the old red countdown-background blink is gone). Helltide keeps its **green (active) / red (locked)** state color as its main base; warning/blink layer on top. Both colors and both lead times are configurable per event in the overlay panel (main color = white default, warning color = amber default). Cosmetic overlay-only; additive `WarningLeadMinutes` / `WarningColor` on `EventOverlayConfig` — existing `overlay-settings.json` loads unchanged (missing warning lead = 30 min, warning color = amber; a boss block saved under the old single-lead meaning warns+blinks from that minute until you re-save) |
| 1.11.0 | 2026-07-10 | 062688a | Global emergency **stop-all hotkey**: emergency stop was web-only (the **■ Stop All** button / `POST /api/engine/stop-all`) — now a configurable **panic key** is read by the existing keyboard hook and fires the same `StopAll` (release held keys, clear owners, deactivate presets) without alt-tabbing. Ships bound to **Pause** (a near-zero-collision, do-nothing key in Diablo 4); set/clear it from the new header **Panic** control (keyboard-only, validated at the API layer). It **takes precedence** over any script/preset bound to the same key. Passive — the key still passes through to the game, no synthetic input or timing change. Additive `OverlaySettings.StopAllHotkey`; existing `overlay-settings.json` loads unchanged (gains the Pause default until you change it) |
| 1.10.0 | 2026-07-10 | d2f7eaf | Overlay event colors: the **Helltide** row now reads its state at a glance — the whole row is **green while active**, **red while locked** (overrides its accent color). The per-event **alarm** changed from recoloring the countdown text to blinking a **red background** behind the countdown (text keeps its configured color) for Helltide/Legion. **World Boss** stays its normal color but switches its whole row to an **amber warning color** inside its lead window (default now **30 min**), a solid heads-up separate from the red alarm blink. Cosmetic overlay-only; additive — existing `overlay-settings.json` loads unchanged (boss lead stays at its saved value; set it to 30 in the overlay panel to match the new default) |
| 1.9.1 | 2026-07-10 | 570920c | Overlay layout: the Diablo 4 event timers (world boss / helltide / legion **alarms**) now render **above** the script/preset rows instead of below, so the countdowns read first. Display-order tweak only — no timing, synthetic input, or data-file change; existing `overlay-settings.json` loads unchanged |
| 1.9.0 | 2026-07-10 | 41c7e2e | Drag-to-position overlay edit mode: a **Reposition by dragging** button in the overlay panel arms edit mode on the live pill, which temporarily drops its click-through (`WS_EX_TRANSPARENT`) so it takes the mouse, shows **✓ / ✕** buttons, and lets you drag it anywhere — no more anchor/margin coordinate guessing. ✓ saves the exact spot (`Anchor = Custom` + per-screen `PositionX/Y`, clamped on-screen and multi-monitor aware), ✕ reverts; click-through is restored on exit. Passive placement only — no synthetic input or timing change. Additive `OverlaySettings` fields; existing `overlay-settings.json` loads unchanged |
| 1.8.0 | 2026-07-10 | 0b67ca1 | Diablo 4 event timers in the overlay: below the script/preset rows, an aligned block shows the next **World Boss** (name + region), **Helltide** (`active` = time until it ends, `locked` = time until the next start), and **Legion**, each with an `H:MM` countdown. Data comes from `helltides.com/api/schedule`, fetched once at startup and cached (the app's only outbound call; a failed fetch just hides the rows — no game-process contact). Per event you can toggle show/hide, an **alarm** that flashes the countdown red for its last N minutes (no sound; world-boss default on at 5 min, helltide/legion off), the lead time, and a row color — all in the overlay panel. Additive `OverlaySettings` fields; existing `overlay-settings.json` loads unchanged |
| 1.7.0 | 2026-07-10 | 0b7d55c | Per-entry overlay style: each script and preset can carry an optional accent color and an emoji icon that tint its label text and prefix its name in the on-screen overlay, so several active rows are easy to tell apart at a glance. The status dot keeps its green/orange/red run-state meaning; white = no override. Edited in the script/preset forms (color picker + emoji quick-pick), validated at the API layer. Backwards-compatible additive fields (`OverlayColor` / `OverlayIcon`, null = default) — existing `scripts.json` / `presets.json` load unchanged |
| 1.6.0 | 2026-07-10 | 211316f | Single-instance guard: launching the app while it's already running no longer crashes on a Kestrel port-bind conflict — the second launch detects the running instance via a `Local\` named mutex, signals it (auto-reset event) to open its web UI in the browser, and exits before building the web host. The primary resolves its own URL, so it works under both `dotnet run` and the published exe |
| 1.5.0 | 2026-07-09 | d601e18 | UI refresh: the sidebar no longer splits 50/50 — the Scripts list fills the available height and scrolls only when long, while Presets stay compact (content-sized, capped max-height) instead of wasting half the sidebar on one card; denser script/preset cards, wider sidebar, and editor panels centered + width-capped on large monitors; warm-dark "Anthropic" palette (clay/coral accent on warm charcoal), serif display headings, softer radii. Cosmetic only — no engine, input-timing, or data-format changes |
| 1.4.0 | 2026-07-08 | 575fe04 | Guided in-game capture: arm a hotkey, then two in-game presses grab the ready then cooldown color at the cursor (no alt-tabbing back to the browser); each pass scans a small neighborhood and, when the two colors are too close to tell apart, suggests a nearby pixel that separates them better — passive GDI reads + the existing keyboard hook, no synthetic input |
| 1.3.0 | 2026-07-08 | 406b8fc | Profiles: named per-character/build/season config sets switchable from the header dropdown (`profiles/<name>/`), with copy-current for season rollover; existing files auto-migrate into a Default profile; overlay settings stay global |
| 1.2.0 | 2026-07-08 | f02e944 | App icon (`wwwroot/app.ico`) shown in the tray, the browser tab (favicon), and the exe / Start-menu shortcut |
| 1.1.0 | 2026-07-08 | 95f75d7 | Tray icon: published exe runs without a console window; tray menu offers open UI (also double-click), stop all scripts, exit |
| 1.0.1 | 2026-07-08 | 9a82273 | Internal restructure: folderized layout, engine decomposed into focused classes, unit-test suite added — zero behavior change |
| 1.0.0 | 2026-07-07 | eb615c5 | Baseline: scripts, presets, pixel-gated autocast, overlay, web UI |
| — | 2026-05-30 | cd77acd | Document presets, ownership model, REST surface |
| — | 2026-05-17 | b607890 | Smarter overlay |
| — | 2026-05-15 | ac34eae | Pixel-gated casting |
| — | 2026-05-15 | b9ef7cf | Held keys in script steps |
| — | 2026-05-13 | 0990a35 | Status overlay |
| — | 2026-04-07 | 431291f | First working version |
| — | 2026-03-27 | 3ca9cee | Initial project files |
| — | 2026-03-27 | 5f3bc13 | Repository scaffolding (.gitattributes, .gitignore, license) |
