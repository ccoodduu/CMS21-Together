# Spike: fluid tool visuals for remote players

Playtest 3 (2026-10-09/10): "There was no animation for my friend when the oil was drained." Static half from the
decompiles (`native\out\cardetails_clean`, new `native\out\fluids_clean` from `work\targets\fluids.txt`) and
`dump.cs`; the runtime facts are at the end. Date: 2026-10-10. Design: `docs/design/remote-fluid-visuals.md`.

## What a receiver sees today

- **Oil bin drain:** the actor sends `ToolAction(OilBin, DrainOil)` at the first step of `<UseOilDrain>d__40`
  (`OilBinHooks`); the receiver only counts it (`CarToolActions.OnRemote` has no case for it). The actor's
  `PlayerActivity` is `CarTool`/`OilBin` on the loader from that step to the last one (`CarToolActions.LocalWork`), so
  the avatar turns to the car. The oil level 0 arrives through row 4's `Fluids` after the end.
- **Refill, extractor:** `PlayerActivity` `Fluid` with the `ToolType` and the car under the mouse; the avatar holds a
  ghost of the receiver's own tool (`ToolProps`). Nothing pours; the level arrives through row 4.

## Oil bin drain: `CarLoader.UseOilbin` → `ToolsManager.<UseOilDrain>d__40`

`UseOilbin` (`0x18050B450`) starts the coroutine on the `CarLoader`. `MoveNext` (`0x180B84840`):

Step 0:
1. Oil level (`FluidsData.GetLevel(EngineOil, 0)`) 0 → `return false`. No `e_engine_h` → `false`.
2. `korek = e_engine_h.transform.Find("korek_spustowy_1(0)")` (the drain plug part); missing or inactive → `false`.
3. `korek.gameObject.SetActive(false)`: the plug disappears for the whole drain.
4. `ToolsManager.Oil_drain_h` (one shared GameObject, `+0xB8`) is moved to the plug: `position = korek.position`,
   `rotation = LookRotation(-korek.forward)`, then `SetActive(true)`.
5. `ps = Oil_drain_h.GetComponentInChildren<ParticleSystem>()`; `ps.emission.enabled = true`; `ps.Play()`.
6. `SoundManager.PlayLoopSFX(Oil_drain_h, "OilDrain", 0.35, 2.55)` (loop between 0.35 s and 2.55 s of the clip).
7. `ToolsMoveManager.Oilbin` (`+0x48`)'s `InteractiveObject.on = false`; `carLoader.EnableIO(false)`.
8. `ps.main.startColor = Color.Lerp(black, white, oilCondition)` with alpha 0.75 (dirty oil is dark).
9. `yield WaitForSeconds(oilLevel * 5)`: at most 5 s (the level is clamped to 0..1).

