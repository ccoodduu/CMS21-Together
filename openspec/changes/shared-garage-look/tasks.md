# Tasks

Prerequisites (all merged): rows 7, 8, 14, 14a, 19 parts 2 and 3.

## 1. Spike

- [ ] 1.1 One client: write `MaterialIndexes` into the session profile (e.g. section 2 → 3, section 0 → 5) and run
      `GarageLookManager.Load()` a second time in the loaded garage; read the renderer materials (`look-read`); set
      section 2 back to `-1` and load again. Also open and close the window from the harness (`look-open`,
      `look-pick`, `look-close`) and log `Show`, `Hide`, `SetMaterialIndexForSection`. Done when design.md D3 names the
      apply that works.

## 2. Core and server

- [ ] 2.1 `ModGarageLook`, `GarageState.Look`, packets `GarageLookUpdate`, `GarageLookClaim`, `GarageLookClaimResult`
      (appended); `DigestMappers.Garage` includes the look.
- [ ] 2.2 `GarageLookService`: store, clamp, claim with release on leave and disconnect, broadcast; `garage` section
      version bump; server command `look` (indexes, pack, claim holder). Verify: server `--check-merges`-style self check
      for clamp and claim release.

## 3. Client and harness

- [ ] 3.1 `Logic/Garage/GarageLookSync.cs`: claim gate on `Show`, commit on `Hide` (D2), apply (D3), snapshot (D4),
      pack fallback (D5).
- [ ] 3.2 Guard: `Window GarageCustomization` allowed (owner row 28).
- [ ] 3.3 Harness `look-open`, `look-pick <section> <material>` (the window's own `OnCategoryChange`/`OnVariationChange`),
      `look-close`, `look-read <section>`; dump section `garageLook` (indexes, pack, claim, per changed section the
      renderer's material name).

## 4. Proof

- [ ] 4.1 Scenario `garage-look` (two clients): A `look-open`, picks wall A material 3 and floor A material 5,
      `look-close` → B's `garageLook` indexes and renderer materials equal A's within 2 s; B `look-open` while A has it
      open → refused with "A is customising the garage.", B's dump unchanged; B joins again → same look after
      `syncAcked`; server restart → same look; A sets wall A back to default → default material on both; a texture pack
      id unknown to B (`look-pack fake`) → B keeps default textures and shows the notice once. Fails on the old code
      (guard on Enforce refuses the window; with `guard-allow` B never changes). Verify: `Run-Session.ps1 -Scenario
      garage-look` passes; `Run-All -Changed` (areas `garage`, `guard`, smoke).

## 5. Docs

- [ ] 5.1 ROADMAP row 28 status (and the backlog line "garage decorations/customization" removed); STATUS entry; docs/try-it.md.
