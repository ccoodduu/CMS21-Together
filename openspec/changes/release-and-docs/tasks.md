# Tasks

Part 1 = M1 (groups 1–3: version source, build script, how-to-try, smoke test). Part 2 = M6 (groups 4–6: guides,
changelog/policy, log collection). Steps marked **[user]** need the user; publishing anything outside the repo is
always the user's decision.

## 1. Single version source

- [ ] 1.1 `Directory.Build.props` (`TogetherVersion` 0.6.0, `TogetherBuildLabel` local) and a Core MSBuild target writing `obj\BuildInfo.g.cs` (`ModVersion`, `FullVersion`, `Commit`; commit from `git rev-parse --short HEAD`, empty when git is unavailable; `WriteOnlyWhenDifferent`, plus the `LoadedFullVersion` property, D1); verify `dotnet build` of the solution succeeds and the generated file shows `0.6.0-local`, and that a second incremental build with `-p:TogetherBuildLabel=dev.1` regenerates it
- [ ] 1.2 Client `MainMod.ASSEMBLY_MOD_VERSION`/`MOD_VERSION` and `MelonInfo` from `BuildInfo`; server `Program.MOD_VERSION = BuildInfo.ModVersion`, `SERVER_VERSION` replaced by `FullVersion` in the banner and by the plain `TogetherVersion` in the Steam `VersionString` (D1); every `ConnectPacket` send site (server `Server.TcpConnectCallback` and `SteamTransport` welcome, client `AuthHandler.HandleConnect` and `ClientSteam.OnConnected`) and row 7's save envelope `ModVersion` use the constants; verify `connect` passes and both logs print `0.6.0-local+<sha>`
- [ ] 1.3 Harness `build-info`: the loaded build's version through `BuildInfo.LoadedFullVersion` (never the const, which the harness would inline at its own compile time), paths and SHA-256 of the loaded `CMS21-Together.dll`, `CMS21_Together_Core.dll`, `Facepunch.Steamworks.Win64.dll`, whether `UserLibs\steam_api64.dll` exists, `MainMod.IsSteamAvailable`; verify on instance A

## 2. Build script and smoke test

