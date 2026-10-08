# Design

## Context

Static spike `docs/spikes/singleplayer-features.md` section 6; runtime spike runs `20261008-230909_L2_sp-features-probe`
and `20261008-231615_L2_sp-features-probe2`.

- `GarageLookManager.sections` has 41 sections in the base garage (spike run 1): interior floors and walls (A/B), the
  lifts (two parts), tire changer, wheel balancer, lockers, cabinets, exterior walls, gates, and the floors and walls of
  the engine room, path test, paint shop, car wash and dyno areas, plus one decal section (`Decals_trash_removed`,
  one material, 330 renderers). Each has 1–26 materials. Sections of a bought area name its upgrade
  (`garage_upgrade`, `path_test`, `paintshop`, `car_wash`, `dyno`). A fresh session profile has every
  `SelectedMaterialIndex = -1` (the default material) and an empty `MaterialIndexes`.
- The window (`GarageCustomizationWindow`) moves the camera to the section's camera points, changes the material live
  (`OnVariationChange` → `ChangeMaterial` → `ProcessRendererData`) and sets the section's `SelectedMaterialIndex`
  (`SetMaterialIndexForSection`). There is no cancel: what is shown when the window closes is the state.
  `GarageLoader.Save` → `GarageLookManager.Save` copies the indexes into `ProfileData.garageCustomizationData`.
- Loading: `LoaderAddition.CustomLoad` runs `garageLookManager.Init()` and `StartCoroutine(garageLookManager.Load())`,
  which reads `garageCustomizationData.MaterialIndexes` into the sections and runs `UpdateMaterialsFromSave`; then
  `TexturePackManager.Initialize()` and `Load()` (reads `CurrentTexturePack`).
- Applying one section from outside the window does not work: `SetMaterialIndexForSection` then
  `UpdateMaterials(section, restore)` throws `IndexOutOfRangeException` (both runs; the window fills a per-section
  material cache first, `FillVariants`). The renderer is marked replaced but keeps its material. The load path
  (profile indexes, then `Load()`) is the one the game itself uses after a scene load.
- Texture packs: `TexturePackManager` lists packs found in the Workshop folder and a local folder; none in the test
  installs (`texturePacks = 0`, current `Default`).

## Goals / Non-Goals

**Goals:** one look for everyone, kept by the server, late join and restart; one editor at a time; the texture pack id
shared with a graceful fallback.

**Non-Goals:** live preview for others while browsing (open question 1); distributing texture pack files.

## Decisions

### D1. Server state

`ModGarageLook { int[] MaterialIndexes; string TexturePack }` in `GarageState` (`[OptionalField]`), saved in the
`garage` section (version bump; old saves: all `-1`, pack `null`). The server clamps each index to `-1..63` and the array
to 64 entries; it does not know the section names (no game data). Every accepted `GarageLookUpdate` replaces the whole
look and is broadcast to all in-session clients (sender included, so its own echo confirms the store).

### D2. Commit and claim

- `GarageCustomizationWindow.Show` prefix (connected): if this client holds the look claim, continue; else send
  `GarageLookClaim` and return `__result = false`; the answer `GarageLookClaimResult { Granted, HolderPlayerId }`
  re-opens the window (granted) or shows "<name> is customising the garage." (refused). The claim is released on
  `Hide`, on leaving the garage scene and on disconnect (`PresenceEvents`), and expires after 10 minutes without a
  renew.
- `GarageCustomizationWindow.Hide` postfix: read `sections[i].SelectedMaterialIndex` for all sections and
  `TexturePackManager.GetCurrentTexturePack().ID` (`null` for the default); send `GarageLookUpdate` when it differs from
  the last known look; release the claim.

### D3. Apply

Receivers (and the snapshot apply) write the indexes into the session profile's
`garageCustomizationData.MaterialIndexes` and `CurrentTexturePack`, then run the game's own load:
`StartCoroutine(GarageLookManager.Instance.Load())` and, if the pack changed, `TexturePackManager.Load()`. While the local
player has the window open (only the claim holder can), incoming looks are not applied (none can arrive: the server only
accepts updates from the holder). Task 1.1 confirms that `Load()` can run a second time in a loaded garage and that a
section set back to `-1` restores the default material (`UpdateMaterials(restore: true)`); if `Load()` cannot run twice,
the apply fills the window's material cache for each changed section the way `FillVariants` does and calls
`ChangeMaterial`.

### D4. Late join and trips

The `garage` snapshot (order 10) arrives before the garage finishes loading for a joiner; the client writes the look
into the session profile at once, so `CustomLoad`'s own `garageLookManager.Load()` shows it without a second pass. If the
snapshot comes after `CustomLoad` passed that point, D3's apply runs. A return from a trip loads the garage from the
session profile, which already holds the look.

### D5. Texture pack fallback

If the stored pack id is not in `TexturePackManager.GetTexturePacks()`, the client keeps the default textures, logs it,
and shows one notice per session ("<pack name or id> is not installed; you see the default garage textures."). The
server keeps the id.

### D6. Digest

`DigestMappers.Garage` appends the look (indexes and pack id) so a client that missed an update is found by the `garage`
digest and gets a resend (row 19 part 2 resends the `garage` key).

## Risks / Trade-offs

- [`Load()` restarts coroutines the window or the culler depend on] → task 1.1; fallback in D3.
- [A section count that differs between clients (a mod adds sections)] → indexes are positional; extra entries are
  ignored, missing ones stay default; a mismatch is logged once.
- [The window's camera fade leaves the player stuck if the claim answer is lost] → the window only opens after the
  answer; a missing answer within 5 s shows "No answer from the server." and nothing opens.

## Migration Plan

`garage` section version bump with a default; additive packet field; new packets appended. Rollback: revert; the stored
look is ignored by older servers.
