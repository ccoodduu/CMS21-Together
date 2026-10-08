# Review: shared-garage-look (row 28)

**Verdict: ready after fixes.** The server-owned look, commit on close and one-editor claim are sound. The client
apply (D3) and the late-join path (D4) are built on claims that the decompiles and the mod's own load flow contradict.

Checked against main `3645acc`: `LoaderAddition.cs` (`CustomPrepareGame`, `VanillaLoad`, `CustomLoad`), `GuardRules.cs`,
`GuardHooks.cs`, `ClientDigests.cs`, `ReconciliationService.cs`, `GarageState` and `GarageSection`, and the decompiles in
`native/out/spfeat_clean` and `spfeat2_clean`: `GarageLookManager` `Init`, `<Load>d__44`, `<UpdateMaterialsFromSave>d__24`,
the four `UpdateMaterials` overloads, `SetMaterialIndexForSection`, `Save`, `PrepareSave`, `RestoreAllSections`,
`TexturePackManager.Load`, and `GarageCustomizationWindow` `Show`/`Hide`/`ChangeMaterial`/`OnVariationChange`. Also
`placement2_clean/GameScript$$ClickIO.c` (`#garageLook`).

## Blockers

**B1. Re-running `GarageLookManager.Load()` cannot apply a default (-1) section, and it leaves stale indexes that
the next commit sends back.**
- `<Load>d__44` copies the profile's `MaterialIndexes` into `dataForSave`.
- `<UpdateMaterialsFromSave>d__24` then skips every section whose value is `< 0`
  (`while (dataForSave[i] < 0)` continues). It only calls `UpdateMaterials(RendererData, i, idx + 1, false)` for
  indexes ≥ 0.

So when A sets wall A back to default, B's apply does nothing for that section:
- B keeps A's old material, and B's `sections[i].SelectedMaterialIndex` keeps the old value.
- `GarageLookManager.Save` (run by every `GarageLoader.Save`, e.g. in each job end) copies the stale
  `SelectedMaterialIndex` back into the profile.
- B's next window close (D2 reads `SelectedMaterialIndex`) sends the old index and reverts A's reset for everyone.

Scenario step "A sets wall A back to default → default material on both" fails on the drafted design.

Fix: apply per section, the way the game's own code does it. For each section whose stored index differs from the
local `SelectedMaterialIndex`:
- index k ≥ 0: run `UpdateMaterials(section.RendererData, i, k + 1, false)`, the overload `UpdateMaterialsFromSave`
  uses;
- index -1: run the same call with `restore: true`.

The per-renderer body (`UpdateMaterials(GarageLookRenderer, int, int, bool)` at `0x180D6A670`) calls
`SetMaterialIndexForSection(i, k or -1)`, so `SelectedMaterialIndex` stays right. Also write the profile array, so a
later scene load agrees.

**B2. The spike's reason for rejecting a per-section apply does not hold.** The proposal and design say
`SetMaterialIndexForSection` + `UpdateMaterials` throws "because the window builds a per-section material cache first
(`FillVariants`)". The decompiles show a different cause:
- `UpdateMaterials(int sectionIndex, bool restore)` (`0x180D6A470`) ignores the section's index. It passes the
  manager's `currentMaterialIndex` to the renderer code.
- That value is 1-based (`ChangeMaterial`/`OnVariationChange` set it to the list index; 0 = default, which also sets
  `restore`).
- The renderer code loads `ProjectMaterials[materialIndex - 1]`.
- After `Init`, `currentMaterialIndex` is 0. The spike's call therefore indexed `[-1]`, which threw
  `IndexOutOfRangeException` after `Replaced = true` was set. That matches the spike's "flagged replaced, material
  unchanged".
- `cachedMaterialsList` is only a scratch list for `GetSharedMaterials`.

Fix: correct the spike note and D3, and make task 1.1 prove B1's per-section apply. Today 1.1 proves "Load twice",
which the decompile already rules out for defaults.

## Major

