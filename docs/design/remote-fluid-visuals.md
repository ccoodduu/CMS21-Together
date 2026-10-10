# Design: fluid tool visuals for remote players

Playtest 3: no oil drain animation on the friend's game. Findings: `docs/spikes/remote-fluids.md`. Extends row 17
(`openspec/changes/remote-visual-feedback`, `Logic/Visuals`): same contract (D1), same activity channel (D5), same
limits and cancel paths. Branch `feat/remote-fluid-visuals`.

## Scope

| Actor's action | Replayed on the other players' games | Driven by |
|---|---|---|
| Oil bin drain | the oil stream from the drain plug (particles, coloured by the oil's condition), the `OilDrain` loop sound and its tail, the plug hidden | `PlayerActivity` `CarTool`/`OilBin`/loader (sent today) |
| Refill can (oil, brake, coolant, washer, power steering) | the can at the reservoir, tilting with the pour, the stream particles and the pour sound while the button is held | `PlayerActivity` `Fluid` + `PartKey` (reservoir cap) + `Progress` (the actor's `power`), new values in existing fields |
| Fluid extractor | nothing new: its animation is a first-person overlay (`CarToolsRenderCamera`); the avatar already holds it | — |

State is unchanged: the level still arrives through row 4's `Fluids` as today. No packet type or field is added.

Order of work: the drain first (the playtest finding, proof scenario), then the pour on the same branch. If the
runtime probe shows a pour copy cannot run without touching the receiver's state, the pour stops there and is
reported with options; the drain does not depend on it.

## D1. Visual-only, as row 17

- Each effect is a `VisualEffect` (`VisualKind.Drain`, `VisualKind.Pour`) run by `VisualScope` (caps 16/4 per car,
  40 m, `RemoteVisuals`, `notInGarage`, `beforeSync`; counted in `started`/`finished`/`skipped`; leak detector on).
- It owns a **copy** of the receiver's own tool object (`ToolsManager.Oil_drain_h`, or the refill's
  `fluidRefillLogic` object): instantiated under an inactive holder, every `MonoBehaviour` removed before it is
  activated (so `FluidRefillLogic.Update` never runs and never writes fluids), colliders (the cans' `EmitCollision`
  and the oil funnel on layer 29 would catch the receiver's clicks) and cameras removed too, renamed
  `TogetherFluid[<kind> <player>]`, at the scene root with the source's world scale. Particle systems, renderers and
  `AudioSource` stay.
- The only write to existing objects: `forceRenderingOff` on the drain plug's renderers (`korek_spustowy_1(0)` under
  `e_engine_h`), recorded and restored by `VisualScope` like a ghost's hidden part. The plug's `SetActive`,
  `EnableIO`, the oil bin's `InteractiveObject.on`, `FluidsData` and `OnOilDrainFinished` are never touched.
- Sound: `SoundManager.PlayLoopSFX(copy, "OilDrain", 0.35, 2.55)` and `StopLoopSFX(copy, …)` (keyed by the copy, so
  the receiver's own drain is independent). Every end path calls `StopLoopSFX(copy, false)` **before** the copy is
  destroyed (`SoundManager.LoopSFX` would throw every frame on a destroyed source). The pour plays and pauses the
  copy's own `AudioSource`, as `FluidRefillLogic` does.

## D2. Start, follow, end

`FluidReplay.Update` (every frame, connected) looks at each roster player's `Activity`:

- **Drain** wanted while the activity is `CarTool`, `ModTool = OilBin`, loader ≥ 0. Start: the car is loaded and
  ready, the plug exists and is active, `Admit` passes. The copy goes to the plug (`position`, `LookRotation(-forward)`
  as the game does), particles start with the start colour `Lerp(black, white, oil condition)` at alpha 0.75 (the
  receiver's condition: row 4 keeps it equal), the loop sound starts, the plug's renderers are hidden.
- **Pour** wanted while the activity is `Fluid` with a refill `ToolType`, a `PartKey` and loader ≥ 0. Start: the
  reservoir part resolves in the receiver's registry, `Admit` passes. The copy is placed as `FluidRefill.Use` places
  the can (the `_OilRefillPivot` sibling of the cap when present; else the cap's position, oil 5 cm higher with the
  cap's local yaw, the others with the car root's rotation). Each frame the shown power follows `Progress/16`; the
  can tilts with the game's formula (`start * Euler(0, 0, a)`, `a` −40° below level 0.65, else −20°), the stream and
  sound run while the shown power is above 0.4.
- One start attempt per received activity value (no `skipped` spam); a value that arrives before the garage is ready
  or the sync is finished is tried again when it is.
- **End:** the activity no longer wants the effect (finished, travelled, idle), the player leaves (`CancelPlayer`), the
  car changes (`SpawnSeq`, delete), the scene is left (`CancelAll`), `RemoteVisuals` goes off (checked every step), or
  60 s pass (a lost end). A normal drain end plays the game's tail: particles `Stop()`, `StopLoopSFX(copy, true)`,
  the plug stays hidden until the particles are gone (at most 3 s), then the copy is destroyed and the plug restored.
  A cancel stops at once.
- Refused or never started on the actor: no activity, no effect (the lock gate refuses before `UseOilbin` runs).

## D3. Actor side (pour only)

`ActivityCapture.FromHandTool`, for a refill tool whose `fluidRefillLogic` object is active (the can is out, the same
moment the actor sees it): `PartKey` = the registry key of `ToolsManager.ItemWorkOn`'s `PartScript`, `Progress` =
`QuantizeProgress(power)`. `power` ramps over 1 s, so a press or release is 3–4 activity updates (within D5's 4/s);
holding steady sends nothing. `ToolProps` shows no hand prop for a `Fluid` activity with a `PartKey` (the can is at
the reservoir, as on the actor's screen).

## Not done

- The extractor's piston and tube (overlay only) and its one-shot `DrainTool` sound (the activity has no "now
  extracting" moment).
- The reservoir's liquid level rising per frame: it follows row 4's updates.
- A drain shorter than one activity poll (oil level below 0.05) may not be seen.
- When the actor also holds a part claim during the drain, the activity reports the part work (claims come first)
  and the drain replay ends early. The drained car's parts are not clickable during the drain (`EnableIO(false)`).

## Proof

Scenario `visual-fluids` (areas `visuals`, `tools`), harness verbs `vfx-fluids on|off|report [loader]|probe` (reads
the scene: `TogetherFluid` copies, their particles and audio, the plug's hidden renderers, traced `PlayLoopSFX`/
`StopLoopSFX`; it compiles against code without the replay) and `vfx-pour <loader> <tool> cap|start [noopen]|hold
<s>|end|status` (the actor takes the can through `ToolsManager.Use` while the cap is on, the cap is unscrewed with
`vfx-unscrew`, the pour is held by keeping `FluidRefillLogic.power` at 1).

1. A fills the oil and drains with `tool-use OilBin` (the game's `UseOilbin` through the lock gate). While A drains, B
   has a `Drain` effect for A, one copy with its `Emit` playing, the plug's renderers hidden (the plug stays active)
   and `PlayLoopSFX(<copy>, "OilDrain")`; afterwards no effect, no copy, no hidden renderer, `StopLoopSFX(playEnd)`,
   no leak, oil 0 on both.
2. `RemoteVisuals` off on B: nothing shown, `skipped.disabled`.
3. A refills brake fluid: B shows the can at B's fill cap (0 m), streams only while A holds the pour, keeps the can
   after A lets go, removes it when A puts the can away; the level A poured is on both.
4. A leaves mid-drain: B's replay is cancelled at once and the loop stopped.

Runs: old code (branch harness and scenario, client of `main`) fails `20261010-031321_L2` (every replay check);
without the pour commit only the pour checks fail (`20261010-030818_L2`); the branch passes (`20261010-030649_L2`).
Smoke plus `visual-*` and `tools-car-effects`: `20261010-031455_regression.json`, all pass but `visual-screens`
(`run-all: skip`, needs graphics; its `stand-before` got an empty name).

Runtime notes: Unhollower's `MinMaxGradient(Color)` constructor throws (the first branch run left a copy streaming
without its effect, `20261010-024554_L2`); the stream colour is set through the fields of an allocated gradient. On the
spawning game the brake servo and its cap read about 100 m below the car (y −99), on the other game they are at the
car; B places its can from its own cap, so this does not affect the replay (not investigated further).
