# Feature backlog

Draft overview of candidate features, grouped by theme. Effort: **S**mall / **M**edium / **L**arge.
Every item must respect the fair-play principles in `CLAUDE.md` — humanlike, passive, conservative.

## 1. Safety & control (highest value)

| Feature | Description | Effort |
|---------|-------------|--------|
| **Global stop-all hotkey** | Emergency stop is REST/web-only today. Add a configurable panic key handled in `KeyboardHook` → `StopAll`: keys release, owners clear, presets deactivate. | S |
| **Auto-pause on focus loss** | Watch the foreground window (`GetForegroundWindow` poll or `WinEventHook`). When the game loses focus, suspend key sending but keep owners; resume on refocus. Prevents keystrokes leaking into Discord/browser. | M |
| **Idle auto-off timer** | A script auto-stops after N minutes without any user trigger interaction — safety net when the user walks away. Per-script or global setting. | S |
| **Held-key panic release** | Explicit "release all held keys now" action exposed in the UI (and optionally a hotkey). Today this only happens implicitly on stop/shutdown. | S |

## 2. Pixel-trigger UX

| Feature | Description | Effort |
|---------|-------------|--------|
| **Capture wizard** | In-game capture flow: hover the skill icon, press a capture hotkey → samples coordinate + color via the existing `/api/screen/color` + `/api/screen/cursor`. Two passes: ready state, cooldown state. Eliminates manual RGB entry. | M |
| **Live tuning view** | Web UI panel streaming live `dReady` / `dCool` distances and the verdict, so `Tolerance` can be tuned against the real game. Reuses `PixelReadyEvaluator` math. | M |
| **Dry-run mode** | Script flag: the evaluator runs and the overlay shows READY/waiting, but no keys are sent. Validate a trigger before trusting it mid-fight. | S |
| **Multi-pixel condition** | A script watches 2+ pixels with AND/OR logic (e.g. skill ready AND resource full). Extends `PixelTrigger` to a list — data-model change, likely a major version. | L |

## 3. Config & data management

| Feature | Description | Effort |
|---------|-------------|--------|
| **Profiles** | Named config sets (per character/build/season) switchable in the UI. Maps to per-profile JSON files via the existing `DataFilePathResolver`. | M |
| **Import/export** | Download/upload scripts + presets as a single JSON bundle. Backup before season reset; move between machines. | S |
| **Default data dir → `%APPDATA%`** | `C:\temp` is risky (disk cleanup wipes it). Migrate the default, keep the config override. Breaking for existing setups → major bump or auto-migration. | S |
| **Config snapshots** | Auto-backup the JSON files on every write (keep last N) so a bad edit can be undone. Cheap to add in `JsonListFileStore`. | S |

## 4. Overlay

| Feature | Description | Effort |
|---------|-------------|--------|
| **Drag-to-position edit mode** | Toggle an edit mode where the overlay accepts mouse input, drag the pill, save the position. Removes coordinate guessing. Requires temporarily dropping `WS_EX_TRANSPARENT`. | M |
| **Per-entry style** | Color/icon per script in the overlay — `ShowInOverlay` grows into richer per-script overlay config. | S |
| **"Time since last fire"** | Pixel-gated entries show seconds since the last cast. Debugging aid — passive information only, no timing advantage. | S |

## 5. Engine & scripts (fair-play constrained)

| Feature | Description | Effort |
|---------|-------------|--------|
| **One-shot scripts** | Trigger press runs the steps once instead of toggling a loop. Useful for combos/openers. Same mandatory randomized delays. | S |
| **Randomized hold duration** | Hold times get humanlike jitter, like inter-step delays already do. *More* human, not less. | S |
| **Per-step delay window** | Each step can carry its own optional `DelayMin/Max`, still clamped to 0.1–5.0 s at the API layer. Enables rotations with mixed pacing. | S |
| **Step order variance (opt-in)** | Shuffle-within-group option for interchangeable steps. Adds human-pattern variance. | M |

## 6. App shell & ops

| Feature | Description | Effort |
|---------|-------------|--------|
| **Tray icon** ✅ | Minimize to tray with quick actions: open UI, stop-all, exit. *Shipped in 1.1.0.* | M |
| **Single-instance guard** | A second launch focuses the existing instance instead of crashing on a port conflict. | S |
| **Fire/event log + viewer** | Structured log of script start/stop, pixel fires, unreadable streaks, with a web UI viewer. Answers "why didn't it cast". | M |
| **Fix overlay-settings-panel paste debt** | Replace the manual paste of `overlay-settings-panel.html` into `index.html` with a build-time include or runtime fetch. Pure tech debt. | S |
| **Live status via SSE** | Replace web UI status polling with server-sent events. Nice-to-have. | M |

## Explicitly out of scope (fair play)

- Fast mode / delay reduction below 0.1 s — never.
- Game-memory reading or packet inspection — pixel reads stay passive GDI.
- Mouse-movement automation / aim assistance.
- Anything reactive faster than a human (the pixel poll interval floor stays).

## Suggested first wave

1. Global stop-all hotkey (S — biggest safety gap)
2. Auto-pause on focus loss (M — second safety gap)
3. Capture wizard + live tuning (M+M — biggest UX pain)
4. Import/export (S — season-reset insurance)