Step 1: `ps.Stop()`; `SoundManager.StopLoopSFX(Oil_drain_h, playEnd: true)` (jumps to the clip's tail).
Step 1/2: while `ps.IsAlive(true)` → `yield WaitForEndOfFrame` (the stream finishes falling).
Then: `emission.enabled = false`; oil bin `InteractiveObject.on = true`; `FluidsData.SetCondition(0)` and
`SetLevel(0)` for the oil; `carLoader.EnableIO(true)`; `korek.SetActive(true)`; `OnOilDrainFinished?.Invoke()`.
`Oil_drain_h` itself stays active (only its emission is off).

No animator, no tween, no level drop over time: the level jumps to 0 at the end. The oil bin object does not move in
the coroutine (it is placed under the car earlier by `ToolsMoveManager.MoveTo`, which row 5b already syncs).

### Side effects per call (for a receiver)

| Call | State it writes | On a receiver |
|---|---|---|
| `korek.SetActive(false/true)` | the plug part's active flag (the coroutine and `HideBySavegame` read it) | **no**; hide its renderers with `forceRenderingOff` (row 17 D1) |
| `Oil_drain_h` move, `SetActive`, emission, `Play`, start colour | the shared tool object (the receiver's own drain uses it) | **no**; use a copy |
| `SoundManager.PlayLoopSFX(go, …)` / `StopLoopSFX(go, …)` | `loopedAudio` entry keyed by `go`; `AudioSource` on `go` (added when missing) | yes, on the copy |
| `InteractiveObject.on`, `EnableIO` | interaction state | **no** |
| `FluidsData.SetCondition/SetLevel` | the oil | **no** (row 4 brings it) |
| `OnOilDrainFinished` | listeners (tutorial) | **no** |

`SoundManager.PlayLoopSFX` (`0x180D8EAE0`): returns at once when `GameSettings.AudioSettingsData.GameVolume == 0`;
otherwise finds the SFX, uses `go`'s `AudioSource` (or adds one), copies the clip and `spatialBlend`, sets
`playOnAwake = false`, adds a `LoopedObject { go, audioSource, loopStart, loopEnd }` to `loopedAudio` and plays.
`LoopSFX` (every frame) rewinds each looped source to `loopStart` after `loopEnd`; it dereferences each entry's
`audioSource` without a null check. **A looped copy must be stopped (`StopLoopSFX(go, …)` removes the entry) before
the copy is destroyed**, or `SoundManager` throws every frame. `StopLoopSFX(go, true)` jumps the source to `loopEnd`
(the clip's tail plays); `false` stops it.

## Refill cans: `FluidRefill` (oil 8, brake 10, coolant 11, washer 12, power steering 13)

- `ToolsManager.Use(type)` sets `currentUsedTool`, calls the tool's `Use()` and `ToolIsActive = true`.
- `FluidRefill.Use` (`0x181287510`): `carLoader` = car under the mouse; `IsActive = true`; `PlaySFX("Material")`;
  `fluidId = carLoader.CurrentUsedFluidId`; `startFluidAmount` = level; `itemWorkOn = ToolsManager.ItemWorkOn` (the
  reservoir cap part, `+0xE8`) and its `PartScript`. The pour object `fluidRefillLogic` (a separate GameObject,
  inactive since `Awake`) is placed at `itemWorkOn.parent.Find("_OilRefillPivot")` (position and rotation) when that
  pivot exists; else for oil at the cap's position (height `+0.05` over its own), euler `(x of the logic, y of the
  cap's local euler, z of the logic)`; else the `FluidRefill` transform goes to the cap's position with the car root's
  rotation. Camera: `EnterToOilRefill`/`EnterToFluidRefill` at the logic's `CameraPosition` child.
  `FluidRefillLogic.carLoader/CurrentFluid/fluidId/power = 0`; starts `UseAnim`.
- `UseAnim` (`d__9`): waits until the cap is unmounted (`itemWorkOnPartScript.IsUnmounted`) and its
  `MountAnimationCompleted`, then activates `fluidRefillLogic` and enables it. `OnEnable` stores `puszka` (the can)'s
  local rotation, clears both emissions, sets the IO description and starts `Delay` (1 s, then `canUse = true`).
- `FluidRefillLogic.Update` (`0x181289060`), every frame once `canUse`: `power += dt` while `ActionMechanic` is held,
  `-= dt` otherwise, clamped 0..1.
  - `power > 0.4`: the logic's `AudioSource` plays (when not already), `emit` emission on; below level 0.05 the
    condition becomes 1; below level 0.65 `FluidsData.AddFluid(dt*0.1, dt*0.05, …)` and `emit` rate 300/s; at level 1
    with `power > 0.8` `emitFull` (overflow) is switched.
  - `power ≤ 0.4`: `AudioSource.Pause()`, `emit` off, `emitFull` off.
  - The can tilts: `puszka.localRotation = Lerp(current, Lerp(start, start * Euler(0, 0, a), power), dt * 10)` with
    `a = -40°` below level 0.65 and `-20°` above.
- `FluidRefill.Hide` (`0x1812887E0`): `StopAllCoroutines`, `PartScript.ActionAutomatic` on the cap (it screws back),
  `AddPlayerMoney(-(Δlevel * 20))`, `ExitFromFluidRefill`, `fluidRefillLogic.SetActive(false)` (`OnDisable` resets the
  can's rotation).

Visual parts: the can (`puszka`, a world object at the reservoir, visible to the main camera), the `emit` stream and
`emitFull` overflow particles, the logic's `AudioSource`. The fluid level in the reservoir rises through
`CarFluid.SetLevel` (the reservoir material's `LiquidLevel`), which row 4's level updates already drive on a receiver
(at its send rate, not per frame).

The pour state lives in `FluidRefillLogic.power` (actor only). The activity today carries the tool and the car, not
the reservoir or `power`.

## Fluid extractor (`FluidExtractor`, tool 15)

`Use` (`0x181285B00`): `PlaySFX("Material")`, the tool object on, `EnableActiveCarToolsRenderCamera(true)` (it renders
through `CarToolsRenderCamera` into the UI's `cameraRawImage`: a first-person overlay, not a world object). `UseAnim`
(`d__5`): one-shot `PlaySFX("DrainTool", reservoir position)`, a 2 s `LeanTween.value` that writes the level down per
frame and moves the piston and the tube's blend shape (in the overlay), then `SetLevelAndCondition(0, 0)`,
`OnZeroFluid`, `GameMode 0x17`. Nothing of it is in the world except the sound; the level arrives through row 4.

## Other fluid paths

- `OilBayonet` (tool 7, examine): the dipstick overlay; reads only.
- Part unmount drains (`PartScript.CheckMessageOnHide`/`HideBySavegame`, `native-decompile.md` d): level and spill
  money, no visual.
- `ToolType.OilDrain` (9) has no case in `ToolsManager.Use` (it only sets `ToolIsActive`); the drain is the oil bin.
- `MountObjectSpray` (14): rust spray on bolts, not a fluid.

## Needs a game run

- `Oil_drain_h`: its components and children (particle systems, renderers, `AudioSource`, scripts), layer, and whether
  a copy (instantiated under an inactive parent, scripts removed) plays its particles headless.
- `fluidRefillLogic` of each refill: children (`puszka`, `emit`, `emitFull`), scripts, layer, `AudioSource`.
- `GameVolume` on the test games (when 0, `PlayLoopSFX` returns at once; the scenario then checks the call trace).
