# Settings schema — global tunables (`settings` block)

Status: **draft v2 — `attentionRenagMinutes` (T88) confirmed with pc-dash-hub
(T89) 2026-09-02.** pc-dash-hub built + smoke-tested its side, commit held
pending coordinator confirmation.

## Context / ownership

pc-dash-app is an **outbound-only** reporter — it never reads settings back
from the hub (`HubClient.cs` is PUT-only, carrying `pcState` fields). It does
**not** consume, forward, or write `attentionRenagMinutes` in any way, and no
pc-dash-app code changes for T88.

This field is **hub-owned and hub-global**. This doc exists only because T88
assigned the wire-shape authorship here (T8/T44/T52/T57 pattern); pc-dash-hub
(T89) owns storage + relay, the knob (T90) and phone deck (T93) are the
consumers.

## `attentionRenagMinutes`

| | |
|---|---|
| Wire name | `settings.attentionRenagMinutes` — top-level in the `settings` block, sibling to `clockFaceId` / `buzzerEnabled` / `vibrationEnabled` / `activeProfile` / `macros` / `weather` / `timeouts`. **Not** under `timeouts` (that sub-block is all `*Ms`; this is minutes). camelCase, like all wire settings. |
| Canonical store | **new `GET`/`PUT /api/settings`** (a single hub-global doc). Echoed **read-only** into `GET`/`PUT /api/devices/{id}/state`'s `settings` block, so the knob receives it via its existing device-state poll — knob-side shape is exactly as proposed. **Not** accepted on `PUT /api/devices/{id}/state` (`StrictModel` → 422). |
| Type | number, minutes. Fractional allowed but integers expected. |
| Range | `>= 0`. Negative → 422. |
| `0` | re-nag disabled: entry chime only, no repeat. Stored as-is. |
| Default (unset) | **5** (hub default). |
| Scope | hub-GLOBAL — one value, not per-session/per-device ("X is global", v1.1 batch-2 model). |
| Writer | hub default today. A future phone settings UI can `PUT /api/settings`. pc-dash-app has no settings-write path and is not involved. |

### Semantics

Per the 2026-09-02 attention-indicator reframe (coordinator `TASKS.md`,
"v1.1 batch 2"):

- While the **aggregate** attention state is `needs-input` (or `error`), the
  knob (T90) and phone (T93) fire the two-beep attention chime on entry,
  then **re-fire it every `attentionRenagMinutes`** until the state resolves.
  `0` → entry chime only, no repeat.
- Resolves on resume-working / `SessionEnd` / PC-offline → indicator off,
  repeating chime stops.
- The repeat timer is **consumer-side** (knob/phone own the semantics) —
  same division of labour as T44 (`utcOffsetMinutes`) and T76
  (`lastInputSecondsAgo`): the hub carries the value, the consumer
  implements the policy. Interval is tunable with no app/firmware redeploy.
- The `working → done` one-time completion chime (T87, hub-detected) is
  **not** governed by this tunable — it is a single edge, never re-nagged.

## `knobBeepEnabled` / `phoneBeepEnabled` (T96)

Status: **field names APPROVED by coordinator, write/read path confirmed
with pc-dash-hub, knob-firmware question answered — all 2026-09-02. Code
HELD pending only Brian's OK on the reader+writer architecture shift (being
surfaced to him live by the coordinator).**

Brian wants **two independent beep-enable toggles**, surfaced as user-facing
controls in the WPF status window (T13), backed by the settings schema —
replacing/extending the single `buzzerEnabled`.

### Proposed fields

| Field | Type | Default | Gates |
|---|---|---|---|
| `knobBeepEnabled` | bool | `true` | knob's buzzer/chime (T60/T77/T90) — the two-beep attention chime + completion chime on the physical knob |
| `phoneBeepEnabled` | bool | `true` | phone deck's beep (T79/T93) — same edges, on the phone kiosk |

- Both top-level in `settings` (siblings of `attentionRenagMinutes`,
  `clockFaceId`, `vibrationEnabled`, …), camelCase, hub-global.
- Naming: `*BeepEnabled` chosen for symmetry and to match the unified
  "two-beep chime" language of the v1.1 batch-2 reframe (both surfaces do
  the same beep now). Alternative considered: `knobBuzzerEnabled` to match
  the existing `buzzerEnabled` term — rejected for the asymmetry with
  `phoneBeepEnabled`.
