# On-screen overlay — patch notes

This patch adds a tiny always-on-top status pill that floats over your game,
showing which script is currently active. Configurable from the existing web
UI (display + placement + opacity + font size).

## Files

### New
- `Overlay/OverlaySettings.cs`
- `Overlay/OverlaySettingsStore.cs`
- `Overlay/OverlayWindow.xaml`
- `Overlay/OverlayWindow.xaml.cs`
- `Overlay/OverlayHostedService.cs`
- `OverlayController.cs`
- `wwwroot/overlay-settings-panel.html` — drop-in `<section>` for `index.html`

### Modified
- `BestInScript_API.csproj` — retargeted to `net10.0-windows`; enables WPF + WinForms
- `Program.cs` — registers `OverlaySettingsStore` and `OverlayHostedService`

## Wiring the UI panel

Open `wwwroot/index.html` and paste the entire contents of
`overlay-settings-panel.html` somewhere inside `<body>` — wherever you'd like
the panel to appear (a sensible spot is near the existing settings area).
It's fully self-contained: scoped CSS, no globals, no library deps.

## New REST endpoints

| Method | URL                       | Description                                  |
| ------ | ------------------------- | -------------------------------------------- |
| GET    | `/api/overlay/settings`   | Current overlay placement / appearance       |
| PUT    | `/api/overlay/settings`   | Replace settings; takes effect immediately   |
| GET    | `/api/overlay/screens`    | List of connected monitors (for the picker)  |

`OverlaySettings` schema:
```json
{
  "enabled":      true,
  "screenIndex":  -1,
  "anchor":       "TopRight",
  "margin":       12,
  "opacity":      0.80,
  "fontSize":     12,
  "hideWhenIdle": false
}
```
`screenIndex` of `-1` means "use the primary display."
Valid `anchor` values: `TopLeft`, `TopCenter`, `TopRight`,
`MiddleLeft`, `MiddleCenter`, `MiddleRight`,
`BottomLeft`, `BottomCenter`, `BottomRight`.

Settings persist to `overlay-settings.json` next to `scripts.json`. Override
the location via `BestInScript:OverlaySettingsPath` in `appsettings.json`.

## How it works

- Hosted alongside the existing ASP.NET host on a dedicated **STA thread**
  so WPF's dispatcher can co-exist with Kestrel.
- The window is **topmost, click-through, and not in the taskbar/Alt-Tab**
  (`WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`),
  so it never steals game input.
- A `DispatcherTimer` polls `HotkeyEngine.GetStatus()` every 200 ms in-process
  — no HTTP roundtrip. When a script is running, the dot turns green and the
  label shows `▶ Script Name · [TriggerKey]`.
- Settings changes from the web UI fire `OverlaySettingsStore.Changed`, which
  the hosted service marshals onto the WPF dispatcher to reposition / restyle
  the window live — no restart needed.
- Multi-monitor positioning uses `System.Windows.Forms.Screen.AllScreens`
  (the index in that array is the value sent to/from the API).

## Caveats

- **Windowed / borderless games only.** Diablo 4, Path of Exile 2, and Last
  Epoch default to borderless windowed, so the overlay sits on top fine.
  Exclusive-fullscreen DirectX renders directly to the swap chain and will
  cover the overlay — that's a Windows limitation. Switch the game to
  borderless if you hit this.
- **Mixed-DPI multi-monitor** setups may have slightly off positioning since
  the app runs system-DPI-aware. Same-DPI displays are pixel-perfect. To make
  it per-monitor v2 aware later, add a `app.manifest` with
  `<dpiAwareness>PerMonitorV2</dpiAwareness>` and reference it via
  `<ApplicationManifest>` in the .csproj.
