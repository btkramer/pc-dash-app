# Agent-session field — schema (T58)

Status: **draft v2 — reconciled against pc-dash-hub's real T57 model, relayed via
the coordinator 2026-08-28.** They built T57 in parallel and held it uncommitted
specifically to avoid this project building T58 against a guess (their words).
The wire shape below is **not this project's original v1 proposal** — it's been
revised to match their confirmed endpoints/fields exactly, since they'd already
built and smoke-tested their side by the time their message reached this session.
Original v1 proposal (superseded, kept below only where still relevant) was
written per T58's instruction ("coordinate the wire shape with pc-dash-hub before
building against a guess — same cross-participant pattern as T8/T44/T52"). Design
context: lilygo-rotary-knob's `ECOSYSTEM_PLAN.md` "Agent-monitor mode" section
(relayed via the coordinator).

## What's built so far, against the confirmed shape below

`src/PcDashApp.HookForwarder/` — a new, separate console project, **not** part of
the WPF app (`PcDashApp.csproj` is Windows-only; a hook listener needs to run
wherever a Claude Code session runs, which today includes this very Linux
container via SSH, not just Brian's Windows PC — see "Where this actually runs"
below). Plain `net8.0`, no WPF/WinRT/NAudio dependency, cross-compiles/publishes
for both `win-x64` and `linux-x64`.

- Reads Claude Code's hook JSON off **stdin** (leniently, via `JsonNode` — not a
  strict record — since this project can't exercise a real hook invocation from
  this container to confirm the exact field set matches memory; unknown/missing
  fields are tolerated, not fatal).
- Maps `hook_event_name` to one of the five statuses via `HookEventMapper.cs`
  (table below).
- Builds the proposed PUT body and sends it via `AgentSessionClient.cs`, a short
  (2s) timeout, always exits 0 and logs failures to a local file
  (`%TEMP%`/`/tmp`+`pc-dash-hook-forwarder.log`) rather than stderr/throwing —
  a hook command that hangs or fails loudly would visibly disrupt every Claude
  Code turn, which this must never do (same "never block the thing it's
  reporting on" principle as this app's own audio/media reads never blocking the
  UI thread).
- **Not wired into any real `settings.json` yet** — see "Rollout, deliberately
  not done yet" below.

## Where this actually runs (flagging, not resolved here)

The design doc frames this as "the Windows companion app listens for hook
events." In practice, Claude Code sessions in this ecosystem run under multiple
SSH logins on this TrueNAS container (this session included), not only on
Brian's Windows gaming PC. The forwarder is therefore built as a **portable,
per-session tool**, not a feature of the Windows-only WPF app — it's invoked
directly by Claude Code's own hook mechanism from whatever process/user/OS is
running that session, and reaches pc-dash-hub over the network the same way
this app's own `HubClient` does. This changes "Windows companion app" from an
architectural given to one possible deployment target among several; flagging
for whoever designs T59/T60 in case "which host is this session on" matters to
the UI (see the dropped `host` field, noted below in the confirmed wire shape).

## Wire shape — confirmed against pc-dash-hub's real T57 model

