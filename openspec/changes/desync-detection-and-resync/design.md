# Design

## Context

See proposal.md — Why. The default-deny guard (former part (a), decisions D1–D6) is now the change
`multiplayer-guard` (ROADMAP row 14a); this change keeps parts (b)–(d), renumbered D1–D5 (old D7–D11). Observed in
the code and the game stubs (`%USERPROFILE%\CMS21-TestInstalls\decomp`):

- Scenes: `NotificationCenter` — `IEnumerator SelectSceneToLoad(string, SceneType, bool useFader, bool saveGame)`
  (guarded by row 14a, prefixed by row 6's `SceneHooks`); `HandleDisconnect` starts it with
  `NotificationCenter.m_instance.StartCoroutine` today.
- Row 14a: `FeatureGuard.Bypass` scope (the mod's own scene loads and windows pass the guard), the guard's 200-entry
  ring buffer. Row 8: `ModNotify.Message` for in-game popups.
- Row 7: `SessionGuard.Active` (client, from `StartGame` until the menu has loaded), `StateLock`, snapshot providers,
  `SyncTracker.InSnapshot`, `ClientData.IsInitialSyncFinished`. A repeated `AskForSync` gets a new snapshot. Row 6:
  `ClientScene.LocalScene`, `GarageBound` queue, the scene prefix raising `LeavingScene`; returning to the garage runs
  `CustomLoad` → `AskForSync`, a full snapshot. Row 1: `PartRegistry`, the record builder used for `CarPartsChange`,
  `CarPartsStore`, single-car `CarPartsSnapshot` (`SnapshotId = 0`, applied without reload unless `carToLoad`
  differs), car `IsReady`/`PartTransaction`. Row 2: parking mirror, `ParkingResyncRequest` answer
  (`ParkingState` + slot updates), `LifterState`.
- Packets are `BinaryFormatter`; the client has Newtonsoft.Json and `System.IO.Compression` (MelonLoader's managed
  folder). Server logs go to `Log/` (not `Logs/`; `Latest.txt` + `Log_<ts>.txt`, `Program.Main`, `MultiTextWriter`).

## Goals / Non-Goals

**Goals:** (b) silent drift is found and fixed within about a minute without player action, with a diff that points
at the missed hook; (c) one key fixes a broken garage; (d) one key captures a bug from every side. Nothing changes
outside a session.

**Non-Goals:** the guard (row 14a); reconciling positions, seats, engine state, claims (runtime only) or anything away
from the garage; replacing rows' own resync requests (they stay; this change calls them); uploading reports.

## Part (b) — reconciliation (M2)

### D1. Projection and digest

- A **projection** is a canonical, sorted list of rows `(Id, Field, Value)` built from the Core DTO a row already
  syncs (server: its stored DTO; client: the DTO built from game objects by that row's existing converter). Values are
  invariant-culture strings; floats rounded to 3 decimals, vectors per component; bools `0/1`. Core `CanonicalHasher`:
  FNV-1a 64 over `Id|Field=Value\n` in ordinal order. One mapper per section in Core turns the DTO into rows, so both
  sides share it; only "how to get the DTO" differs per side.
- Interfaces: client `IClientDigest { string Key; IEnumerable<string> SubKeys(); DigestState TryProject(subKey, out
  Projection) }` (`DigestState.Ready | NotReady`); server `IServerDigest { Key; SubKeys(); Projection Project(subKey);
  void Resend(int clientId, string subKey) }`. Found by `[DigestSection]` like row 7's `[SessionSection]`.
- Sections implemented here (all exist by M2): `world` (money, scrap, level, exp), `garage` (upgrade and skill
  levels), `inventory` (items and groups by UID: id, condition, quality, count; inventory and warehouse), `cars`
  (subKey = loader id; row 1's part records: key → mounted, condition, quality, dent, examined …), `car-placement`
  (lift state per loader, parking slot → car id). Rows 3 (`jobs`), 4 (`car-details:<loader>`) and 5a
  (`workshop-tools`) register a digest when they land (one task each, see review.md). The server resends through
  two small APIs the owning rows expose for this change (INTEGRATION.md): row 1 `CarsSnapshotProvider.SendCar(clientId,
  loader)` and row 2 `ParkingService.SendFullState(clientId)`, each callable without a request packet.

### D2. Pull model, confirmation, repair

```
server tick (main loop, under StateLock, every desync_check_interval_seconds = 5)
  for each client InSession:
    StateDigestRequest{Seq, [world, garage, inventory, car-placement, cars:<next loader round-robin>]}
client (main thread):
  skip unless IsInitialSyncFinished && LocalScene == Garage && !InSnapshot && GarageBound queue empty
  StateDigest{Seq, Trigger = Poll, Entries[(key, subKey, hash | NotReady)]}
server on StateDigest (under StateLock):
  for each entry: serverHash = hash(Project(subKey))
    match → clear; NotReady → clear
    mismatch → if previous result for (client, key, subKey) was a mismatch with the same serverHash and clientHash
               → CONFIRMED, else remember and wait for the next round
```

- Car round-robin: one car per request, so with four cars each car is checked every 20 s; global sections every 5 s;
  the spec's 30 s bound holds up to six loaded cars, more cars lengthen it (logged). Client cost is one projection per
  car per round, measured in task 2.6.
- `NotReady` (client): car not `IsReady`, an open `PartTransaction` or own claim on that car, or any queued live packet.
  This and the "both hashes unchanged" rule keep in-flight changes from counting.
- Why hashing at receipt is tight: the request travels on the same reliable, ordered stream as the live updates, and
  the client's own change packets precede its `StateDigest` on its stream, so when the server hashes, both sides have
  seen the same updates except those from other clients in the meantime — which the two-round rule absorbs.
- At most one outstanding request per client: a client that skipped (away, snapshot, queue) sends nothing and the
  next tick sends a new `Seq`; answers with an old `Seq` are dropped.
- Server side: not a hash of `ISaveSection.Save()` — the save JSON holds server-only fields (row 1's baseline,
  `Revision`, `SpawnedBy`, claims) the client cannot rebuild. `IServerDigest` lives on the same `[SessionSection]`
  class and projects the stored DTO through the shared Core mapper. Cost under `StateLock`: one projection per entry
  (inventory and one car are the large ones, a few thousand rows); task 2.6 logs the server time per round, and a
  per-round cache of the server projections (same state for every client) is added only if it exceeds 5 ms.
- **Confirmed**: if `desync_autofix = true` (default) and the (client, key, subKey) is not in backoff: send
  `StateDetailRequest{Key, SubKey}`; on `StateDetail` (or after 5 s without it) write the diff record (D3), then
  `IServerDigest.Resend(clientId, subKey)`:

| Key | Resend through (owner) |
|---|---|
| `world` | `WorldState` (`updateGamemode = false`) — row 7's section |
| `garage` | `GarageState` with computed `AvailablePoints` — row 7 |
| `inventory` | the inventory batches of row 7's provider, sent live (not counted by `SyncTracker`); the client handler replaces both lists on `IsFirstBatch` (task 2.3 verifies) |
| `cars:<loader>` | row 1's single-car `CarPartsSnapshot` (`SnapshotId = 0`) via `CarsSnapshotProvider.SendCar` (the same builder as the `CarPartsResyncRequest` answer) |
| `car-placement` | row 2's `ParkingService.SendFullState` (the `ParkingResyncRequest` answer) plus one `LifterState` per loader |

- Rejected: push model (clients send digests on their own timer — the server cannot spread the load or pick cars),
  comparing only on demand (misses drift until someone notices), full-state comparison without hashes (bandwidth),
  reloading the whole garage on any mismatch (visible to the player every time).

### D3. Diff log, backoff, notice

- Record `Log/desync/<utc>_<player>_<key>_<subKey>.json`: player name/slot, key, subKey, both hashes, both
  projections, and `Diff` = rows that differ (`Id`, `Field`, `Client`, `Server`), plus a summary line in the server log
  (`[Desync] cars:3 player2: 2 fields differ (door_front_left.condition 0.420 vs 0.800, …)`). The `desync` command
  lists the last 20 and counts per section.
- Backoff: a second confirmed desync of the same (client, key, subKey) within 60 s of a resend → `Persistent`: no
  automatic resend for 5 min, `DesyncNotice{Key, SubKey, Persistent = true}` to that client, which shows "Car on lift 3
  is out of sync — press F7 to resync" (popup through row 8's `ModNotify.Message`, as in row 14a D3). One resend per client per 5 s at most.

### Late join and away

Digests start only after the client's `SyncAck` (row 7 `InSession`) and only while it is in the garage; a late joiner
is never compared mid-snapshot. Leaving the garage clears that client's remembered mismatches.

## Part (c) — manual resync (M2)

### D4. Resync = reload the garage

Key (MelonPreferences `CMS21Together.ResyncHotkey`, default `F7`) → `ResyncController.Request()`: refused with a message unless
connected, `IsInitialSyncFinished`, `LocalScene == Garage`, not `InSnapshot`, 30 s since the last one, and no car
this client spawned is still waiting for its baseline upload (row 1 deletes such a car with
`ClearLoader(SpawnerLeft)` when its spawner leaves the garage, and a garage reload counts as leaving). Then:
1. send `StateDigest{Trigger = ManualResync}` with the full digest of every section and car (the server logs "manual
   resync by X" and which sections mismatched at that moment — useful evidence even though nothing is repaired);
2. inside `FeatureGuard.Bypass`, `NotificationCenter.m_instance.StartCoroutine(SelectSceneToLoad("garage",
   SceneType.Garage, true, false))` — the coroutine overload, as `HandleDisconnect` does today, so row 6's scene prefix
   runs.
   Row 6's scene prefix raises `LeavingScene(Garage, Garage)` and publishes `Loading` (row 13 flushes its results; row
   6 releases claims via `SceneChanged`); `CustomLoad` → `AskForSync` → full snapshot (row 7), cars from the snapshot
   (row 1 D9); row 6's spawn placement (from M5 row 7's `PlayerRestore`) places the player.

Rejected: an in-place `AskForSync` without reload. Row 1's snapshot path loads cars into empty loaders (its D9 step 1
deletes local cars during `VanillaLoad`), and other snapshot handlers assume a fresh garage; making every handler
idempotent over live state is more work and more risk than a 10–20 s reload. The harness verb `resync [force]` runs
`Request()`; `force` skips only the cooldown, so scenarios of later rows use it instead of an in-place `AskForSync`
(row 5a's late-join-with-own-save check dropped its own `tool-resync` for it in the integration pass).

## Part (d) — bug report (M5)

### D5. Bundle

Key (MelonPreferences `CMS21Together.BugReportHotkey`, default `F8`; F7 is the resync key, F9 row 8's session panel),
one per 30 s. Id `yyyyMMdd-HHmmss-<4 hex>`. Layout and redaction are the shared bug-report layout in
`openspec/INTEGRATION.md` (owner: this change; `release-and-docs` D6's offline `Collect-Logs.ps1` produces the subset
that exists without the game running): `client\…` and `server\…` roots, each with `info.json`.
- Client writes `UserData/CMS21Together/BugReports/<id>.zip` with `client\`: `info.json` (report id, UTC time, mod
  version and full version, game version, connection state, scene, player slot/name), `MelonLoader\Latest.log`
  (opened with `FileShare.ReadWrite`) and the newest 5 `MelonLoader\Logs\*.log`, `UserData\CMS21Together\*.json`
  except row 7's `player.json`, `files.txt` (the `Mods\`/`UserLibs\` file list with sizes), `MelonPreferences.cfg`
  reduced to the `CMS21Together*` categories and redacted, `mods.json` (row 9's `ModInventory` with classification),
  `guard.log` (row 14a's ring buffer), `state\<key>.json` (projections of every digest section, D1, when in session
  and in the garage). Written on a worker thread from data captured on the main thread; the game does not pause.
- If connected: `BugReportRequest{Id, Note = ""}` → server writes `BugReports/<id>.zip` with `server\`: `info.json`
  (id, time, server full version, player count), `Log\Latest.txt` and the newest 5 `Log\Log_*.txt`,
  `server_config.ini` redacted, `save.json` (row 7's envelope built under `StateLock`, written outside it, with every
  `players[].Key` removed — identity keys are bearer secrets), `state\` (server projections of every section),
  `Log\desync\` from the last hour, `players.json` (slot, name, scene, game version, mods; no identity keys); then
  `BugReportCollect{Id}` to every other in-session client (each writes its own bundle, shows "Bug report <id> saved")
  and `BugReportResult{Id, ServerFile}` to the reporter.
- Redaction (one rule for both zips and `Collect-Logs.ps1`, Core `Diagnostics/Redaction`): the value of every config
  or preference entry whose name contains `token`, `password`, `secret` or `key` (case-insensitive) becomes
  `<redacted>`, except names containing `Hotkey` (key bindings such as `ResyncHotkey`). This covers `GSLT_Token`,
  `password`, `admin_key` and `CMS21Together.AdminKey`. `player.json` is never included and every save copy drops
  `players[].Key`.
- The popup tells the reporter where the client file is and the server file name; row 12's guide tells friends to send
  their zip and the host to send the server's.
- Rejected: transferring server bundles to the client (size vs. Steam message limits, needs chunking); one bundle on
  the server only (friends' logs stay on their machines).

### What the server stores vs relays (all parts)

Stores nothing in the save. Writes `Log/desync/*.json` and `BugReports/*.zip`; keeps per-client mismatch memory and
backoff in RAM. Relays `BugReportCollect` to other clients.

## Risks / Trade-offs

- [Projection differs although state is equal (float noise, order, client-only fields)] → quantization, sorting,
  Core mapper shared; task 2.6 runs a 10-minute two-instance session that must show zero confirmed desyncs.
- [Resend fights a player who is working on the car] → `NotReady` while a transaction/claim is open; confirmation
  needs two stable rounds; backoff.
- [A digest for rows 3/4/5a is missing until they add it] → drift there stays undetected (manual resync still works);
  tracked in review.md and INTEGRATION.md.
- [Reload for manual resync takes 10–20 s and moves the player] → acceptable for a repair key; row 7's `PlayerRestore`
  (M5) puts the player back.
- [Bug bundle exposes data] → redaction rule + spec; logs contain player names only.
- [The game binds F7 or F8] → row 14a's trace (task 1.1) checks it; the keys are preferences.

## Migration Plan

No save change. New `server_config.ini` keys `desync_check_interval_seconds = 5` (0 = off) and `desync_autofix =
true`, appended if missing. Client preferences get defaults. Each part ships behind its milestone; part (b) can be
switched off by setting the interval to 0.

## Open Questions

1. Client digest cost per car through IL2CPP interop (task 2.6 measures it; a slower interval for cars is the fallback).
