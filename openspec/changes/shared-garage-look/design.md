# Design

## Context

Static spike `docs/spikes/singleplayer-features.md` section 6; runtime spike runs `20261008-230909_L2_sp-features-probe`
and `20261008-231615_L2_sp-features-probe2`; review `review.md` (decompiles `spfeat_clean`, `spfeat2_clean`,
`placement2_clean/GameScript$$ClickIO.c`).

- `GarageLookManager.sections` has 41 sections in the base garage (spike run 1): interior floors and walls (A/B), the
  lifts (two parts), tire changer, wheel balancer, lockers, cabinets, exterior walls, gates, and the floors and walls of
  the engine room, path test, paint shop, car wash and dyno areas, plus one decal section (`Decals_trash_removed`,
  one material, 330 renderers). Each has 1–26 materials. Sections of a bought area name its upgrade
  (`garage_upgrade`, `path_test`, `paintshop`, `car_wash`, `dyno`). A fresh session profile has every
  `SelectedMaterialIndex = -1` (the default material) and an empty `MaterialIndexes`.
- The window is opened from `GameScript.ClickIO` (`#garageLook`) → `StartCoroutine(GameScript.ShowGarageCustomization())`
  (not decompiled yet); its `Hide` fades back in (`ScreenFader.NormalFadeIn/FadeOut`). In the window,
  `OnVariationChange` → `ChangeMaterial` set the manager's `currentMaterialIndex` (1-based list index; 0 = default,
  which also sets `restore`) and `ProcessRendererData` changes the material live; `SetMaterialIndexForSection` sets the
  section's `SelectedMaterialIndex`. There is no cancel: what is shown when the window closes is the state.
  `GarageLoader.Save` → `GarageLookManager.Save` copies the sections' indexes into `ProfileData.garageCustomizationData`.
- Loading: `<Load>d__44` copies the profile's `MaterialIndexes` into `dataForSave`; `<UpdateMaterialsFromSave>d__24`
  skips every section whose value is `< 0` and calls `UpdateMaterials(RendererData, i, idx + 1, false)` for the others.
  So re-running `Load()` can never restore a default section.
- `UpdateMaterials(int sectionIndex, bool restore)` (`0x180D6A470`) ignores the section's index and passes the manager's
  `currentMaterialIndex`; the renderer code loads `ProjectMaterials[materialIndex - 1]`. After `Init`,
  `currentMaterialIndex` is 0, so the spike's call indexed `[-1]` and threw `IndexOutOfRangeException` after
  `Replaced = true` was set ("flagged replaced, material unchanged"). The spike's earlier explanation (a per-section
  cache from `FillVariants`) was wrong; `cachedMaterialsList` is only a scratch list for `GetSharedMaterials`.
- `UpdateMaterials(GarageLookRenderer[], int sectionIndex, int materialIndex, bool restore)` (the overload
  `UpdateMaterialsFromSave` uses; per-renderer body `0x180D6A670`) takes the material index explicitly and calls
  `SetMaterialIndexForSection(i, k or -1)`.
- The mod's load (`LoaderAddition.CustomLoad`) runs `VanillaLoad` first: `garageLookManager.Init()`, `yield
  garageLookManager.Load()`, `TexturePackManager.Initialize()`/`Load()`; only then does it send `AskForSync`. So the
  `garage` snapshot always arrives in a garage whose look is already loaded.
- Texture packs: `TexturePackManager.Load()` with an empty `CurrentTexturePack` only sets `currentActiveTexturePack =
  default` and does not reload the default textures; with an unknown id, `GetTexturePack` fails and nothing changes.
  The window uses `SetDefaultTexturePack` (`0x1810C2390`) for index 0 and `SetActiveTexturePack` for a pack. None are
  installed in the test installs (`texturePacks = 0`, current `Default`).
- `ClientDigests` builds the `garage` digest from live game state; `GarageSection` is v1 with no migration.

## Goals / Non-Goals

**Goals:** one look for everyone, kept by the server, late join and restart; one editor at a time; the texture pack id
shared with a graceful fallback.

**Non-Goals:** live preview for others while browsing (open question 1); distributing texture pack files.

## Decisions

### D1. Server state

`ModGarageLook { int[] MaterialIndexes; string TexturePack; int SectionCount }` in `GarageState` (`[OptionalField]`),
saved in the `garage` section v2; `Migrate(1→2)` adds an empty look (no indexes, pack `null`, count 0). The server clamps
each index to `-1..63` and the array to 64 entries, and stores the sender's `SectionCount`; it does not know the section
names (no game data). Every accepted `GarageLookUpdate` from the claim holder replaces the whole look and is broadcast
to all in-session clients (sender included, so its own echo confirms the store). Runtime only: the claim.

### D2. Claim and commit

- Gate (task 1.1 picks the point after decompiling `ShowGarageCustomization`): a `GameScript.ClickIO` prefix for
  `#garageLook`, or the first step of the coroutine's `MoveNext`, before any fade. Connected and not holding the claim:
  send `GarageLookClaim` and skip; `GarageLookClaimResult { Granted, HolderPlayerId }` re-runs
  `StartCoroutine(GameScript.ShowGarageCustomization())` when granted, or shows "<name> is customising the garage."
  when refused. No answer within 5 s → "No answer from the server." and nothing opens. The guard keeps hooking
  `WindowManager.Show` as before.
- Release on `Hide`, on leaving the garage scene and on disconnect (`PresenceEvents`). No expiry timer (nothing would
  renew it).