- [ ] 2.1 `tools/release/Build-Release.ps1` (D3 steps 1–4): label `dev.<n>` (commits since the last `v*` tag, all commits if none), dirty-tree check with `-AllowDirty` (`.dirty`), Release build of client and server with `-p:TogetherBuildLabel`, staging, both zips + `release.json` + `SHA256SUMS.txt` in `tools/release/out/` (gitignored); verify a run on a clean tree produces both zips named `…-0.6.0-dev.<n>-client.zip`/`-server.zip`
- [ ] 2.2 Expected-file lists in the script for both zips (incl. `TogetherServer\` in the client zip, `.pdb`s) and the version check against the built Core; verify by deleting `steam_api64.dll` from the staging copy in a test run that the script fails naming it and leaves no zip
- [ ] 2.3 `steam_api64.dll` check on test install A (never the user's main install, which carries other mods and the real saves): with the client zip installed and Steam running, `IsSteamAvailable` is true; the server from the server zip, started in the lane's server dir with `Start-TestServer -Arguments '--use-steam true'` (row 8 task 2.3; otherwise a temporary `use_steam = True` in the run's config), logs a Steam ID; record the dll's version in design.md D3; **[user]** only if the run needs the user's Steam login confirmed
- [ ] 2.4 `tools/release/Install-ReleaseToTestEnv.ps1 -Lane <n>` (D4: client zip into the lane's two installs + harness dll, server zip into the lane's server dir keeping config/saves/logs, `-NoSteamLib`); `Deploy-Mod.ps1` removes `UserLibs\steam_api64.dll` and `TogetherServer\` from the lane's installs so later dev runs stay Steam-free as today; verify `build-info` on the lane's first instance shows the zip's version and hashes equal to `release.json`
- [ ] 2.5 `scenarios/release-smoke.ps1` (two instances, after `Install-ReleaseToTestEnv.ps1`; addresses from `$Ctx.Lane`): `build-info` on A and B equals `release.json` version and hashes; server log banner shows the same `FullVersion`; A and B connect, reach the garage, `Compare-HarnessDumps` shows equal `stats`/`inventory`/`cars`; both `quit`; passes when all checks hold (if Steam in both instances breaks the run, rerun with `-NoSteamLib` and record it in STATUS.md); `release-smoke` is not part of `Run-All` (it needs a release install and fails against a dev deploy): its first line is `# run-all: skip`, and `Run-All.ps1` (which today runs every `scenarios\*.ps1`) skips files with that marker unless they are named in `-Scenarios`; verify `Run-All -SkipDeploy -Lanes 1 -Scenarios connect` still runs and a dry listing of the default scenario set leaves `release-smoke` out

## 3. How to try (part 1)

- [ ] 3.1 `docs/try-it.md` (D5, ≤ 1 page; join section from row 8 task 4.6, hosting = run `TogetherServer\CMS21_Together_Server.exe`), packed as `CMS21-Together-TRY-IT.txt` / `TRY-IT.txt`; README: replace upstream's install section with a link to it and a "fork of Fozkais/CMS21-Together" note; verify every path and file name in the text exists in the built zips
- [ ] 3.2 Run `release-smoke` and the full regression run (with the dev build redeployed by `Deploy-Mod.ps1` afterwards); all pass; record results and the zip names in STATUS.md; **[user]** hand the zip to friends (sharing is the user's step)

## 4. Guides (part 2)

- [ ] 4.1 `docs/install.md`: install, update (replace files, same version for everyone), uninstall (files to delete, `UserData\CMS21Together\`), troubleshooting (MelonLoader version, Steam DLL missing → no Steam join, antivirus, version mismatch, each refusal message of row 8/9 incl. game version/DLC/gameplay-mod differences and that gameplay mods must be removed or allowed by the host); verify each listed file/folder name against the client zip and each refusal text against row 8's `ConnectionMessages`
- [ ] 4.2 `docs/hosting.md`: host from the game (row 8 part 2), dedicated server (zip, `server_config.ini` keys: `port`, row 7's `autosave_interval_seconds`/`backup_count`, row 8's `server_name`/`public_address`/`password`/`password_steam`/`admin_key`/`new_session_difficulty`, row 9's `game_version`/`dlc`/`mods_*`, GSLT optional; Steam client required for Steam joins), Steam vs DirectIP and port forwarding (D5: TCP+UDP `port`, relay needs none), backups and autosave (row 7), moving a server save (D5); verify by following the move-save steps in the test env: stop the test server, copy `Saves\` to a second server folder from the server zip, start it, A joins and `stats` equal the old server's
- [ ] 4.3 README rewrite for 1.0: what the mod does, requirements, links to the guides and changelog, credits (Fozkais/upstream; TogetherFixer if any code was adapted — check `rg -l "TogetherFixer"`; LvxMagick if row 15 shipped), license; verify all links resolve to files in the repo

## 5. Changelog and version policy (part 2)

- [ ] 5.1 `CHANGELOG.md` (Keep a Changelog) with sections for every released milestone from STATUS.md, and `docs/versioning.md` (D2); verify the policy text matches the `release-versioning` spec
- [ ] 5.2 `Build-Release.ps1 -Release`: requires a `## [<version>]` changelog section and tag `v<version>` on HEAD (prints the tag command, does not create it), no label; `-DraftGitHubRelease` (only on request) creates a draft with `gh`; verify `-Release` fails without the changelog section and passes on a local test tag (deleted afterwards)

## 6. Log collection and bug reports (part 2)

- [ ] 6.1 Re-check D6 against row 14 (d)'s landed `BugReport`/`BugReportWriter` (M5, before this part): same entry names and `info.json` fields where the offline collector can produce them, same redaction rule (INTEGRATION.md: `token`/`password`/`secret`/`key` in the name, except `*Hotkey*`; raise it in QUESTIONS.md if row 14 landed with a different rule); verify by listing a `bug-report` bundle (harness verb, row 14) next to a collector archive and comparing entry names
- [ ] 6.2 `Collect-Logs.ps1` + `.bat` (D6): game folder or server folder, `client\`/`server\` per D6, redacted preferences and `server_config.ini`, existing in-game bundles, `-IncludeSave`, `info.json` from `release.json`; added to both zips and the expected-file lists; verify on test install A after a `connect` run with `password = pw-7f3a`, `admin_key = adm-91c2` and `GSLT_Token = "gslt-5d1e"` in the run's config and `CMS21Together.AdminKey = adm-91c2` in A's preferences: the archive contains the logs, no `player.json`, `rg` over the extracted archive finds none of the three values, `CMS21Together.SessionPanelHotkey` keeps its value, and with `-IncludeSave` the `save.json` has no `players[].Key`
- [ ] 6.3 `docs/bug-reports.md`: in-game key (row 14 (d)) and the collector, what is included, privacy note (Steam names/IDs, saves only on request); link from README and `TRY-IT`; verify the steps by running the collector from the installed client zip

## 7. Release scenario

- [ ] 7.1 Build a release candidate with `-AllowDirty` off, install with `Install-ReleaseToTestEnv.ps1 -Lane 1`, run `release-smoke` (now also checking that `Collect-Logs.bat` is present in the game folder and `TogetherServer\`) and the full regression run; all pass in two instances; record in STATUS.md
- [ ] 7.2 **[user]** 1.0 acceptance (M6 definition): a friend installs from the zip and the guides alone, without help, and joins the user's session; findings go to QUESTIONS.md "Playtest findings"