```
GET  /api/agent-sessions                    -- list, for T59
GET/PUT/DELETE /api/agent-sessions/{sessionId}
```
Global collection, not nested under the per-device state endpoint (sessions
aren't tied to a knob device). `sessionId` = Claude Code's own per-session UUID,
verbatim, as the URL path segment — confirmed fits pc-dash-hub's
`^[A-Za-z0-9_-]+$` key constraint.

PUT body — **partial, deep-merge like `pcState`/settings** (omit a field =
untouched, `null` = cleared):
```jsonc
{
  "agentProfile": "claude-code",  // confirmed exact literal both sides agree on.
                                   // Free string (not a closed enum) per the design
                                   // doc's "agent profile" concept, so Codex CLI etc.
                                   // can get their own value later with no schema
                                   // change -- same pattern nowPlaying.source/
                                   // macros.type already use.
  "status": "idle",               // FIXED set: idle | working | needs-input | error |
                                   // done -- unlike agentProfile this is not a free
                                   // string. See mapping table below.
  "project": "pc-dash-app",       // short project/repo name, NOT a full path --
                                   // confirmed. Derived here as the last path segment
                                   // of the hook payload's cwd.
  "startedAt": "2026-08-28T16:55:00Z"  // ISO8601, sent only on SessionStart (first
                                   // report for a session) -- omitted every other
                                   // event per the deep-merge omit convention.
}
```
Response adds `sessionId`, `updatedAt` (server-stamped, never client-sent — same
`lastSeenAt` convention `pcConnected` already established), `lastUpdateSecondsAgo`,
and `stale` (computed, 120s default threshold).

**Deliberately sends ONLY these four fields, nothing extra** — pc-dash-hub's PUT
body is strict (`extra="forbid"`; this exact class of mismatch already broke
every field on a PUT once before, see T46 in `PROGRESS.md`). The original v1
draft below proposed `host` (which machine ran the session) and a raw
`hookEvent` debug field; both are dropped from the wire body for now rather than
risk a 422 by sending fields their model doesn't declare. Flagging `host` in
particular as something worth an actual schema field later if T59's UI ever
needs to disambiguate sessions running on different machines (Brian's Windows
PC vs. one of several TrueNAS SSH logins) — not requesting it now, just noting
the gap since it was designed away, not overlooked.

## Hook event → status/DELETE mapping (confirmed)

| `hook_event_name` | action | reasoning |
|---|---|---|
| `SessionStart` | PUT `status: idle`, `startedAt: now` | session exists, nothing in flight yet |
| `UserPromptSubmit` | PUT `status: working` | Claude is about to start processing |
| `PreToolUse` | PUT `status: working` | |
| `PostToolUse` | PUT `status: working` | more tool calls or a final response likely still coming |
| `Notification` (`notification_type` in `permission_prompt`/`agent_needs_input`/`elicitation_dialog`) | PUT `status: needs-input` | a genuine "waiting on you" moment |
| `Notification` (any other `notification_type`, e.g. `idle_prompt`, `auth_success`) | not forwarded | T85: `idle_prompt` fires from sitting quiet with nothing pending -- treating it as `needs-input` (the pre-T85 behavior) falsely flagged attention on ordinary idle stretches |
| `Stop` | PUT `status: done` | turn complete, waiting on the next prompt |
| `SessionEnd` | **DELETE** `/api/agent-sessions/{sessionId}` | see "Session cleanup" below — not a status value at all |
| `SubagentStop` | not forwarded | parent session's own working/done already reflects state |
| `PreCompact` | not forwarded | transient, not a status change from the outside |
| anything else | not forwarded | unrecognized event names degrade to no-op, not an error |

`idle` vs `done`: resolved (this doc's own earlier open question) — `idle` is
only ever sent on `SessionStart`; `done` is what `Stop` sends. Two distinct
values only make sense as two distinct meanings given the fixed 5-state set
pc-dash-hub built against.

**`error` still has no dedicated hook event and is NOT detected in this v1.**
The only signal available is `PostToolUse`'s `tool_response` occasionally
carrying an error/non-zero-exit indication — heuristic, not reliable enough to
ship. Sessions simply never reach `error` status yet. Flag if this is a hard
requirement rather than an acceptable v1 gap.

## Session cleanup (confirmed)

**DELETE on `SessionEnd`** is the primary cleanup path — it's a real, reliably-
fired hook event on normal exit. A hard kill/crash won't fire it, so that
session's last PUT'd status just sits — mitigated by pc-dash-hub's own `stale`
computed field (120s threshold) marking it inactive for any UI that checks it,
not by a second cleanup mechanism on this app's side. Matches "no auto-prune on
the hub side either way for v1" — DELETE handles the clean-exit case actively,
`stale` is the safety net for everything else, no additional pruning needed on
either side for v1.

**Two sessions in the same directory** share the same derived `project` value
(last cwd path segment) but have distinct `sessionId`s — intentional (grouping
label, not a key), flagging in case T59's UI design assumes `project` is
unique. Not raised with pc-dash-hub as an open question since it doesn't affect
their storage model either way (still just a string field).

## Rollout, deliberately not done yet

Wiring this into a real `settings.json` `hooks` block would change the behavior
of **every** Claude Code session under whichever login it's added to (including
this one) — that's a config-file change with real, hard-to-preview consequences
across sessions, not a code change local to this repo. Not doing that without
Brian's own explicit go-ahead, per this project's standing consent carve-out for
config edits. What ships in this draft is the forwarder tool itself
(compile-verified, not live-tested against a real hook invocation or a real hub
endpoint, since neither exists yet); the actual `hooks` config snippet to add is
documented here for whenever that's approved:

```jsonc
// settings.json, illustrative -- not applied anywhere yet
"hooks": {
  "SessionStart":     [{ "hooks": [{ "type": "command", "command": "pc-dash-hook-forwarder" }] }],
  "UserPromptSubmit":  [{ "hooks": [{ "type": "command", "command": "pc-dash-hook-forwarder" }] }],
  "PreToolUse":        [{ "hooks": [{ "type": "command", "command": "pc-dash-hook-forwarder" }] }],
  "PostToolUse":       [{ "hooks": [{ "type": "command", "command": "pc-dash-hook-forwarder" }] }],
  "Notification":      [{ "hooks": [{ "type": "command", "command": "pc-dash-hook-forwarder" }] }],
  "Stop":              [{ "hooks": [{ "type": "command", "command": "pc-dash-hook-forwarder" }] }],
  "SessionEnd":        [{ "hooks": [{ "type": "command", "command": "pc-dash-hook-forwarder" }] }]
}
```
Same binary registered for every event — the event name comes from the stdin
JSON's own `hook_event_name` field, not from argv, so no per-event command
variation is needed.

## Related: attention re-nag tunable (T88)

The knob/phone derive an aggregate attention state (`needs-input`/`error`)
from the session statuses this forwarder reports. How often they *re-fire*
the two-beep attention chime while that state persists is governed by a
global settings tunable, `attentionRenagMinutes` — see `SETTINGS_SCHEMA.md`.
It does not touch this forwarder or the wire shape above (no `Stop`-event
change: the `working → done` completion edge is detected hub-side, T87).

## Explicitly out of scope for this draft

- Approve/deny/interrupt control (T58 is notify-only v1 by design; the client
  is structured so a later version could poll `AgentSessionClient` for pending
  commands without restructuring, but nothing does that yet).
- Reliable `error` detection (see "Hook event → status/DELETE mapping" above).
- Actually adding the `hooks` block to any real `settings.json` (see Rollout
  above).
- Live smoke-testing against pc-dash-hub's real endpoint — T57 was still
  uncommitted as of this writing (held pending this reply). Will build a
  `test/HookForwarderSmoke` mirroring `HubClientSmoke`/`LogoSmoke` once it's
  live, same as T52's pattern.
- A `host` field identifying which machine ran the session (dropped from the
  wire body, see above).

## Review status

- **2026-08-28**: draft v1 written and sent for review. Before it reached
  pc-dash-hub, they'd already independently designed, built, and smoke-tested
  T57 (held uncommitted specifically to avoid this project guessing) and
  relayed their real model + four confirmation questions via the coordinator.
  Answered all four directly against their model rather than this project's
  own v1 guesses (session_id verbatim/UUID — confirmed; `project` = short name
  — confirmed; `agentProfile: "claude-code"` — confirmed exact match; cleanup
  = DELETE on `SessionEnd`, `stale` as the crash-case safety net — proposed and
  not yet re-confirmed by them). This doc (v2) reflects their model, not the
  original proposal. `src/PcDashApp.HookForwarder/` rebuilt against it,
  compile-verified (0 warnings) and self-contained-publish-verified for both
  `linux-x64` and `win-x64`.

- **2026-08-28, T61 deployed — live-verified end-to-end.** pc-dash-hub's T57
  endpoints deployed (commit `2f95e01`). Built `test/HookForwarderSmoke`
  (mirrors `HubClientSmoke`/`LogoSmoke`'s pattern — compiles the real
  `AgentSessionClient.cs` by reference, runs from this container against the
  real hub): 17/17 checks pass — 404-before-create, the confirmed field shape
  round-trips (`agentProfile`/`status`/`project`/`startedAt`, plus server-
  stamped `sessionId`/`updatedAt`/`lastUpdateSecondsAgo`/`stale`), partial-PUT
  deep-merge (a `status`-only PUT leaves `project`/`agentProfile` untouched,
  same convention as `pcState`), the session appears in `GET
  /api/agent-sessions` while present, `DELETE` removes it (confirmed via a
  follow-up 404), and a second `DELETE` on an already-gone session still
  reports success (this client's own idempotent-404 handling). Also ran the
  **actual `pc-dash-hook-forwarder` binary** (not just the client library) end
  to end against the live hub with synthetic stdin for
  `SessionStart`/`PreToolUse`/`Notification`/`Stop`/`SessionEnd` — full
  pipeline confirmed, session correctly cleaned up (404 after `SessionEnd`).

  **One real bug found and fixed via this live testing**: `AgentSessionClient`
  set its own hard 2s `HttpClient.Timeout`, independent of and racing against
  `Program.cs`'s own 3s `CancellationTokenSource` passed into every call — two
  separate timeout budgets on one request, the tighter one silently winning.
  Surfaced as a `SessionEnd` DELETE logging "delete failed" during a rapid
  back-to-back test run, even though a plain `curl` immediately after
  succeeded in well under a second — the hub was never slow, this client's own
  inner timeout was just tighter than the caller's. Not currently
  service-affecting for most events (every event but `SessionEnd` self-heals
  on the next hook fire), but `SessionEnd`'s `DELETE` is one-shot with no
  natural retry, so a timeout there would leave a zombie session relying
  entirely on `stale` as the fallback — the fallback this project had already
  designed in, so nothing broke, but worth fixing rather than leaving a known
  double-timeout footgun in place. **Fixed**: removed the internal
  `HttpClient.Timeout`; the caller's `CancellationToken` is now the single
  source of truth. Reran the smoke test and a 3x repeat of the full
  `SessionStart→...→SessionEnd` sequence via the real binary after the fix —
  all pass, zero timeouts, forwarder's own failure log stays empty. No
  leftover synthetic data on the live hub (`GET /api/agent-sessions` → `[]`
  after cleanup).

  **T58 is functionally complete and live-verified end-to-end**, same bar as
  T52. Still not done: wiring any real `settings.json` `hooks` block (see
  Rollout above — needs Brian's explicit go-ahead) and reliable `error`
  detection (still no dedicated hook event, deliberately not shipped in v1).
