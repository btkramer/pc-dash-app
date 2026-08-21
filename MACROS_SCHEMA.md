# Macros field — first-draft schema (T8)

Status: **draft v3** — `macros` + `macroState` confirmed and implemented
by pc-dash-hub (smoke-tested, not yet deployed); window enumeration
approach now decided (see below), implementation not yet built. Written
2026-08-20. Owned here per the coordinator's T8
reassignment: pc-dash-app is where PC-dependent macro types actually
execute, so this project drives the schema's shape. Design context this
draft is built from (relayed from the coordinator's `MASTER_PLAN.md`, not
originating in this project) is saved in `coordinator-reports/pc-dash.md`'s
2026-08-20 "T8 design context relayed" entry.

## Shape

A macro definition is one JSON object:

```jsonc
{
  "id": "obs-switch-cam",       // stable slug, unique across the whole macro set
  "label": "Switch to Cam",     // display text for the grid tile (deck renders it, not this app)
  "icon": "camera",             // optional icon key; icon set/rendering is deck-side, opaque string here
  "type": "obs.sceneSwitch",    // dotted "<system>.<action>" discriminator, see Types below
  "target": "pc-dash-app",      // "pc-dash-app" | "pc-dash-hub" -- which surface executes this
  "payload": { "sceneName": "Cam" }  // type-specific fields, see Types below
}
```

`type` is an open dotted string, not a closed enum — matches how this
project's own wire contract with pc-dash-hub already treats `nowPlaying`'s
`source` as an open string (raw AUMID) rather than a fixed set, and avoids
forcing every participant to ship a matching enum whenever a new macro
type is added. `target` decides which surface's executor owns dispatch;
`payload` is opaque to everyone except that surface's own executor —
pc-dash-hub does not need to understand `payload` internals to store and
route a macro, only `id`/`type`/`target`.

## Types (first pass — covers what's called out in the design context)

Executed by **pc-dash-app** (`target: "pc-dash-app"`):