- `vibrationEnabled` is unaffected (separate concern — knob haptic).

### Write/read path — confirmed with pc-dash-hub (T96) 2026-09-02

Anchor: `buzzerEnabled` / `vibrationEnabled` are today **per-device**
`settings` fields written via `PUT /api/devices/{id}/state` — NOT in
`/api/settings`. `clockFaceId` / `activeProfile` / `macros` are likewise
per-device. `/api/settings` (global doc) currently holds only
`attentionRenagMinutes`. So there's no clobber risk between `/api/settings`
writes and the per-device fields regardless.

1. **`buzzerEnabled` migration (hub plan):** add `knobBeepEnabled` +
   `phoneBeepEnabled` as new **global** fields in `/api/settings` (bool,
   default true). Keep `buzzerEnabled` alive as a **deprecated alias** — the
   hub mirrors `knobBeepEnabled`'s value into `buzzerEnabled` in the
   device-state echo block so knob firmware still reading
   `settings.buzzerEnabled` keeps working through the rollout; drop the
   alias once the knob OTA reads `knobBeepEnabled`. No auto-seed from the
   stored per-device `buzzerEnabled` (global doc has no `device_id` to seed
   from). `vibrationEnabled` untouched, stays per-device.
   - **Answered by lilygo-rotary-knob 2026-09-02:** knob firmware reads
     `settings.buzzerEnabled` today (NVS key `buzzerEn`, gates
     `startBuzzPattern()`). It can switch to `settings.knobBeepEnabled` on
     its next OTA, with `buzzerEnabled` kept as the deprecated mirrored
     alias during rollout — so the hub's alias-echo stays until that OTA
     ships, then drops.
2. **`PUT /api/settings` merge:** already partial field-level deep-merge
   (`_deep_merge(stored, model_dump(exclude_unset=True))`, built for T89). A
   PUT of just `{"knobBeepEnabled": false}` leaves `attentionRenagMinutes`
   and everything else untouched. Matches the preference — no change needed.
3. **Concurrent writers:** fine. Each writer only PUTs its own
   `exclude_unset` fields, no read-modify-write race; same-field concurrent
   write is last-write-wins (acceptable for user toggles). Hub is adding a
   temp-file+rename atomic write to `_save_global_settings` as part of T96
   so a rare simultaneous PUT can't truncate the doc.
4. **Window-open read:** WPF app calls **`GET /api/settings`** directly —
   canonical source, no `device_id` needed, semantically correct (these are
   global). Not the device-state echo (that's only for the knob's existing
   poll convenience).

### WPF build plan (this app — HELD, do not start until Brian approves)

- New `SettingsClient.cs` (or extend `HubClient`): `GET /api/settings` →
  parse `knobBeepEnabled` / `phoneBeepEnabled`; `PUT /api/settings` with a
  partial `JsonObject` body (`{"knobBeepEnabled": <bool>}` etc.), trim-safe
  the same way `HubClient` builds its body (`JsonObject.ToJsonString()`, no
  reflection / source-gen entry).
- `MainWindow`: two checkboxes ("Knob beep" / "Phone beep"), populated from
  `GET /api/settings` on window open, `PUT` the single changed field on
  toggle. Hub-unreachable handling: disable the toggles + show a "settings
  unavailable" note rather than presenting a stale/false state as truth
  (same principle as the T46 "don't make a failure look like success" gap).
- Net new hub work (their side): 2 fields + defaults in the global model,
  the `buzzerEnabled` alias-echo, atomic write. Small.

### Implication flagged to coordinator (T96)

This makes the WPF app a settings **writer** (and reader) via
`/api/settings` — it has been strictly outbound (`HubClient` PUT-only,
`pcState`) until now. That's a real architecture shift, not just a new
field. Confirming the field names **and** this implication back through the
coordinator before building the write/read path, per T96's instruction.

## Review status

- **2026-09-02**: draft v1 written, relayed via coordinator.
- **2026-09-02**: pc-dash-hub (T89) confirmed all open questions —
  `settings.attentionRenagMinutes` top-level (not under `timeouts`),
  canonical store is a new global `GET`/`PUT /api/settings` echoed read-only
  into device state, `0` = disabled, negative = 422, default 5. pc-dash-hub
  built + smoke-tested, commit held pending coordinator confirmation.
  No pc-dash-app code involved. Draft v2.
