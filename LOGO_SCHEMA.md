# Project logos field — first-draft schema (T52)

Status: **draft v2 — reviewed by pc-dash-hub, 2026-08-27.** Written 2026-08-27,
proposed by pc-dash-app per T52's instruction to "settle the wire shape ... and
loop pc-dash-hub in ... rather than guessing ahead of it" (same T8/T44 pattern
MACROS_SCHEMA.md followed). This app has built the picker UI and local data model
against this shape already (see `src/PcDashApp/Logos/`), but storage/serving calls
are still stubbed with a local-disk store (`LocalLogoAssignmentStore`) —
**the logo-library half (section 1) is now built and committed hub-side as
drafted**; the assignment half (section 2) was revised per pc-dash-hub's
counter-proposal below and is confirmed but not yet built.

Design context this draft is built from: T39's decided spec (relayed via the
coordinator, `TASKS.md`) and the "Agent Monitor Mockups" artifact's revision
history (2026-08-22 and 2026-08-25 entries specifically — logo library location,
generic-icon-set flat-scoping, and the initials-overlay toggle).

## What pc-dash-app already owns, no hub involvement needed

The **generic icon fallback set** (`GenericIconIds` in this app's code) never
touches pc-dash-hub at all. Each icon is a small vector shape rendered locally by
whichever client draws it — this app today, the future phone UI later — keyed by
a stable string id (`"folder"`, `"terminal"`, `"grid"`, `"cube"`, `"chip"`,
`"gear"`, `"layers"`, `"flask"`). Only the *id* crosses the wire (see
`ProjectLogoAssignment.ref` below when `kind` is `"generic"`), the same pattern
`MacroDefinition.icon` already uses. **Every client needs its own matching copy of
this vector set** — if the phone UI is ever built, it must ship the same 8 shapes
under the same ids, or a project assigned e.g. `"chip"` on Windows would render as
a blank/unknown icon there. Flagging that coordination cost explicitly since it's
not obvious from the wire shape alone.

## What needs pc-dash-hub: uploaded logos + per-project assignment

### 1. Shared logo library (uploaded images)

Per the mockup's 2026-08-22 revision, an uploaded logo is **permanent and
cross-project reusable**, not a one-off tied to whichever project uploaded it —
so this is its own top-level resource, not nested under a project.

```
POST /api/logos          (multipart/form-data, one image file field)
  -> 201 { "id": "a1b2c3d4e5f6a7b8", "url": "/api/logos/a1b2c3d4e5f6a7b8", "fileName": "arduino-mark.png" }

GET  /api/logos          -> [ { "id", "url", "fileName" }, ... ]

GET  /api/logos/{id}     -> the raw image bytes (whatever content-type it was uploaded as)
```

- `id` proposed as a content hash (first 16 hex chars of SHA-256) so re-uploading
  identical bytes returns the existing entry instead of duplicating storage —
  this app's local stand-in already does this; happy to defer to whatever id
  scheme pc-dash-hub prefers if content-hashing doesn't fit its storage model.
- No delete endpoint proposed yet — cleanup of orphaned/unused library entries
  isn't designed, out of scope for this draft same as macro execution was for T8.
- Accepted formats: png/jpg/jpeg/gif/bmp (matches this app's upload-dialog file
  filter). Size cap not proposed — pc-dash-hub's call since it owns the disk.

### 2. Logo assignment, keyed by an opaque string (revised per pc-dash-hub)

Original draft proposed `GET/PUT /api/projects/{project_id}/logo`, on the
assumption a `projects` collection would exist to hang it off. **pc-dash-hub
pushed back**: it has no `projects` collection today, and since agent-monitor
mode's own project-identity scheme is still undesigned (hierarchical? scoped per
device instance? unknown), baking in `project_id`-shaped semantics now risked a
reshape later. Counter-proposal, **accepted 2026-08-27** — same body, opaque key
instead of a foreign key into a collection that doesn't exist:

