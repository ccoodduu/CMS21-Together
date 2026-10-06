# Review: release-and-docs (2026-10-06)

Self-review against ROADMAP (row 12, M1 and M6, milestone definition of done, working rules), INTEGRATION.md,
QUESTIONS.md, the drafts of rows 7 and 8, and the repo (`Deploy-Mod.ps1`, `Run-Session.ps1`, csproj files, build
outputs, version constants).

## Conflicts and integration points

1. **Server inside the client zip (`TogetherServer\`).** Row 8 part 2 starts the server from there; M1 hosts run it
   from there by hand. Same decision as row 8's review question 2.
2. **Version constants move to Core's generated `BuildInfo`.** Users: row 9's handshake check (compares
   `BuildInfo.ModVersion`), row 7's save envelope `ModVersion` (task 1.2 switches it), row 8's version-mismatch message.
   Add `BuildInfo` to the API matrix (owner 12).
3. **Row 14 (d) bug-report bundle (M5) vs `Collect-Logs.ps1` (M6).** One layout for both; whichever is drafted first
   defines it (D6, task 6.1). Row 14 is not drafted yet; its draft should reference D6.
4. **Steam DLL in the harness.** Installing a release zip puts `steam_api64.dll` into the test installs' `UserLibs` and
   turns Steam on there; `Deploy-Mod.ps1` now removes it again (task 2.4) so other scenarios keep today's Steam-free setup.
5. **ROADMAP working rules** already require "a dev zip built" per milestone. Recommendation for the coordinator: name
   `Build-Release.ps1` + `release-smoke` + updating `docs/try-it.md` in the milestone definition of done (not edited
   here; ROADMAP is coordinator-maintained).
6. **Harness registration** (not yet in INTEGRATION.md): verb `build-info` (owner 12), scenario `release-smoke`, scripts
   `tools/release/Build-Release.ps1`, `Install-ReleaseToTestEnv.ps1`, `Collect-Logs.ps1`. No packets, sections or
   `SyncOrder` slots.
7. **Row 8 task 4.6** writes the join section of `docs/try-it.md` before this change's part 1 (M1 order: 8 → 9 → guard →
   dev zip); task 3.1 here completes the file.

## Open questions (default in use)

1. Version numbering: M1 dev builds are `0.6.0-dev.n` (break from upstream's 0.5.0), one minor per milestone, 1.0.0 at
   M6.
2. `.pdb` files ship in both zips (better stack traces; small).
3. Docs and `TRY-IT` are in English (README is English). Ask if the friends would rather have Danish for `TRY-IT`.
4. A GitHub release is only drafted on request (`-DraftGitHubRelease`); sharing zips with friends stays the user's step.

## Risks

- `Server\Libs\steam_api64.dll` might not match the Facepunch build → checked on the real install (task 2.3).
- Two harness instances with Steam on (same account) may misbehave in `release-smoke` → `-NoSteamLib` fallback.
- Exact version compare makes every dev build incompatible with the previous one; intended, and the mismatch message
  names both versions (row 8).
- Antivirus warnings for an unsigned exe/`.bat` → install troubleshooting section.

## Size

Part 1 (groups 1–3): S, ≈ 1–2 sessions. Part 2 (groups 4–7): S, ≈ 2 sessions (guides take the most time). Matches the
roadmap's "S (+S for the M1 dev build)".

## Second review (2026-10-06)

Independent review against the repo (csproj files, build outputs, `Deploy-Mod.ps1`, `Run-Session.ps1`,
`TestLanes.psm1`, version constants, `SteamTransport`) and the drafts of rows 7, 8, 9 and 14.

### Fixed in this pass

1. **Harness would report its own version.** `BuildInfo` constants are inlined at compile time, and the harness is
   built separately from the release zip, so `build-info` reading `FullVersion` would always show `-local` and
   `release-smoke` could never pass. D1 adds `BuildInfo.LoadedFullVersion` (property inside Core); task 1.3 uses it.
2. **Stale label on incremental builds.** D1/task 1.1: the generated file is rewritten when label/commit change
   (`WriteOnlyWhenDifferent`), verified by a second build with another label.
3. **Steam `VersionString`.** Steam expects a dotted numeric version; it now gets the plain `TogetherVersion`, the
   banner keeps `FullVersion` (D1, task 1.2). Task 1.2 lists all four `ConnectPacket` send sites (incl. `Server.cs`).
4. **Test lanes.** `Install-ReleaseToTestEnv.ps1` and `Deploy-Mod.ps1` work per lane (`-Lane`, lane installs and server
   dir, lane port kept), `release-smoke` takes addresses from `$Ctx.Lane` (D4, tasks 2.4, 2.5, 7.1).
5. **Task 2.3 used the user's main install** (other mods, real saves). Now test install A + the lane's server with
   `--use-steam true` (row 8's `Start-TestServer -Arguments`, fallback: config edit).
6. **`release-smoke` in `Run-All`** would fail against every dev deploy; it is excluded from `Run-All`.
7. **Log bundle vs row 14 (d).** Row 14 is now drafted (D11: separate client/server zips with a shared id). D6 now
   mirrors that layout (`client\`/`server\`, `info.json`, Latest + previous log, preference sections) for everything
   producible offline, includes existing in-game bundles, and defines one redaction rule (`token`/`password`/`key` in the
   key name). Task 6.1 became a re-check against row 14's landed code; 6.2's verification uses distinctive secret
   values incl. `Together.AdminKey`.
8. **Hosting guide content.** Ports (TCP+UDP `port`, relay needs none, query port `port + 1`), all config keys of rows
   7/8/9, gameplay-mod refusals in install troubleshooting (row 9 asks for it), Steam client needed for Steam joins
   (new risk: a dedicated server on a PC without Steam probably has no Steam transport).
9. Proposal impact: `Deploy-Mod.ps1` (not `Run-Session.ps1`) is the changed harness script.

### Remaining (for the integration pass / user)

1. **Row 14's redaction misses `admin_key`/`AdminKey`** (it redacts `token`/`password` keys) — row 14's draft should
   adopt D6's rule. Row 14 also names the server log folder `Logs/` while the server writes `Log\`; its `Logs/desync/`
   would be a second folder. Proposed default: row 14 uses `Log\` and `Log\desync\`.
2. **INTEGRATION.md registration** (not edited here): `BuildInfo` (owner 12; users 7 envelope, 8 message, 9 check),
   verb `build-info`, scenario `release-smoke` (not in `Run-All`), scripts in `tools/release/`, the shared redaction rule.
3. **Milestone DoD wording** (ROADMAP, coordinator): name `Build-Release.ps1` + `release-smoke` + `docs/try-it.md` in the
   milestone definition of done (unchanged recommendation from the first review).
4. Open defaults unchanged: `0.6.0` for M1 / `1.0.0` for M6, `.pdb` files shipped, English docs (ask whether `TRY-IT`
   should be Danish for the friends), GitHub release drafted only on request, server inside the client zip.
5. **Size**: part 1 S (≈ 2 sessions, the MSBuild generator and the harness/lane plumbing are the risk), part 2 S–M
   (≈ 2–3: guides plus the move-save and redaction checks). Matches the roadmap within the 50 % margin.

## Integration pass (2026-10-06)

Applied: the bug-report layout and redaction rule now live in INTEGRATION.md (owner row 14 (d)); D6 follows it
(`info.json` in both roots, newest 5 logs, `CMS21Together*` preference categories, `secret` added and `*Hotkey*`
exempted, `-IncludeSave` drops `players[].Key`); row 14 adopted `Log\`, `admin_key` and `CMS21Together.AdminKey`.
`release-smoke` is excluded from `Run-All` by a `# run-all: skip` marker (task 2.5, `Run-All.ps1` change owned here).
`BuildInfo`, `build-info`, scripts and scenario are registered in INTEGRATION.md; the milestone definition of done in
ROADMAP now names `Build-Release.ps1`, `release-smoke` and `docs/try-it.md`.

## Implementation notes (part 1, 2026-10-06)

1. **`BuildInfo.Version` added** (the plain `TogetherVersion`, e.g. `0.6.0`): the Steam `VersionString` needs it (D1) and
   the generated class is its only compile-time source. Additive; the other members are as in D1.
2. **Name clash with `MelonLoader.BuildInfo`.** Client and harness files that also import `MelonLoader` must write
   `using BuildInfo = CMS21_Together_Core.BuildInfo;` (done in `MainMod.cs` and the harness `build-info`). Rows 8 and 9
   hit this when they show or check versions on the client.
3. **Release label.** `-p:TogetherBuildLabel=` (empty) builds the plain version; part 2's `-Release` uses that. A global
   property overrides `Directory.Build.props`, so no extra switch is needed.
4. **`Build-Release.ps1` details.** `.dirty` is appended only when the tree is actually dirty (with `-AllowDirty` on a
   clean tree the build is clean). The version check reads `BuildInfo` from each Core dll inside the finished zips and
   looks for the `ModVersion` constant in the mod dll and the server exe, so a stale client or server fails too.
   `-DropFromStaging <path>` exists only to test the content check (task 2.2).
5. **`Run-All.ps1`** now honours the marker only on the first line (it matched any line before) and has `-List` for the
   dry listing of task 2.5.
6. **`Install-ReleaseToTestEnv.ps1`** clears the lane's server folder except `server_config.ini`, `Saves\`, `Log\` and
   `BugReports\` before extracting, so files of the dev deploy do not mix with the release. The lane config helper and
   the list of release-only files moved into `TestLanes.psm1` (`Set-LaneServerConfig`, `Remove-ReleaseOnlyFiles`),
   shared by `Deploy-Mod.ps1`; the dev deploy also removes the release's `.pdb`s and root `CMS21-Together-*` files.