- `GarageCustomizationWindow.Hide` postfix: read `sections[i].SelectedMaterialIndex` for all sections, the section
  count and the current pack id (`null` for the default); send `GarageLookUpdate` when it differs from the last applied
  look; release the claim.

### D3. Apply

`GarageLookSync.Apply(look)` (a coroutine, one section per frame like the game's own load):
- for each section `i` below `min(look.SectionCount, sections.Length)` whose stored index `k` differs from
  `sections[i].SelectedMaterialIndex`: `k ≥ 0` → `UpdateMaterials(sections[i].RendererData, i, k + 1, false)`;
  `k = -1` → the same call with `restore: true`;
- write `garageCustomizationData.MaterialIndexes` and `CurrentTexturePack` in the session profile, so a later scene load
  and `GarageLookManager.Save` agree;
- texture pack (D5);
- remember the look as `LastApplied` when done.
Incoming looks are never applied while the local player holds the claim (the server accepts updates only from the
holder, so none arrive). Task 1.1 measures the apply time for all 41 sections, including the 330-renderer decal section
and the per-renderer coroutine `<UpdateMaterials>d__21`.

### D4. Late join and trips

The `garage` snapshot handler (`GarageUpgrades`) passes the look to D3's apply and does not wait for it (spike
results below). This is the main path: a joiner's
garage has already loaded the empty session profile's look, and the apply changes the differing sections. After a trip,
the garage loads from the session profile, which already holds the look, so the apply finds nothing to change.

### D5. Texture pack

A known pack id (in `TexturePackManager.GetTexturePacks()`) → `SetActiveTexturePack(pack)` and `LoadTextures`. A null id
→ `SetDefaultTexturePack()`. An unknown id → `SetDefaultTexturePack()`, a log line and one notice per session ("<pack
name or id> is not installed; you see the default garage textures."). The server keeps the id.

### D6. Digest

`DigestMappers.Garage` appends the look (indexes, pack id). The client side builds it from `LastApplied`, not from the
live sections, so the claim holder's preview and a running apply never differ from the server; a client that missed an
update still differs and gets the `garage` key resent (row 19 part 2).

### D7. Section count

A client whose `sections.Length` differs from the stored `SectionCount` applies the common sections and logs the
difference once (`garageLook.sectionCountMismatch` in the dump).

## Risks / Trade-offs

- [`ShowGarageCustomization` fades out before any hookable point] → task 1.1 decompiles it; the `ClickIO` prefix is
  before the coroutine starts.
- [The per-section call has a side effect the load path avoids] → task 1.1 applies, resets and re-applies sections in a
  loaded garage and reads the renderers back.
- [The apply takes longer than the scenario's bound] → task 1.1 measures it; the scenario uses the measured bound.

## Spike results (task 1.1, 2026-10-09)

Decompile `garagelook_clean`, `garagelook2_clean`, `garagelook3_clean`; run `20261009-212145_L1_garage-look-probe`
(headless test installs).

- **D2 confirmed, gate in `GameScript.ClickIO`.** `<ShowGarageCustomization>d__73` disables input
  (`InputManager.ChangeInput(0, …)`) and starts `ScreenFader.NormalFadeIn` in its first step, before
  `WindowManager.Show(42)`; `ClickIO` is the only caller of `ShowGarageCustomization`. The prefix checks
  `IOMouseOverType == "#garageLook"`. In the run, A's click asked for the claim, the window opened on the grant (faded
  during the fade, not faded 2 s later, mode `UI`); B's click was refused ("Player1 is customising the garage.") with
  no fade and mode `Garage`.
- **D3 confirmed.** The 4-argument `UpdateMaterials` is a coroutine of nested coroutines (`d__21` → `d__23` per
  renderer → `<ProcessRendererData>d__26`, which loads the material with `Resources.LoadAsync` and waits a frame
  until it is done). The client steps it with its nested coroutines itself (`RunNative`), one apply at a time, as the
  renderers share the manager's `cachedMaterialsList` across those frames. Section 2 → 3 and section 0 → 5 change the
  renderers' materials, section 2 back to -1 with `restore: true` shows the original material again, and
  `SelectedMaterialIndex` follows each time; `GarageLookManager.Save` then writes the same indexes into the profile
  (the client calls it, and `TexturePackManager.Save`, after every apply instead of writing the profile itself).
- **Times:** 2 sections 0.42 s, one restore 0.07 s, all 41 sections to material 0 28.6 s (the decal section has 330
  renderers, one async load each), all 41 back to default 2.7 s (restore is synchronous). So the `garage` snapshot
  does not wait for the apply (D4 changed): a joiner with a heavily customised garage would otherwise pass the 30 s
  no-progress timeout of the initial sync. While an apply runs the client's `garage` digest is "not ready" and a
  `#garageLook` click shows "The garage look is still being updated." The scenario bound is 10 s (two sections).
- **D5:** `SetActiveTexturePack("not-installed")` and `SetDefaultTexturePack()` throw nothing; no pack is installed in
  the test installs and the default pack's id is `Default` (the client treats it as null). `SetActiveTexturePack`
  starts `LoadTextures` itself.
- An unknown pack id from the server is kept by a player who does not have it: the window close sends the pack it had
  when the window opened unless the player picked another one there, so a player without the pack does not reset it
  for everyone by closing the window.

## Migration Plan

`garage` section v1 → v2 with `Migrate` filling an empty look; additive packet field; new packets appended. Rollback:
revert; the stored look is ignored by older servers (a v2 section is refused by a v1 server, as for every section bump).