```jsonc
// GET/PUT /api/logo-assignments/{key}
{
  "kind": "generic",          // "generic" | "upload"
  "ref": "chip",               // generic-icon id (see above), or a library logo's `id`
  "initialsOverlay": false     // T39: explicit per-project toggle, independent of kind
}
```

`key` is just a string pc-dash-hub stores against — no assumption it's a foreign
key into any collection. Works today (this app's local store already treats its
`projectId` as an opaque string throughout, so nothing on this app's side needed
to change to agree to this), and works later under whatever agent-monitor mode
lands on for project identity, no migration needed either way. Confirmed
pc-dash-app has no objection: `project_id` semantics never actually mattered here
beyond "a stable string to key a dictionary by," so this is a pure naming/URL-shape
change, not a functional one.

Whole-object PUT (not field-by-field like `pcState`) — the object is small and
there's no equivalent of a "transient unknown reading" case that would need a
per-field omit/null distinction.

## Explicitly out of scope for this draft

- Deleting/renaming library entries.
- Any cap on library size or per-upload file size.
- How/whether the phone UI caches logo images locally vs. always resolving from
  the hub URL (flagged as an open question in the mockup's 2026-08-22 revision
  too — not resolved here either).
- Agent-monitor mode's own project-tracking/discovery mechanism — still
  undesigned; `key` in section 2 is deliberately agnostic to whatever that ends
  up being.

## Review status

- **pc-dash-hub** (2026-08-27): both endpoints built and committed — section 1
  (logo library) as drafted, section 2 (assignment) with the opaque-`key`
  revision above. Confirmed contract details (relayed via the coordinator): the
  multipart upload field name is `file`
  (`upload_logo(file: UploadFile)`), an unset key returns **404** (with a
  `{"detail": ...}` body, not 200-with-null), and `GET /api/logos/{id}`'s
  Content-Type is stored verbatim from whatever the upload sent — omit it and
  it comes back `application/octet-stream`, so this app's uploader always sets
  a real Content-Type from the file extension. **Not yet deployed** — pc-dash-
  hub committed but the rebuild/redeploy is in progress (dispatched to
  `docker` by the coordinator). `HubLogoAssignmentStore.cs` is written and
  compile-verified against this contract but not wired into `MainWindow` yet;
  holding for deploy confirmation before swapping and testing live.

