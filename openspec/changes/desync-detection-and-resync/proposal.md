# Proposal

## Why

Every row syncs only what it hooks, and hooks miss: IL2CPP inlines methods, coroutines fire their patch only on start,
and a dropped packet is never noticed. Players then work on different cars without knowing it, and when they notice,
the only fix is reconnecting. Bugs from a friend's session arrive as "it broke" without logs. (Unsynced features are
kept out by the default-deny guard, `multiplayer-guard`, ROADMAP row 14a, split from this change on 2026-10-06.)

## What Changes

Three parts that land in different milestones (task groups split by part):

**(b) Continuous state reconciliation — M2**
- Every few seconds the server asks each in-session client in the garage for **state digests** of a few sections
  (shared stats, garage upgrades, inventory, each loaded car, parking/lifts). Client and server hash the same
  canonical projection with code in Core. A mismatch confirmed twice in a row while neither side changed is
  reloaded for that client from the server through the owning row's existing resend path, and the field-level diff
  is logged on the server.

**(c) Manual resync key — M2**
- A key (default F7) reloads the garage, which reruns the late-join snapshot (row 7's `AskForSync` path, the same as
  row 6's return to the garage).

**(d) One-key bug report — M5**
- A key (default F8; F9 is row 8's session panel) writes a bundle in the shared bug-report layout (INTEGRATION.md,
  also used by `release-and-docs`' offline collector) on the reporting client (logs, client state projections, mod
  list, guard log) and asks the server to write its own bundle with the same id (logs, save copy without identity
  keys, server projections, desync logs, redacted config); other connected clients write theirs too.

**Packets** (appended to `PacketTypes`): `StateDigestRequest` (S→C), `StateDigest` (C→S), `StateDetailRequest`
(S→C), `StateDetail` (C→S), `DesyncNotice` (S→C), `BugReportRequest` (C→S), `BugReportCollect` (S→C),
`BugReportResult` (S→C). No changed packets. No new `DisconnectReason` values.

**Hooks**: none on game methods. The resync reload starts the game's own `NotificationCenter.SelectSceneToLoad`
coroutine inside `FeatureGuard.Bypass` (row 14a), so row 6's scene prefix runs as on any return to the garage.

**Out of scope**: the default-deny guard and the single-player audit (row 14a); reconciling state that is not stored on
the server (positions, seats); uploading bug reports anywhere; automatic reconnect.

## Capabilities

### New Capabilities
- `state-reconciliation`: detecting that a client's shared state differs from the server's, repairing it
  automatically or on request, and recording what differed.
- `bug-report-bundle`: collecting client and server diagnostics for one incident with a single key press.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `PacketTypes.cs` (appended), new `Network/Packets/DiagnosticsPackets.cs`, new `Data/Digest/`
  (`CanonicalHasher`, projection DTO helpers), new `Diagnostics/Redaction` (the shared redaction rule).
- Client: new `Reconciliation/` (`IClientDigest` per section, digest/detail handlers, `ResyncController`), new
  `Diagnostics/BugReport`, `MainMod` (preferences `CMS21Together.ResyncHotkey`, `CMS21Together.BugReportHotkey`).
- Server: new `Data/Reconciliation/` (`IServerDigest`, `ReconciliationService` tick under `StateLock`,
  `Log\desync\`), new `Data/Diagnostics/BugReportWriter`, `server_config.ini` (`desync_check_interval_seconds`,
  `desync_autofix`), commands `desync` and `bugreport`.
- Uses (not owns): row 14a `FeatureGuard.Bypass` and the guard ring buffer; row 7 `StateLock`, `SyncTracker`,
  providers; row 6 `ClientScene`; row 1 `PartRegistry`, record builder, `CarsSnapshotProvider.SendCar`; row 2
  `ParkingService.SendFullState`; row 8 `ModNotify`; row 9 `ModInventory`.
- Rows 3, 4, 5a register a digest when they land (coordination in design D1).
- Test harness: `Features/ReconcileCommands.cs`; scenarios `desync-autofix`, `resync-key`, `bug-report`.