| `type` | `payload` | Notes |
|---|---|---|
| `obs.sceneSwitch` | `{ sceneName }` | Requires an OBS WebSocket connection — not built yet, out of scope for the schema draft. |
| `obs.sourceToggle` | `{ sceneName, sourceName, action }` — `action`: `"show"` \| `"hide"` \| `"toggle"` | |
| `obs.streamControl` | `{ stream, action }` — `stream`: `"streaming"` \| `"recording"`, `action`: `"start"` \| `"stop"` \| `"toggle"` | |
| `discord.callAction` | `{ action }` — `action`: `"muteToggle"` \| `"deafenToggle"` \| `"leaveCall"` | Discord has no first-party local control API for this as far as I know; likely implemented as a simulated global hotkey press rather than a real IPC call — **open question, flagged below**, not resolved by this draft. |
| `keystroke.send` | `{ chords: [ { keys: string[], holdMs?: number } ] }` | `keys` = simultaneous keys in one chord (e.g. `["ctrl","shift","s"]`); the outer array is an ordered sequence for multi-step macros. `holdMs` optional, default left to the executor. |
| `screenshot.capture` | `{ defaultMode, windowTitleMatch? }` — `defaultMode`: `"fullScreen"` \| `"activeWindow"` \| `"namedWindow"` (only `namedWindow` uses `windowTitleMatch`) | `defaultMode` is the static fallback only. The *live* mode (what long-press actually cycles, and what this app's executor reads at dispatch time) lives in the separate `macroState` field, not here — see "Mutable macro state" below. |
| `window.focus` | `{ windowTitleMatch }` | Not explicitly requested, but falls straight out of the window-enumeration capability the screenshot macro already needs — bringing a named window to front. Included as a natural extension; drop if not wanted. |

Executed by **pc-dash-hub** (`target: "pc-dash-hub"`), opaque to this app:

| `type` | `payload` | Notes |
|---|---|---|
| `scene.activate` | `{ sceneId }` | Refers to a scene defined by the hub's own scene data model (`(device, target state)` pairs, per the relayed design context) — this app doesn't interpret `sceneId` at all, just carries it. |

`custom.*` (any `type` not in this table) is intentionally left valid but
unspecified — a forward-compatible escape hatch, same spirit as
`payload` being opaque per-type. An executor that doesn't recognize a
`type` should skip/grey out the macro, not error.

## Mutable macro state — `macroState` (resolved, per pc-dash-hub's review)

`macros` and `macroState` are **separate top-level sibling fields**, not
one nested under the other:

```jsonc
{
  "macros": [ /* MacroDefinition[] as above */ ],
  "macroState": {
    "obs-switch-cam": { "captureMode": "activeWindow" }   // keyed by macro id, opaque value per type
  }
}
```

Why separate, per pc-dash-hub: their `_deep_merge` replaces `macros`
wholesale on any PUT (lists don't recurse), so folding live state into
each macro's `payload` would mean resending the *entire* macro list on
every long-press, and would race any concurrent edit to the macro
definitions themselves. `macroState` is a dict keyed by macro `id`,
deep-merged the same way `pcState` already is — one entry updates without
touching the rest, and keying by `id` gives persistence across profile
switches for free (a profile only references macro `id`s, per the
grid-slot-binding note below).

Cadence: `macroState` writes do **not** ride this app's 5s `pcState`
report loop — that loop is specific to this app's own Core Audio/SMTC
polling. The long-press event that changes capture mode happens on the
phone deck, a different client entirely, so it PUTs `macroState` on-demand
when the event fires, not on any polling cadence. No staleness semantics
needed either — it's UI state, not a liveness signal, so there's no
"stale" reading of it.

For `screenshot.capture` specifically: this app's executor reads
`macroState[id].captureMode` at dispatch time, falling back to
`payload.defaultMode` if no `macroState` entry exists yet for that id.

## Window enumeration (capability, not a macro type) — resolved

`screenshot.capture`'s `namedWindow` mode and `window.focus` both need a
list of open window titles for the phone deck's config UI to pick from.
Decision: **option (a)**, an additional field on this app's existing
push-report pattern (e.g. `openWindows: string[]`), not a new on-demand
relay endpoint.

Why: this app is, today, a strictly outbound push client —
`Program.cs`'s loop only ever calls `HubClient.ReportStateAsync` (an
outbound PUT); there is no inbound listener, no HTTP server, nothing for
the hub to call into. An on-demand relay (option (b)) would require
standing one up — a new dependency (e.g. Kestrel), an open inbound port
on Brian's PC, NAT/firewall exposure — to serve a capability (a window
picker's list) that doesn't need instant on-demand freshness. That's a
meaningfully larger architectural change for what a periodic push already
covers well enough. It also matches the ecosystem-wide pattern
lilygo-rotary-knob already follows for its own device class ("HID stays
outbound-only, everything else routes through the hub").

Not coupling it to the existing 5s `pcState` cadence, though — window
titles don't need audio-level freshness, and re-sending a full window
list every 5s forever (mostly unused, since it only matters while
someone's actively configuring a `namedWindow` target) is wasteful the
same way pc-dash-hub already flagged for `macros`' wholesale-list-replace
concern. Left as an implementation detail for whoever builds the actual
Win32 enumeration code: a separate, slower refresh cadence (e.g. every
15-30s) alongside/independent of the pcState report.

## Explicitly out of scope for this draft

- Grid-slot binding (which macro sits in which of the fixed 2×4 positions
  per profile) — that's a separate `profile` concept per the relayed
  design context, not part of a macro *definition* itself.
- Actual execution code (OBS WebSocket client, Discord control, keystroke
  injection via `SendInput`, window enumeration via Win32) — none of that
  exists yet in this app; this draft is the wire shape only.
- Icon rendering/set — `icon` is carried as an opaque string; the deck
  owns what it means.

## Review status

- **lilygo-rotary-knob** (2026-08-20): shape signed off — `id`/`label`/
  `icon`/`type`/`target` with opaque `payload` works as a caller; no
  macro type missing since there's no knob-side macro UX to compare
  against (phone-deck-only). Re-review requested on the `macroState`
  addition below since it changes the shape slightly from what they
  originally signed off on.
- **pc-dash-hub** (2026-08-20): confirmed `target`-based routing fits
  their storage model (`macros` as `list[{id,label,icon,type,target,
  payload:dict}]`, `target` also kept an open string rather than a closed
  enum, same forward-compat reasoning as `type` — they only actively
  execute `target:"pc-dash-hub"` macros, otherwise store+relay). Proposed
  the `macroState` sibling field below to resolve the mutable-state open
  question; folded into this doc, **sent back to them to confirm before
  they build the storage/merge side**.

Window enumeration (previous section) is still an open question — not
addressed by either review yet.
