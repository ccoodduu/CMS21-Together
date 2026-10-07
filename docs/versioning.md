# Versions and releases

This page is for whoever builds the zips. Players only need to know one rule: **the host and every player must use
the same build.** The server refuses any other build and names both versions in the message.

## Where the version comes from

There is one version number, `TogetherVersion` in `Directory.Build.props` (for example `0.6.0`). The mod, the
server and the zip names all take it from there. The connection handshake compares it exactly on both sides.

A build adds a label to it:

| Build | Version | Made by |
|---|---|---|
| Release | `1.0.0` | `tools/release/Build-Release.ps1 -Release` |
| Development | `1.0.0-dev.<n>`, n = commits since the last release tag (all commits when there is none) | `tools/release/Build-Release.ps1` |
| Local | `1.0.0-local` | any plain `dotnet build` (IDE, `tools/test-env/Deploy-Mod.ps1`) |

The full version in the logs also names the commit, for example `1.0.0-dev.12+c0647af`. A build made from
uncommitted changes (`-AllowDirty`) ends in `.dirty`. You can find it in the MelonLoader console
(`Together Mod <full version> initialized!`), in the server window title and in `release.json` inside each zip.

## When the number changes

Before 1.0:

- each milestone release raises the minor version: `0.6.0` = M1, `0.7.0` = M2 ... `0.10.0` = M5;
- a fix release of the same milestone raises the patch version (`0.6.1`);
- `1.0.0` is the M6 release.

After 1.0, the number tells players what changed (the handshake requires equal versions anyway):

- **patch** (`1.0.1`): fixes only, no change to the save format or the network protocol;
- **minor** (`1.1.0`): new shared features or a protocol change;
- **major** (`2.0.0`): a save format that needs a migration an older server cannot read back.

The save format has its own number (`SaveVersion` in the server save), independent of this one. A server loads saves
of older versions and refuses saves of newer ones.

**After a release, the first commit raises the version** to the next planned one (`chore: bump version to 1.1.0`), so
development builds of the next milestone already carry the next number.

History: the milestones M1 to M4 were only shared as development builds of 0.6.0 and never tagged, so the version
stayed 0.6.0 until 1.0. 1.0.0 is the first release of this fork.

## Making a release

1. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [<version>] - <date>` and start a new, empty
   `## [Unreleased]` above it.
2. Set `TogetherVersion` in `Directory.Build.props` to the release version. Commit both
   (`chore: release <version>`).
3. Tag the commit: `git tag -a v<version> -m "CMS21 Together <version>"`.
4. Run `tools/release/Build-Release.ps1 -Release`. It refuses to build when:
   - the working tree has uncommitted changes (it lists them),
   - `CHANGELOG.md` has no `## [<version>]` section, or the section is empty,
   - HEAD does not carry the tag `v<version>` (it prints the tag command; it never tags by itself).

   The zips `CMS21-Together-<version>-client.zip` and `-server.zip` and `SHA256SUMS.txt` land in
   `tools/release/out/`.
5. Run the release smoke test with them (`tools/release/Install-ReleaseToTestEnv.ps1`, then the `release-smoke`
   scenario and the full regression run).
6. Push the tag (`git push origin v<version>`). Publishing is a separate decision: only when asked,
   `Build-Release.ps1 -Release -DraftGitHubRelease` creates a **draft** GitHub release with the changelog section and
   both zips. Someone publishes the draft by hand.
7. Raise the version for the next milestone (see above).
