# Tasks

Prerequisites (all merged): rows 7, 8, 14, 14a, 19 parts 2 and 3.

## 1. Spike

- [x] 1.1 Decompile `GameScript.ShowGarageCustomization` and its `MoveNext`; pick D2's gate point (before any fade).
      One client: apply sections through D3's per-section call (section 2 → material 3, section 0 → 5), read the
      renderer materials (`look-read`), set section 2 back to -1 with `restore: true` and read again; check that
      `SelectedMaterialIndex` follows and that a `GarageLoader.Save` afterwards writes the same indexes. Apply all 41
      sections and time it (decal section included). Try `SetActiveTexturePack`/`SetDefaultTexturePack` (no pack
      installed: default only). Correct the spike note in `docs/spikes/singleplayer-features.md` section 6 (the
      `IndexOutOfRangeException` came from `currentMaterialIndex = 0`, not from a material cache). Done when D2 and D3
      are confirmed or changed.
      **Done 2026-10-09** (`20261009-212145_L1_garage-look-probe`, design "Spike results"): D2 confirmed (gate in the
      `ClickIO` prefix; the coroutine fades in its first step), D3 confirmed (the profile is written by
      `GarageLookManager.Save`, which the probe's `look-save` calls); D4 changed: the snapshot does not wait for the
      apply (all 41 sections took 28.6 s).

## 2. Core and server

- [x] 2.1 `ModGarageLook` (with `SectionCount`), `GarageState.Look`, packets `GarageLookUpdate`, `GarageLookClaim`,
      `GarageLookClaimResult` (appended); `DigestMappers.Garage` includes the look.
- [x] 2.2 `GarageLookService`: store, clamp, section count, claim with release on leave and disconnect, broadcast;
      `GarageSection` v2 with `Migrate(1→2)`; server command `look` (indexes, pack, count, claim holder). Verify: a
      server self-check for clamp, migration and claim release. **`--check-garage-look` passes; `--check-merges`
      (look in the digest) and `--check-digest` pass; `Test-ServerSaves` migrates the v1 fixture's garage to v2.**

## 3. Client and harness

- [x] 3.1 `Logic/Garage/GarageLookSync.cs`: claim gate (D2), commit on `Hide`, per-section apply (D3), snapshot (D4),
      pack setters and notice (D5), `LastApplied` digest source (D6), section count (D7).
- [x] 3.2 Guard: `Window GarageCustomization` allowed (owner row 28).
- [x] 3.3 Harness `look-open` (through the same click entry as the game, so the gate runs), `look-pick <section>
      <material>` (the window's own `OnCategoryChange`/`OnVariationChange`), `look-close`, `look-read <section>`,
      `look-pack <id>`; dump section `garageLook` (indexes, pack, count, claim, last applied, per changed section the
      renderer's material name). **The dump lists the first renderer's material of every section.**

## 4. Proof

- [x] 4.1 Scenario `garage-look` (two clients): A `look-open`, picks wall A material 3 and floor A material 5,
      `look-close` → B's `garageLook` indexes and renderer materials equal A's within the bound measured in 1.1; B
      `look-open` while A has it open → refused with "A is customising the garage.", B's dump unchanged and B's screen
      not faded; B joins again → same look after `syncAcked`; server restart → same look; A sets wall A back to default
      → default material on both, and after B's next `look-open`/`look-close` without changes the server still has -1;
      A sets a texture pack id unknown to B (`look-pack fake`) → B keeps default textures and shows the notice once; no
      `garage` digest mismatch is counted while A has the window open. Old-code failure: run with `guard-allow
      Window:GarageCustomization`; B's indexes stay -1. Verify: `Run-Session.ps1 -Scenario garage-look` passes;
      `Run-All -Changed` (areas `garage`, `guard`, smoke).
      **Fails on the old code (`20261009-212616_L1_garage-look`, in a worktree of `main` with only the harness and
      the scenario: B opens the window too, B's indexes stay -1, nothing reaches the server) and passes
      (`20261009-213011_L1_garage-look`, 53 checks; the look reaches B in 0.4–0.7 s). "Same look after `syncAcked`"
      became "within the bound after the rejoin" (D4 change).**

## 5. Docs

- [x] 5.1 ROADMAP row 28 status (and the backlog line "garage decorations/customization" removed); STATUS entry; docs/try-it.md.
      **ROADMAP has no such backlog line; README "Playing together" and "Not shared yet" updated as well.**