**M1. D4 has the order the wrong way round: the snapshot always comes after the look is loaded.**
- In the mod's load, `CustomLoad` first runs `VanillaLoad`. That does `garageLookManager.Init()`, then
  `yield StartCoroutine(garageLookManager.Load())`, then `TexturePackManager.Initialize()`.
- Only after that does it send `AskForSync` and wait for the snapshots.
- So the `garage` snapshot never arrives "before the garage finishes loading". On a first join (fresh, empty session
  profile) and after every trip, the look must be applied to a loaded garage.

That is the B1 path, and it is the main path, not a fallback. On trip returns the profile already holds the last look,
and the apply is a no-op when nothing changed. Fix D4 to say this, and make the snapshot handler call the B1 apply.

**M2. Gate the window at the click, not at `GarageCustomizationWindow.Show`.**
- The window is opened from `GameScript.ClickIO` (`#garageLook`) → `StartCoroutine(GameScript.ShowGarageCustomization())`.
  That coroutine is not decompiled, and the window's own `Hide` fades back in (`ScreenFader.NormalFadeIn/FadeOut`).
- If the coroutine fades out before `WindowManager.Show(42)` and the `Show` prefix returns false, the screen can stay
  black (no `Hide` runs).
- A `Show` prefix that answers false also sits under `WindowManager.Show`, which has already run its own bookkeeping.
  The guard hooks that level (`GuardHooks`).

Fix: task 1.1 decompiles `ShowGarageCustomization` / its `MoveNext`. Then gate the claim either in a `ClickIO` prefix
for `#garageLook` or at the start of that coroutine. On a grant, re-run `ShowGarageCustomization`. The harness
`look-open` must use the same entry, or the scenario never exercises the gate.

**M3. Texture pack apply needs the explicit setters.**
- `TexturePackManager.Load()` with an empty `CurrentTexturePack` only sets `currentActiveTexturePack = default`. It does
  not reload the default textures (the window uses a separate call for index 0, `0x1810C2390`, which looks like
  `SetDefaultTexturePack`).
- With an unknown id, `GetTexturePack` fails and nothing changes, so a client keeps whatever pack it had.

So "back to default" and D5's "keeps the default textures" both fail through `Load()`. Fix: call
`SetDefaultTexturePack()` for a null or unknown id, and `SetActiveTexturePack`/`LoadTextures` for a known one. The
spike lists `SetActiveTexturePack` and `SetDefaultTexturePack` in `spfeat2_clean`.

**M4. The look digest would report a desync while someone is customising.** `ClientDigests` builds the `garage`
digest from live game state. With the look added, the claim holder's live preview (and every client during the
one-section-per-frame apply coroutine) differs from the server. That counts as a mismatch, and row 19 resends the
`garage` key. Fix: build the client's look digest from the last applied server look (not from `sections`), or leave
the look out of the digest while the window is open or an apply is running.

## Minor

- m1. Claim expiry "after 10 minutes without a renew": nothing renews. Either renew from the open window every
  N seconds, or drop the expiry and release only on `Hide`, leaving the scene, or disconnect.
- m2. Timing: the decal section `Decals_trash_removed` has 330 renderers, and the apply runs one section per frame
  plus the per-renderer coroutine `<UpdateMaterials>d__21` (not decompiled). Measure the apply time in 1.1 before fixing
  the scenario's "within 2 s".
- m3. `GarageState` is a packet class (`INetworkData`); the `garage` save section is `GarageSection` v1 with no
  migration. The version bump needs `Migrate(1→2)` filling an empty look. Name it in 2.2.
- m4. Positional indexes: the server can't validate them. Send the section count with each update and store it, so
  a client with a different count (a mod) can be detected instead of only logged.
- m5. Fails-on-old-code: "guard on Enforce refuses the window" fails for the guard, not for sync. The meaningful old-code
  failure is with `guard-allow`: B's indexes stay -1. Run the old-code proof with `guard-allow`.

## Size

S (≈ 1–2) is too small for a new server service with a claim, three packets, a save-section migration, a new apply
path, a window gate that needs a decompile, texture-pack setters, a digest rule and a restart scenario. Plan for M
(≈ 3).
