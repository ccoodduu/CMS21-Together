# Proposal

## Why

The user wants the garage's look shared (2026-10-08, ROADMAP row 28): walls, floor and whatever else the game lets a
player customise, the same for every player and kept by the server. Today the guard blocks the window
(`Window GarageCustomization`, "backlog"), and if it were open each player's change would stay on their own client and
be lost at the end of the session: a session profile is created fresh for every join (`ModGameManager.StartGame`), so
every client starts with the default look.

What the game offers (static spike, `docs/spikes/singleplayer-features.md` section 6):

- **Sections with material variants.** `CMS.Garage.Customization.GarageLookManager.sections` is an array of
  `GarageLookSection { Name, RequiredUpgrade, RequiredUpgrade2, RendererData[], ProjectMaterials[], cameraPoints[],
  SelectedMaterialIndex }`. A player picks a section (category) and a variant (material) in
  `GarageCustomizationWindow`; some sections need a garage upgrade.
- **Texture packs.** `TexturePackManager` swaps garage textures for a texture pack (`ModType.TexturePack`, loaded from
  the Steam Workshop or a local folder). Packs are mods: another player may not have the same one.
- **The saved form is tiny.** `ProfileData.garageCustomizationData` is `GarageCustomizationData { int[]
  MaterialIndexes; string CurrentTexturePack }`; `GarageLoader.Save` writes it and `CustomLoad`
  (`LoaderAddition`) reads it through `GarageLookManager.Load()` and `TexturePackManager.Load()`.
- Nothing else in the garage is customisable: there are no placeable decorations in this build (no type, window or save
  field for them). The "Garage Customization DLC" entry in `SteamDLC.Init` has product id `-1` (never owned) and no code
  checks it; the window is base game.

## What Changes

- **The server owns the look.** It stores `ModGarageLook { int[] MaterialIndexes; string TexturePack; int
  SectionCount }` in the garage state (save section `garage` v1 → v2, `Migrate` fills an empty look), sends it in the
  `garage` snapshot and broadcasts every accepted change.
- **Commit on close.** When a player closes the customisation window (`GarageCustomizationWindow.Hide` postfix), the
  client reads every section's `SelectedMaterialIndex`, the section count and the current texture pack and sends
  `GarageLookUpdate` if something changed. Browsing variants inside the window stays local, as the paint shop does.
- **One player at a time.** Clicking the garage-look computer (`GameScript.ClickIO` for `#garageLook`, or the start of
  the `GameScript.ShowGarageCustomization` coroutine, whichever task 1.1's decompile shows is before the fade) asks
  the server for the look claim (`GarageLookClaim`), like the balancer lock of row 5a. On a grant the client re-runs
  `ShowGarageCustomization`; a second player is refused with "<name> is customising the garage." (D16 answer, no silent
  drop). The claim ends on close, on leaving the garage and on disconnect; it has no timer.
- **Apply per section on every client.** For each section whose stored index differs from the local
  `SelectedMaterialIndex`, the client runs the game's own per-section call `UpdateMaterials(section.RendererData, i,
  k + 1, false)` (the overload `UpdateMaterialsFromSave` uses), or the same call with `restore: true` for the default
  (-1), and writes the profile array. The per-renderer body calls `SetMaterialIndexForSection`, so the section's index
  stays right and a later `GarageLookManager.Save` does not send a stale value back. Re-running
  `GarageLookManager.Load()` is not used: `UpdateMaterialsFromSave` skips every section below 0, so a reset to default
  would never apply.
- **Late join and trips.** The mod's load runs the game's look load before it asks for the snapshots, so the `garage`
  snapshot always arrives in a loaded garage and goes through the per-section apply; after a trip the profile already
  holds the look and the apply finds nothing to change.
- **Texture packs are best effort.** The pack id is stored and sent. A known id is applied with
  `TexturePackManager.SetActiveTexturePack`/`LoadTextures`; a null or unknown id with `SetDefaultTexturePack` (the call
  the window uses for index 0), and an unknown id shows one notice ("<pack> is not installed; you see the default
  garage textures.").
- **Digest.** The `garage` digest (row 14/19) includes the last look the client applied from the server (not the live
  sections), so a missed update is found and resent while a player's live preview or a running apply never counts as a
  desync.
- **Section count.** Each update carries the sender's section count; the server stores it, and a client with a
  different count (a mod adds sections) applies the common sections and logs the difference once.
- **Guard.** `Window GarageCustomization` becomes allowed (owner row 28) in the merge commit.

Hooks: `GameScript.ClickIO` (prefix, `#garageLook` only) or `GameScript._ShowGarageCustomization_d__*.MoveNext` (first
step), per task 1.1; `GarageCustomizationWindow.Hide` (postfix: commit and release). Packets: new `GarageLookUpdate`,
`GarageLookClaim`, `GarageLookClaimResult`; `GarageState` gains `[OptionalField] ModGarageLook Look`.

## Capabilities

### New Capabilities
- `garage-look-sync`: the garage's look (material per section and texture pack) is the same for every player, survives
  a server restart and a late join, and is changed by one player at a time.

### Modified Capabilities
- None (no main spec owns the garage state yet).

## Impact

- Core: `Network/Packets/GarageLookPackets.cs` (new), `PacketTypes` (appended), `GarageState.Look`,
  `Data/GameType/ModGarageLook.cs`, `DigestMappers.Garage` (look included).
- Server: `Data/Garage/GarageLookService.cs` (store, claim, clamp, section count), `GarageSection` v2 with
  `Migrate(1→2)`, `garage` snapshot carries the look, server command `look`, a self-check for clamp and claim release.
- Client: `Logic/Garage/GarageLookSync.cs` (claim gate, commit, per-section apply, pack setters, last applied look),
  `GarageUpgrades` snapshot handler passes the look on, `Reconciliation/ClientDigests.cs` (look from the last applied
  server look), `Guard/GuardRules.cs`.
- Harness: `look-probe`, `look-set` (spike verbs from `SpFeatureProbeCommands`), new `look-open` (through the same
  click entry as the game), `look-close`, `look-pick <section> <material>`, `look-read <section>`, `look-pack <id>`;
  dump section `garageLook`; scenario `garage-look`.
- Depends on (merged): row 7 (`StateLock`, sections), row 8 (`ModNotify`), row 14/19 (`garage` digest), row 14a (guard),
  row 19 part 3 (D16 answers).

## Open questions

Each has the default the draft works with.

1. **Live preview for others.** Others see the change when the window closes, not while the player browses variants.
   **Default:** on close (no flicker for others, one packet per visit).
2. **One player at a time.** **Default:** yes, a claim on the window. Alternative: everyone may open it and the last
   close wins per section.
3. **Texture packs.** Packs are Workshop mods. **Default:** sync the pack id; a player without that pack sees the
   default textures and one notice. Alternative: leave texture packs local (not stored by the server).
4. **Required upgrades.** A section that needs a garage upgrade is offered only when the upgrade is bought; upgrades
   are shared (M1), so every player sees the same choice. The server cannot check upgrade names (no game data).
   **Default:** the client checks, the server only clamps indexes and stores the section count reported with the
   update.
5. **Reset.** The window's "reset all" restores the default materials. **Default:** it is an ordinary change, sent on
   close like any other.
