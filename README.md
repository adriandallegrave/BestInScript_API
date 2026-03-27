# BestInScript — ARPG Macro Engine

A local Windows-only ASP.NET Core app that lets you assign keyboard macro scripts
to toggle keys, designed to help with repetitive ARPG skill rotations
(Diablo 4, Last Epoch, Path of Exile, etc.).

---

## How it works

1. You define a **Script**: a trigger key + a sequence of steps + delay settings.
2. Run the API and leave it open in the background while you play.
3. Press the trigger key in-game (e.g. `3`) to **toggle** the script **ON**.
4. The script loops through its steps indefinitely, pressing/holding the
   configured keys with a random delay between each step.
5. Press the trigger key again to **toggle it OFF**.
6. The trigger key is **not suppressed** – the game still receives it normally.

---

## Requirements

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

---

## Getting started

```powershell
# 1. Restore packages and run
cd BestInScript_API
dotnet run

# The browser will open automatically to:
#   Swagger UI  →  http://localhost:5238/swagger
#   Config UI   →  http://localhost:5238
```

> **Tip:** Run the terminal as a normal user — `WH_KEYBOARD_LL` does not need
> administrator rights. However, if the game runs as Administrator you may need
> to also run this app as Administrator for the hook to fire while the game
> window is focused.

---

## Script structure

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

> Mouse buttons can be used **in steps** (press/hold) but **not as trigger keys**.

---

## API endpoints

| Method | URL | Description |
|--------|-----|-------------|
| GET    | `/api/scripts` | List all scripts |
| POST   | `/api/scripts` | Create a script |
| PUT    | `/api/scripts/{id}` | Update a script |
| DELETE | `/api/scripts/{id}` | Delete a script |
| GET    | `/api/engine/status` | Runtime status (which scripts are running) |
| POST   | `/api/engine/stop-all` | Stop all running scripts |
| GET    | `/api/scripts/valid-keys` | Full list of valid key names |

---

## UI

Open `http://localhost:5238` for the graphical config UI.

- Create/edit scripts with a click-to-capture trigger key
- Add steps with hold/press key chips and autocomplete
- Drag-free delay sliders with real-time preview
- Live status polling (green badge when a script is active)
- Emergency **Stop All** button in the header

---

## Data storage

Scripts are saved to `scripts.json` next to the executable
(or wherever `BestInScript:DataFilePath` points in `appsettings.json`).
The engine reloads all scripts on startup automatically.

---

## Notes

- The app only sends key events when a script is **toggled on** and the loop is running.
- If the app crashes or is closed, all held keys are released in the `finally` block.
- Adjust `delayMin`/`delayMax` per script to match your character's cast time/animation speed.
- For builds with a single spammable skill just use one step with that skill key.
