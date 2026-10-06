# Design

## Context

See proposal.md for the motivation. Today (observed, `main`):

- Versions: client `MainMod.ASSEMBLY_MOD_VERSION = "0.5.0"` (also the `MelonInfo` version, a `const`), server
  `Program.MOD_VERSION = "0.5.0"` (compared in `AuthHandler.OnConnected`, sent in the welcome `ConnectPacket`) and
  `Program.SERVER_VERSION = "1.0"` (Steam `VersionString`, log banner). All projects have `GenerateAssemblyInfo=false`
  and hand-written `AssemblyInfo.cs` with `1.0.0.0`. Upstream used the same 0.5.0 for its Dev branch; upstream tags are
  `Release-0_4_x`/`R-…`, our fork has no tags of its own.
- Install footprint, from `Deploy-Mod.ps1`: `Mods\CMS21-Together.dll`, `UserLibs\CMS21_Together_Core.dll`,
  `UserLibs\Facepunch.Steamworks.Win64.dll`. Newtonsoft.Json comes from MelonLoader 0.5.7 (`MelonLoader\Managed`).
  `MainMod.InitializeSteam` requires `UserLibs\steam_api64.dll` (not the game's `…_Data\Plugins\x86_64` copy), so a
  zip without it silently runs without Steam. `CMS21-Together-Server\Libs\steam_api64.dll` is the copy that ships with
  the Facepunch build in use.
- Server output (`bin\Release`): exe + `.exe.config`, Core, Facepunch, Newtonsoft, Terminal.Gui, NStack, `Database\*.json`
  (≈ 5 MB); `steam_api64.dll` is not copied to the output (the test server runs with `use_steam = False`). The server
  creates `server_config.ini`, `Log\` and `Saves\` itself on first start.
- Logs: client → MelonLoader (`MelonLoader\Latest.log`, `MelonLoader\Logs\*.log`); server → `Log\Latest.txt` and
  `Log\Log_<ts>.txt`. Row 7 adds `UserData\CMS21Together\player.json` (a bearer key), `session.json`,
  `ProfileBackups\`, and server `Saves\backups\`.
- The harness (`TogetherTestHarness.dll`) and LoadOptimizer are test-only and never part of a release.

## Goals / Non-Goals

**Goals:**
- One command produces both zips from a commit, with an exact, checked file list and a version that client, server and
  zip names agree on.
- A friend can install and join from the zip plus one short page (M1), and from the zip plus the guides alone (M6).
- Bug reports arrive with the same set of logs every time, without leaking keys or passwords.

**Non-Goals:**
- An installer, auto-updater or update check (upstream's `ContentManager.IsNewVersionAvailable` is not ported).
- Publishing to Thunderstore/Nexus; publishing a GitHub release is a user decision (outward communication), the script
  only prepares a draft when asked.
- Linux/headless server packaging (backlog).
- The in-game bug-report key and its bundle code (ROADMAP row 14 (d)).

## Decisions

### D1. One version source
- `Directory.Build.props` (repo root): `<TogetherVersion>0.6.0</TogetherVersion>` and `<TogetherBuildLabel>local
  </TogetherBuildLabel>` (overridable with `-p:`). An MSBuild target in Core (`BeforeTargets="CoreCompile"`) writes
  `obj\BuildInfo.g.cs`: `public static class BuildInfo { public const string ModVersion; public const string
  FullVersion; public const string Commit; public static string LoadedFullVersion => FullVersion; }`, written with
  `WriteOnlyWhenDifferent` and regenerated whenever the label or commit changes (incremental builds must not keep a
  stale label).
  - Constants are inlined into every assembly that compiles against them. The harness (`TogetherTestHarness.dll`,
    built separately and added to release-installed test instances) therefore reads the version only through
    `LoadedFullVersion` (a property, evaluated inside the loaded Core) or the loaded mod's `MelonInfo.Version`.
  - `ModVersion` = `0.6.0` for a release, `0.6.0-dev.<n>` for a dev build, `0.6.0-local` for any plain `dotnet build`
    (IDE, `Deploy-Mod.ps1`).
  - `FullVersion` = `ModVersion+<short sha>` (`.dirty` appended when built with `-AllowDirty`).
- Client: `MainMod.ASSEMBLY_MOD_VERSION = BuildInfo.ModVersion` (a `const` from Core, so `MelonInfo` keeps working);
  server: `Program.MOD_VERSION = BuildInfo.ModVersion`, `SERVER_VERSION` removed (the banner uses `FullVersion`; the
  Steam game server's `VersionString` gets the plain `TogetherVersion`, e.g. `0.6.0`, because Steam expects a dotted
  numeric version there). The handshake keeps comparing `modVersion` exactly (row 9 owns the check; it compares this value).
- *Why exact dev labels:* any commit may change packets; friends must run the same build. Harness instances and the
  test server are always built together (`-local` on both sides).
- *Alternative:* reading `AssemblyInformationalVersion` at runtime — rejected: `MelonInfo` needs a compile-time
  constant, and turning `GenerateAssemblyInfo` on clashes with the hand-written `AssemblyInfo.cs` files.

### D2. Version bump policy (`docs/versioning.md`)
- Before 1.0: minor bump per milestone release (`0.6.0` = M1, `0.7.0` = M2 … `0.10.0` = M5), patch bump for a fix release of the same
  milestone; `1.0.0` = M6. After 1.0: semver from the player's view — patch = fixes with no save or protocol change,
  minor = new synced features or protocol change, major = save format that needs migration a downgrade cannot read.
  The handshake requires equal versions anyway, so the number tells players what changed, not what is compatible.
- Dev builds between releases: `-dev.<n>`, `n` = commits since the last `v*` tag (all commits when there is none).
- The bump is the first commit after a release tag (`chore: bump version to 0.7.0`), so dev builds of the next
  milestone already carry the next version. Save compatibility is row 7's `SaveVersion`, independent of this number.

### D3. `Build-Release.ps1`
```
Build-Release.ps1 [-Release] [-AllowDirty] [-OutDir tools/release/out] [-DraftGitHubRelease]
```
1. Read `TogetherVersion`; label = `-Release` ? none : `dev.<n>`; refuse a dirty tree unless `-AllowDirty`.
   `-Release` also requires `CHANGELOG.md` to have a `## [<version>]` section and HEAD to carry tag `v<version>`
   (the script prints the tag command instead of creating it).
2. `dotnet build -c Release -p:TogetherBuildLabel=<label>` for client and server (not the harness).
3. Stage and zip:
   - `CMS21-Together-<ModVersion>-client.zip`: `Mods\CMS21-Together.dll`, `UserLibs\CMS21_Together_Core.dll`,
     `UserLibs\Facepunch.Steamworks.Win64.dll`, `UserLibs\steam_api64.dll` (from `CMS21-Together-Server\Libs`),
     `TogetherServer\…` (the server zip's content), `CMS21-Together-TRY-IT.txt`, `CMS21-Together-release.json`.
   - `CMS21-Together-<ModVersion>-server.zip`: exe, `.exe.config`, Core, Facepunch, Newtonsoft, Terminal.Gui, NStack,
     `steam_api64.dll`, `Database\*.json`, `TRY-IT.txt`, `release.json`; no `server_config.ini`, `Log\`, `Saves\`.
   - Part 2 adds `Collect-Logs.ps1` + `Collect-Logs.bat` to both (client zip: game root; server zip: server root and
     `TogetherServer\`).
   - `.pdb` files are included next to their dlls (line numbers in bug-report stack traces; size is small).
   - `steam_api64.dll` shipped: `CMS21-Together-Server\Libs\steam_api64.dll`, file version 05.69.73.98, 262 944 bytes,
     SHA-256 `473f5a312b56519f…`. Server side checked (task 2.3): the server from the server zip with `use_steam = True`
     logged "Steam connection established! SteamID: …" (2026-10-06); the client side waits for a game run.
4. Verify each zip against an explicit expected-file list in the script (missing or extra file → fail), and that
   `BuildInfo.ModVersion` in the built Core equals the zip name. Write `SHA256SUMS.txt`.
5. `-DraftGitHubRelease`: `gh release create v<version> --draft` with the changelog section and both zips — only on the
   user's request.
- `release.json`: `{ version, fullVersion, commit, builtUtc, files: { path: sha256 } }`.
- *Why the server inside the client zip:* M1 hosts run the server from the game folder (`TogetherServer\`) without a
  second download, and row 8 part 2 starts it from there. ≈ 5 MB extra for joiners. The server zip stays for hosting on
  another machine.

### D4. Release smoke test
`tools/release/Install-ReleaseToTestEnv.ps1 -ClientZip … -ServerZip … [-Lane 1]` extracts the client zip into the
lane's test installs (`Get-TestLane`: A/B or C/D; `Mods`, `UserLibs`, `TogetherServer`, overwriting) and adds the
harness dll from its own build; extracts the server zip into the lane's server dir (`Server` or `Server2`) keeping
`server_config.ini` (with the lane's `port`), `Saves\`, `Log\`. Then `Run-Session.ps1 -Scenario release-smoke -Lane <n>`. `steam_api64.dll` in `UserLibs` turns Steam on in the harness instances (one
Steam account, two processes); the smoke test runs as shipped, and `-NoSteamLib` removes it if that turns out to break
the two-instance run (recorded in STATUS.md). The next `Deploy-Mod.ps1 -Lane <n>` restores the dev build.

### D5. How-to-try (part 1) and guides (part 2)
- `docs/try-it.md` (≤ 1 page, packed as `CMS21-Together-TRY-IT.txt`): requirements (CMS21 on Steam, no DLC, MelonLoader
  0.5.7 with link), back up saves (the mod protects them, row 7, but a backup costs nothing), unzip into the game folder,
  host (run `TogetherServer\CMS21_Together_Server.exe`, Steam on) or join (row 8 part 1: Steam "Join Game" or the
  Multiplayer panel), what works in this milestone and what the guard blocks, where the logs are, how to report.
  Updated per milestone from the STATUS.md "what to try" checklist.
- `docs/install.md`, `docs/hosting.md`, `docs/bug-reports.md`, README (fork description, install link to docs, credits:
  Fozkais/upstream, TogetherFixer if code was adapted, LvxMagick if row 15 ships; MIT license unchanged).
- Ports (`docs/hosting.md`): DirectIP needs `port` (default 7777) forwarded for TCP and UDP; Steam joins go through
  Valve's relay and need no forwarding (the game server also opens query port `port + 1`).
- Moving a server save (`docs/hosting.md`): stop the server (`/stop` or Host → Stop, so the save is final), copy
  `Saves\server_save.json` and `Saves\backups\` to the new host's server folder (dedicated or `TogetherServer\`), start
  the same or a newer server version (row 7 refuses a save newer than it supports); player identities survive (Steam IDs;
  DirectIP keys live on each client in `player.json`); the server Steam ID changes, which rich presence (row 8) hides.

### D6. Log collection (part 2)
The bug-report layout is shared with row 14 (d) (`desync-detection-and-resync` D5, owner of the layout) and recorded in
`openspec/INTEGRATION.md`: a client zip (`UserData/CMS21Together/BugReports/<id>.zip`) and a server zip
(`BugReports/<id>.zip`) with the same id `yyyyMMdd-HHmmss-<4 hex>`, roots `client\` and `server\`, each with
`info.json`. The offline collector produces every entry that exists without the game running and adds the in-game
bundles that already exist:

`Collect-Logs.ps1` (and a `.bat` that runs it with `-ExecutionPolicy Bypass`), run from the game folder or a server
folder, writes `CMS21Together-logs-<id>.zip` to the Desktop with both roots:
- `client\`: `info.json` (the shared fields that exist offline: id, time, mod version from `release.json`, game
  folder, Steam DLL present; connection state and scene `"offline"`), `MelonLoader\Latest.log` and the newest 5
  `MelonLoader\Logs\*.log`, `MelonPreferences.cfg` reduced to the `CMS21Together*` categories and redacted,
  `UserData\CMS21Together\*.json` except `player.json` (identity key), `files.txt` (the `Mods\`/`UserLibs\` files
  with sizes), and the newest 3 in-game bundles from `UserData/CMS21Together/BugReports/`;
- `server\` (from `TogetherServer\` or the folder it runs in): `info.json`, `Log\Latest.txt` and the newest 5
  `Log\Log_*.txt`, `server_config.ini` redacted, the newest 3 `BugReports/*.zip`, and `save.json` (a copy of
  `Saves/server_save.json` with every `players[].Key` removed) only with `-IncludeSave`.
- Redaction (the shared rule, same as row 14's Core `Diagnostics/Redaction`): the value of every config or preference
  entry whose name contains `token`, `password`, `secret` or `key` (case-insensitive) becomes `<redacted>`, except
  names containing `Hotkey` (key bindings). Covers `GSLT_Token`, `password`, `admin_key` (row 8) and
  `CMS21Together.AdminKey` (row 8).

If row 14 (d) changes its layout before this part starts, it updates INTEGRATION.md and task 6.1 follows the change.

### D7. Late join, server state
Not applicable: this change adds no synced state, packets or save sections. The server's only change is where its
version string comes from.

## Risks / Trade-offs

- [`steam_api64.dll` from `Server\Libs` does not match the Facepunch build] → task 2.3 checks both Steam inits with it;
  else take the dll from the Facepunch package of the same version.
- [Steam on in both harness instances breaks the smoke run] → `-NoSteamLib` fallback, recorded.
- [Dev label differs between a friend's and the host's zip] → the version-mismatch message (row 8) names both; the
  how-to-try says "everyone uses the same zip".
- [Antivirus flags the unsigned server exe or the `.bat`] → mention in install troubleshooting; no signing.
- [Redistribution of `steam_api64.dll`] → Valve's Steamworks redistributable, shipped by Facepunch and upstream releases.
- [Dedicated server on a PC without the Steam client] → a Steamworks game server normally loads `steamclient64.dll`
  through the installed Steam client, so Steam transport probably fails there (unverified; `SteamTransport.Initialize`
  catches the failure). `docs/hosting.md` names the Steam client as a requirement for Steam joins; shipping the
  SteamCMD runtime dlls is not planned (everyone in the group has Steam).
- [Logs contain Steam names/IDs] → stated in `docs/bug-reports.md`; keys and passwords are redacted, saves opt-in.

## Migration Plan

The version changes from upstream's 0.5.0 to 0.6.0(-dev.n): clients and servers must be rebuilt together (as already
required). No save data changes. Rollback = revert the change; the zips are build outputs, not committed.
