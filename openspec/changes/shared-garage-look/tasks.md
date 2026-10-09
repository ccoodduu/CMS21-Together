# Tasks

Prerequisites (all merged): rows 7, 8, 14, 14a, 19 parts 2 and 3.

## 1. Spike

- [ ] 1.1 Decompile `GameScript.ShowGarageCustomization` and its `MoveNext`; pick D2's gate point (before any fade).
      One client: apply sections through D3's per-section call (section 2 → material 3, section 0 → 5), read the
      renderer materials (`look-read`), set section 2 back to -1 with `restore: true` and read again; check that
      `SelectedMaterialIndex` follows and that a `GarageLoader.Save` afterwards writes the same indexes. Apply all 41
      sections and time it (decal section included). Try `SetActiveTexturePack`/`SetDefaultTexturePack` (no pack
      installed: default only). Correct the spike note in `docs/spikes/singleplayer-features.md` section 6 (the
      `IndexOutOfRangeException` came from `currentMaterialIndex = 0`, not from a material cache). Done when D2 and D3
      are confirmed or changed.

## 2. Core and server

- [ ] 2.1 `ModGarageLook` (with `SectionCount`), `GarageState.Look`, packets `GarageLookUpdate`, `GarageLookClaim`,
      `GarageLookClaimResult` (appended); `DigestMappers.Garage` includes the look.
- [ ] 2.2 `GarageLookService`: store, clamp, section count, claim with release on leave and disconnect, broadcast;
      `GarageSection` v2 with `Migrate(1→2)`; server command `look` (indexes, pack, count, claim holder). Verify: a
      server self-check for clamp, migration and claim release.

## 3. Client and harness

- [ ] 3.1 `Logic/Garage/GarageLookSync.cs`: claim gate (D2), commit on `Hide`, per-section apply (D3), snapshot (D4),
      pack setters and notice (D5), `LastApplied` digest source (D6), section count (D7).
- [ ] 3.2 Guard: `Window GarageCustomization` allowed (owner row 28).
- [ ] 3.3 Harness `look-open` (through the same click entry as the game, so the gate runs), `look-pick <section>
      <material>` (the window's own `OnCategoryChange`/`OnVariationChange`), `look-close`, `look-read <section>`,
      `look-pack <id>`; dump section `garageLook` (indexes, pack, count, claim, last applied, per changed section the
      renderer's material name).

## 4. Proof

- [ ] 4.1 Scenario `garage-look` (two clients): A `look-open`, picks wall A material 3 and floor A material 5,
      `look-close` → B's `garageLook` indexes and renderer materials equal A's within the bound measured in 1.1; B
      `look-open` while A has it open → refused with "A is customising the garage.", B's dump unchanged and B's screen
      not faded; B joins again → same look after `syncAcked`; server restart → same look; A sets wall A back to default
      → default material on both, and after B's next `look-open`/`look-close` without changes the server still has -1;
      A sets a texture pack id unknown to B (`look-pack fake`) → B keeps default textures and shows the notice once; no
      `garage` digest mismatch is counted while A has the window open. Old-code failure: run with `guard-allow
      Window:GarageCustomization`; B's indexes stay -1. Verify: `Run-Session.ps1 -Scenario garage-look` passes;
      `Run-All -Changed` (areas `garage`, `guard`, smoke).

## 5. Docs

- [ ] 5.1 ROADMAP row 28 status (and the backlog line "garage decorations/customization" removed); STATUS entry; docs/try-it.md.
