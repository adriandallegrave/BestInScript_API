# Feature backlog

Draft overview of candidate features, grouped by theme. Effort: **S**mall / **M**edium / **L**arge.
Every item must respect the fair-play principles in `CLAUDE.md` — humanlike, passive, conservative.

The **#** column numbers each item (`section.item`); **Shipped** marks released items with ✅ and the version they landed in.

## 1. Safety & control (highest value)

| # | Shipped | Feature | Description | Effort |
|---|---------|---------|-------------|--------|
| 1.1 | ✅ v1.11.0 | **Global stop-all hotkey** | Emergency stop is REST/web-only today. Add a configurable panic key handled in `KeyboardHook` → `StopAll`: keys release, owners clear, presets deactivate. | S |
| 1.2 | | **Auto-pause on focus loss** | Watch the foreground window (`GetForegroundWindow` poll or `WinEventHook`). When the game loses focus, suspend key sending but keep owners; resume on refocus. Prevents keystrokes leaking into Discord/browser. | M |
| 1.3 | | **Idle auto-off timer** | A script auto-stops after N minutes without any user trigger interaction — safety net when the user walks away. Per-script or global setting. | S |
| 1.4 | | **Held-key panic release** | Explicit "release all held keys now" action exposed in the UI (and optionally a hotkey). Today this only happens implicitly on stop/shutdown. | S |

## 2. Pixel-trigger UX

| # | Shipped | Feature | Description | Effort |
|---|---------|---------|-------------|--------|
| 2.1 | ✅ 1.4.0 | **Capture wizard** | Single-sample capture already ships: 📍 sample-at-cursor with a 3 s countdown, plus separate "capture ready color" / "capture cooldown color" grabs — there is **no manual RGB entry**. Remaining work: a guided **in-game capture-hotkey** flow that walks ready→cooldown in two passes without alt-tabbing, and helps nudge the coordinate when the two colors are too close to tell apart (a real season pain point). | S |
| 2.2 | | **Live tuning view** | Web UI panel streaming live `dReady` / `dCool` distances and the verdict, so `Tolerance` can be tuned against the real game. Reuses `PixelReadyEvaluator` math. (A basic live readout already exists; this extends it with the raw distances.) | M |
| 2.3 | | **Dry-run mode** | Script flag: the evaluator runs and the overlay shows READY/waiting, but no keys are sent. Validate a trigger before trusting it mid-fight. | S |
| 2.4 | | **Multi-pixel condition** | A script watches 2+ pixels with AND/OR logic (e.g. skill ready AND resource full). Extends `PixelTrigger` to a list — data-model change, likely a major version. | L |

## 3. Config & data management

| # | Shipped | Feature | Description | Effort |
|---|---------|---------|-------------|--------|
| 3.1 | ✅ 1.3.0 | **Profiles** | Named config sets (per character/build/season) switchable in the UI. Each is a `profiles/<name>/` folder of `scripts.json` + `presets.json`; existing files auto-migrate into a `Default` profile. Overlay settings stay global. | M |
| 3.2 | | **Import/export** | Download/upload scripts + presets as a single JSON bundle. Backup before season reset; move between machines. | S |
| 3.3 | | **Default data dir → `%APPDATA%`** | `C:\temp` is risky (disk cleanup wipes it). Migrate the default, keep the config override. Breaking for existing setups → major bump or auto-migration. | S |
| 3.4 | | **Config snapshots** | Auto-backup the JSON files on every write (keep last N) so a bad edit can be undone. Cheap to add in `JsonListFileStore`. | S |

## 4. Overlay

| # | Shipped | Feature | Description | Effort |
|---|---------|---------|-------------|--------|
| 4.1 | ✅ 1.9.0 | **Drag-to-position edit mode** | Toggle an edit mode where the overlay accepts mouse input, drag the pill, save the position. Removes coordinate guessing. Requires temporarily dropping `WS_EX_TRANSPARENT`. | M |
| 4.2 | ✅ 1.7.0 | **Per-entry style** | Color/icon per script in the overlay — `ShowInOverlay` grows into richer per-script overlay config. | S |
| 4.3 | | **"Time since last fire"** | Pixel-gated entries show seconds since the last cast. Debugging aid — passive information only, no timing advantage. | S |

## 5. Engine & scripts (fair-play constrained)

| # | Shipped | Feature | Description | Effort |
|---|---------|---------|-------------|--------|
| 5.1 | | **One-shot scripts** | Trigger press runs the steps once instead of toggling a loop. Useful for combos/openers. Same mandatory randomized delays. | S |
| 5.2 | | **Randomized hold duration** | Hold times get humanlike jitter, like inter-step delays already do. *More* human, not less. | S |
| 5.3 | | **Per-step delay window** | Each step can carry its own optional `DelayMin/Max`, still clamped to 0.1–5.0 s at the API layer. Enables rotations with mixed pacing. | S |
| 5.4 | | **Step order variance (opt-in)** | Shuffle-within-group option for interchangeable steps. Adds human-pattern variance. | M |

## 6. App shell & ops

| # | Shipped | Feature | Description | Effort |
|---|---------|---------|-------------|--------|
| 6.1 | ✅ 1.1.0 | **Tray icon** | Minimize to tray with quick actions: open UI, stop-all, exit. | M |
| 6.2 | ✅ 1.6.0 | **Single-instance guard** | A second launch focuses the existing instance instead of crashing on a port conflict. | S |
| 6.3 | | **Fire/event log + viewer** | Structured log of script start/stop, pixel fires, unreadable streaks, with a web UI viewer. Answers "why didn't it cast". | M |
| 6.4 | | **Fix overlay-settings-panel paste debt** | Replace the manual paste of `overlay-settings-panel.html` into `index.html` with a build-time include or runtime fetch. Pure tech debt. | S |
| 6.5 | | **Live status via SSE** | Replace web UI status polling with server-sent events. Nice-to-have. | M |

## Explicitly out of scope (fair play)

- Fast mode / delay reduction below 0.1 s — never.
- Game-memory reading or packet inspection — pixel reads stay passive GDI.
- Mouse-movement automation / aim assistance.
- Anything reactive faster than a human (the pixel poll interval floor stays).

## Suggested first wave

1. Global stop-all hotkey (1.1 — biggest safety gap)
2. Auto-pause on focus loss (1.2 — second safety gap)
3. Capture wizard in-game hotkey pass + live tuning (2.1 + 2.2 — biggest remaining UX pain)
4. Import/export (3.2 — season-reset insurance)
