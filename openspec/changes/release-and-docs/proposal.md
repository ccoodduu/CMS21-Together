# Proposal

## Why

Friends cannot install the mod today: there is no build to hand them, the only install route is
`tools/test-env/Deploy-Mod.ps1` into the developer's test installs, the README still describes upstream's 0.4
releases, and the client and server carry two separate hand-edited version strings (`MainMod.ASSEMBLY_MOD_VERSION`,
`Program.MOD_VERSION`, both "0.5.0", the same as upstream). Every milestone from M1 on ends with friends playing a dev
zip (ROADMAP: milestone definition of done), and 1.0 (M6) must be installable from the zip and a guide alone.

## What Changes

Part 1 (M1: dev build zip):
- **One version source**: `Directory.Build.props` holds `TogetherVersion` (e.g. `0.6.0`); client, server and Core read
  it (`BuildInfo.ModVersion`, `BuildInfo.FullVersion` with build label and git commit). The handshake compares the same
  value on both sides. Dev builds are `0.6.0-dev.<n>`; `n` counts commits since the last release tag.
- **`tools/release/Build-Release.ps1`**: builds Release, stages and zips
  - client zip: `Mods\CMS21-Together.dll`, `UserLibs\CMS21_Together_Core.dll`, `UserLibs\Facepunch.Steamworks.Win64.dll`,
    `UserLibs\steam_api64.dll`, `TogetherServer\` (the dedicated server, for hosting on the same PC), `TRY-IT.txt`;
  - server zip: the server exe, its dlls (incl. `steam_api64.dll`), `Database\*.json`, `TRY-IT.txt`; no config, logs or
    saves;
  - a manifest (`release.json`: version, commit, file hashes) in both, and a check that each zip contains exactly the
    expected files.
- **How to try (short)**: `docs/try-it.md` (also packed as `TRY-IT.txt`): MelonLoader 0.5.7, unzip into the game
  folder, back up saves, host or join, known gaps of the milestone, where the logs are.
- **Release smoke test**: the release zips are installed into the two test installs and the local server and the
  regular two-instance connect flow runs from them.

Part 2 (M6: release 1.0):
- **Guides**: `docs/install.md` (install, update, uninstall, troubleshooting), `docs/hosting.md` (host from the game,
  dedicated server, Steam vs DirectIP, port forwarding, password/admin key, moving a server save to another host,
  backups), README rewritten for the fork with credits (upstream, TogetherFixer if adapted, LvxMagick if row 15 ships).
- **Changelog and version bump policy**: `CHANGELOG.md` (Keep a Changelog), `docs/versioning.md` (when to bump
  major/minor/patch, dev labels, tags `v<version>`), `Build-Release.ps1 -Release` refuses without a changelog entry for
  the version, a clean tree and a matching tag.
- **Log collection for bug reports**: `Collect-Logs.ps1` / `Collect-Logs.bat` shipped in both zips (works when the
  game crashed and the in-game bug-report key of ROADMAP row 14 (d) cannot be used), producing the same bundle layout
  as row 14 (d); `docs/bug-reports.md` explains both ways.

No game hooks. Packets: none added; the existing mod-version compare in the handshake switches to
`BuildInfo.ModVersion` (row 9 owns the check itself).

## Capabilities

### New Capabilities
- `release-packaging`: what a release consists of (client and server zips, their exact contents, manifest, the
  how-to-try text, the offline log collector) and how it is built and smoke-tested.
- `release-versioning`: where the version comes from, how dev and release builds are labelled, when the version is
  bumped, and the changelog requirement for releases.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- New: `Directory.Build.props`, `CMS21-Together-Core/BuildInfo.cs`, `tools/release/Build-Release.ps1`,
  `tools/release/Install-ReleaseToTestEnv.ps1`, `tools/release/Collect-Logs.ps1` (+ `.bat`), `docs/` (try-it, install,
  hosting, bug-reports, versioning), `CHANGELOG.md`; `.gitignore` (`tools/release/out/`).
- Changed: `CMS21-Together-Client/MainMod.cs` and `Properties/AssemblyInfo.cs` (version constants), `CMS21-Together-Server/
  Program.cs` (`MOD_VERSION`, `SERVER_VERSION`), server `Server`/`AuthHandlers`/`SteamTransport` and client
  `AuthHandlers`/`ClientSteam` (version values sent), `README.md`, `tools/test-env/Run-All.ps1` (skips scenarios marked `# run-all: skip`), `tools/test-env/Deploy-Mod.ps1` (removes the release
  zip's `UserLibs\steam_api64.dll` and `TogetherServer\` from the lane's installs).
- Depends on: row 8 part 1 for the join steps the how-to-try describes and part 2 for hosting from the game (which uses
  `TogetherServer\`); row 9 for the handshake check; row 7 for save locations, backups and server-save moving; row 14 (d)
  for the bug-report bundle layout (part 2).
- Test harness: `build-info` verb, `scenarios/release-smoke.ps1`.