- **Deployed 2026-08-27, live-tested via `test/LogoSmoke` (new, mirrors
  `HubClientSmoke`'s pattern) — found two real divergences from what was
  confirmed above, both reproduced with plain `curl` against the live hub:**

  1. **Blocking**: `PUT /api/logo-assignments/{key}` rejects a real JSON
     boolean for `initialsOverlay` with a 422 (`"Input should be a valid
     string"`) — the hub's actual model has that field typed as `str`, not
     `bool`, despite the confirmed shape being `{kind, ref,
     initialsOverlay: bool}`. Sending the string `"false"` *is* accepted,
     but it round-trips as the literal string `"false"` — dangerous for any
     future JS-based client (phone UI): `Boolean("false")` is `true` in
     JS, so a naive truthy check on this field would silently invert every
     "off" toggle. Repro:
     ```
     curl -X PUT http://pcdashhub.lan:8015/api/logo-assignments/curl-test-key \
       -H "Content-Type: application/json" \
       -d '{"kind":"generic","ref":"folder","initialsOverlay":false}'
     # -> 422 {"detail":[{"type":"string_type","loc":["body","initialsOverlay"],...}]}
     ```
  2. **Non-blocking, still a real divergence**: `POST /api/logos` and
     `GET /api/logos` both return `{id, contentType, sizeBytes, createdAt}`
     — no `url`, no `fileName` at all, not the confirmed `{id, url,
     fileName}`. `HubLogoAssignmentStore` tolerates this today (falls back
     to constructing `/api/logos/{id}` as the URL, and to the locally-known
     upload filename), so it isn't currently breaking this app, but it
     means **the original filename is never preserved hub-side** — any
     other client listing the library gets no filename info at all, only
     whatever this app happened to upload it as.

  Not fixing either myself / not silently working around #1 by encoding
  `initialsOverlay` as a string client-side — that would bake the mismatch
  into the wire contract everyone else builds against instead of fixing
  the actual bug. Reported to the coordinator for pc-dash-hub. **Leftover
  test data on the live hub from this diagnosis**: `logo-assignments`
  key `curl-test-key` (`{"kind":"generic","ref":"folder",
  "initialsOverlay":"false"}`, the string-typed value from the repro
  above) and one 68-byte synthetic 1x1 PNG in the logo library
  (id `431ced6916a2a21a`) — both clearly test artifacts, left in place
  since there's no delete endpoint (not proposed/built, out of scope per
  this doc's own "Explicitly out of scope" section).

  `MainWindow` still not wired to the hub-backed store — holding for the
  `initialsOverlay` fix at minimum before that swap.

- **2026-08-28 — confirmed gap, flagged by pc-dash-hub building T59**: does
  this app's picker assign under the same `key` T58's hook forwarder derives
  for `project` (last cwd path segment)? **No, not today.** Checked the actual
  code: `ProjectLogosWindow`'s "add project" field (`NewProjectIdBox`) is
  free-text the user types by hand — there is no code path deriving it from a
  session's cwd or repo folder name, so it's whatever string Brian happens to
  type, not necessarily matching T58's derived short name at all (e.g. typing
  "PC Dash App" wouldn't match the derived `"pc-dash-app"`). Not a bug in
  either T52 or T58 individually — both do exactly what they were each built
  to do — it's a real gap *between* them that only shows up now that T59 tries
  to join them. **Degrades safely**: a mismatched/missing key just means T59's
  session tile falls back to a generic-icon placeholder, not a crash or wrong
  logo. Confirmed to pc-dash-hub as a known gap, on record for whenever
  agent-monitor mode's real project-identity scheme gets designed (this
  doc's own "Explicitly out of scope" section already flagged that as
  undesigned) — not fixing preemptively since the right fix depends on that
  scheme (e.g. should the picker's key become the *same* derived-from-cwd
  string T58 uses, or should T58's `project` become the opaque, user-assigned
  identity instead — a real design decision, not a guess to make here).

- **pc-dash-hub fixed both (commit `595e36d`), redeployed, re-verified live
  2026-08-28**: `initialsOverlay` is now properly `bool | None` (pydantic's
  lax coercion also converts a legacy `"false"` string to a real `false`, so
  the JS-truthy footgun is closed, not just the type mismatch); `/api/logos`
  now returns real `url`/`fileName` going forward. Reran `test/LogoSmoke` —
  **all checks pass.**

  Fixing pc-dash-hub's bugs surfaced **one real bug in this app's own
  client code**: `GetLibraryAsync` treated a null `fileName` the same as a
  malformed entry and silently dropped it from the list — broke on the
  pre-fix leftover test entry (`431ced6916a2a21a`, which still has
  `fileName: null` since it predates the fix and nothing rewrites old
  entries). `LibraryLogo.FileName` is now `string?` (was non-nullable), the
  filter only requires `id`+`url`, and the local image cache derives its
  file extension from Content-Type when the filename doesn't have one,
  rather than assuming a filename is always present. All call sites
  (`LogoPickerDialog`'s swatch tooltip, the cache-path helper) updated to
  handle a null filename.

  `MainWindow` now constructs `HubLogoAssignmentStore` (was
  `LocalLogoAssignmentStore`) using `AppConfig.HubBaseUrl`. Rebuilt and
  republished to `dist/win-x64/` (GUI subsystem + `HubLogoAssignmentStore`
  symbols confirmed present). `ILogoAssignmentStore` now extends
  `IDisposable` (both implementations properly dispose their resources);
  `MainWindow.OnClosed` disposes the store on window close.
