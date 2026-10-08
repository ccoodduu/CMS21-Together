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

- **The server owns the look.** It stores `ModGarageLook { int[] MaterialIndexes; string TexturePack }` in the garage
  state (save section `garage`, version bump with an empty default), sends it in the `garage` snapshot and broadcasts
  every accepted change.
- **Commit on close.** When a player closes the customisation window (`GarageCustomizationWindow.Hide` postfix), the
  client reads every section's `SelectedMaterialIndex` and the current texture pack and sends `GarageLookUpdate` if
  something changed. Browsing variants inside the window stays local, as the paint shop does.
- **One player at a time.** Opening the window asks the server for the look claim (`GarageLookClaim`), like the
  balancer lock of row 5a. A second player is refused with "<name> is customising the garage." (D16 answer, no silent
  drop). The claim ends on close, on leaving the garage and on disconnect.
- **Apply on every client.** Receivers write the look into the session profile's `garageCustomizationData` and run
  the game's own load (`GarageLookManager.Load()`, `TexturePackManager.Load()`), the path `CustomLoad` uses after every
  scene load. Setting one section from outside the window (`SetMaterialIndexForSection` + `UpdateMaterials`) throws
  in the spike runs, because the window builds a per-section material cache first. A late joiner gets the look in the
  `garage` snapshot before its garage finishes loading.
- **Texture packs are best effort.** The pack id is stored and sent; a client that does not have that pack keeps the
  default textures and shows one notice ("<pack> is not installed; you see the default garage textures.").
- **Digest.** The `garage` digest (row 14/19) includes the look, so a missed update is found and resent.
- **Guard.** `Window GarageCustomization` becomes allowed (owner row 28) in the merge commit.

Hooks: `GarageCustomizationWindow.Show` (prefix: claim; refused → `__result = false`), `GarageCustomizationWindow.Hide`
(postfix: commit and release), `GarageLookManager.Load` (only to skip a stale profile apply while a snapshot is
pending; see design D4). Packets: new `GarageLookUpdate`, `GarageLookClaim`, `GarageLookClaimResult`; `GarageState`
gains `[OptionalField] ModGarageLook Look`.

## Capabilities

### New Capabilities
- `garage-look-sync`: the garage's look (material per section and texture pack) is the same for every player, survives
  a server restart and a late join, and is changed by one player at a time.

### Modified Capabilities
- None (no main spec owns the garage state yet).

## Impact

- Core: `Network/Packets/GarageLookPackets.cs` (new), `PacketTypes` (appended), `GarageState.Look`,
  `Data/GameType/ModGarageLook.cs`, `DigestMappers.Garage` (look included).
- Server: `Data/Garage/GarageLookService.cs` (store, claim, clamp), garage save section version bump, `garage`
  snapshot carries the look, server command `look`.
- Client: `Logic/Garage/GarageLookSync.cs` (hooks, apply, claim), `GarageUpgrades` snapshot handler passes the look on,
  `Guard/GuardRules.cs`.
- Harness: `look-probe`, `look-set` (spike verbs from `SpFeatureProbeCommands`), new `look-open`, `look-close`,
  `look-pick <section> <material>`; dump section `garageLook`; scenario `garage-look`.
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
   **Default:** the client checks, the server only clamps indexes to the section count reported with the update.
5. **Reset.** The window's "reset all" restores the default materials. **Default:** it is an ordinary change, sent on
   close like any other.
