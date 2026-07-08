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
