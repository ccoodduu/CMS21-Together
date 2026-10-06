# Spike: workshop machines (row 5a, `sync-workshop-machines`)

Goal: answer task group 1 of `openspec/changes/sync-workshop-machines` statically. For each bench machine: the player
call chain, every inventory call (real or inlined), where the state lives, the remote-apply primitives and their side
effects, which patches never fire, and the hook points. Method: static decompile only (setup in `native-decompile.md`);
the game was not launched. Date: 2026-10-06.

Decompiles: `%USERPROFILE%\CMS21-TestInstalls\native\out\wsm_clean` and `wsm2_clean` (targets in
`work\targets\wsm1.txt`, `wsm2.txt`). All call sites named below as "real" were checked against the raw bytes
(`E8`/`E9` rel32 to the method's own address). VAs are `0x180000000 + RVA`.

## Short answers

1. **Every put is "inventory first, machine second", in one call stack.** The UI path calls the real
   `Inventory.Delete`/`DeleteGroup` and then `SetGroupOn…`/`SetItem…`. The hooks see both, in that order.
2. **Every take is a real `Inventory.Add`/`AddGroup` call, except the engine stand.** Its take uses an inlined
   `groups.Add`, so the `AddGroup` hook stays silent.
3. **Six hooks named in the design never fire, because their methods are inlined or only allocated:**
   - `WheelBalancerLogic.FinishBalance`/`CancelBalance`: the window writes the two flags directly.
   - `TireChangerLogic.Clear()`, `WheelBalancerLogic.Clear()` and `NotificationCenter.TakeOffEngineFromStand()`:
     the pie lambdas build these coroutines themselves.
   - `RepairPartWindow.UpdateItemCondition`.
   - `PaintshopManager.MakePartPaintEffects`.
4. **The balance result is decided at take-off, not when the minigame ends.** The take coroutine sets
   `items[0].WheelData.IsBalanced = !balanceCanceled`. Opening the minigame is `WheelBalancerLogic.Balance(bool)`,
   and `SetGroupOnWheelBalancer(g, instant: false)` calls it, so a remote apply **must** pass `instant: true`.
5. **`SetGroupOn…` never removes the old models.** The tire changer, balancer, spring clamp, lathe and charger
   instantiate new models without destroying the previous ones, so a remote apply on an occupied machine has to clear
   it first. The engine stand clears itself.
6. **The game has exactly one engine stand.** All code paths and the save use `ToolsManager.Get().EngineStandLogic`
   (`+0x30`). See "Engine stand 2".
7. **The stand's group gets a new UID on every client that runs `SetGroupOnEngineStand`.** Each take-off builds new
   `Item`s with new UIDs.

## Shared entry points

- The pie menu actions are `PieMenuController.<GetOnClick>b__72_N`, or `PieMenuController.<>c.<GetOnClick>b__72_N`
  for the static ones. Unhollowed, they are `PieMenuController._GetOnClick_b__72_N` (in `Assembly-CSharp-firstpass`).
- "Choose item" windows are `ChoosePartUpWindow` (window `0x10`). They submit through
  `NotificationCenter.NewButtonAccept(hash)` (`0x1809DD…`, string-switch on `WindowType`). Real call sites in it:

| `WindowType` | Calls, in order (VA of the call) |
|---|---|
| `SelectWheelToSeparate` | `Inventory.GetGroup(uid)`, **`DeleteGroup(uid)`** `0x1809DD4ED`, `TireChangerLogic.SetGroupOnTireChanger(g, false, false)` `0x1809DD53A` |
| `SelectSpringToSeparate` | `GetGroup`, **`DeleteGroup`** `0x1809DD662`, `SpringClampLogic.SetGroupOnSpringClamp(g, false, false)` `0x1809DD6AF` |
| `SelectWheelToBalance` | `GetGroup`, **`DeleteGroup`** `0x1809DECEF`, `WheelBalancerLogic.SetGroupOnWheelBalancer(g, false)` `0x1809DED37` |
| `SelectItemForBrakeLathe` | **`Inventory.Delete(item)`** `0x1809DD37D`, `BrakeLatheLogic.SetItem(item, false)` `0x1809DD3C5` |
| `SelectItemForBatteryCharge` | **`Delete`** `0x1809DE4A4`, `BatteryChargerLogic.SetItemOnBatteryCharger(item, false)` `0x1809DE4EC` |
| `SelectItemForPaintshop` | **`Delete`** `0x1809DDB8D`, `PaintshopManager.PrepareForPart(item)`, `WindowManager.Show(0x15)` |
| `SelectEngine` + `CreateEngineWindow` | `EngineStandLogic.SetEngineOnEngineStand(item)` `0x1809DD9A5` (no inventory call) |
| `SelectEngine` + `ChooseEngineWindow`, `Crane == false` | `NotificationCenter.ActionHangOn(group)` `0x1809DDA9E` |

- Connect paths (tire changer `WheelConnect`, spring clamp `SpringConnect`) build a group from single items:
  - `ChoosePartUpWindow.SelectItemInCreateGroup(item)` (`0x180F98E20`) runs once per chosen item and calls the real
    **`Inventory.Delete(item)`** (`0x180F98FDA`).
  - After the last segment it calls `SubmitGroupItem` (`0x180F996B0`). That method makes a
    `new GroupItem(items[0].ID)` (**new UID**, `IsNormalGroup = false`), adds the items, hides the window, then calls
    `SetGroupOnTireChanger(g, false, true)` (`0x180F998FF`) or `SetGroupOnSpringClamp(g, false, true)`
    (`0x180F99971`).
  - Cancel: `Back` and `BackAllItems` return the chosen items with the real **`Inventory.Add(item)`** (`0x180F99F7B`,
    `0x180F9A230`).
- Garage load: `GarageLoader.LoadMachines` (`0x180D69…`) reads `NewMachines` and calls every setter with
  `instant: true`.
  - `NewMachines` fields: `GroupOnWheelBalancer`, `WheelWasBalanced`, `GroupOnTireChanger`, `…IsMounting`,
    `GroupOnSpringClamp`, `…IsMounting`, `GroupOnEngineStand`, `EngineStandAngle`, `ItemOnBatteryCharger`,
    `ItemOnBrakeLathe`.
  - It sets `balancer.balanceCanceled = !WheelWasBalanced` directly.
  - It **inlines `BrakeLatheLogic.SetItem`**, so a `SetItem` patch fires for UI puts but not during load.
    Load forces `Item.Condition = 1`.

## Tire changer (`TireChangerLogic`, `ToolsManager+0x20`)

State: `GroupOnTireChanger` `+0x88`, `GroupOnTireChangerIsMounting` `+0x90` (true = connected wheel), models `rim`
`+0x48` and `tire` `+0x50`, and `InteractiveObject` `+0x58`.

| Action | Chain | Inventory |
|---|---|---|
| Separate (put a wheel) | pie `b__72_63` → `ChoosePartUpWindow.Show("WheelSeparate", 4)` → `NewButtonAccept` | real `DeleteGroup(uid)`, then `SetGroupOnTireChanger(g, false, false)` |
| Connect (put rim + tire) | pie `b__72_62` → `Show("WheelConnect", 5)` → `SelectItemInCreateGroup` ×2 → `SubmitGroupItem` | real `Delete(rim)`, real `Delete(tire)`, a new group (new UID), then `SetGroupOnTireChanger(g, false, true)` |
| Take | pie `b__72_61` (`0x1812DC060`) builds `<Clear>d__23` itself (**`Clear()` builder inlined**) → `<Clear>d__23.MoveNext` `0x180B80AE0` | state 0: IO off, destroy `tire`/`rim`, yield one frame. State 1: IO `Reload`. **Separated:** real `Inventory.Add(item, showPopup: true)` for each item (`0x180B80E11`). **Connected:** `items[0].WheelData.IsBalanced = false`, then real `AddGroup(group)` (`0x180B80EA7`). Then `PlaySFX("PartTakeOff")`, `GroupOnTireChanger = null`, IO on, `OnWheelTake` |

`SetGroupOnTireChanger(GroupItem, bool instant, bool connect)` `0x1810CE480`:

- returns at once if `ItemList.Count < 2`;
- sets the group and `IsMounting = connect`;
- invokes `OnWheelConnect` or `OnSeparate` (tutorial callbacks);
- creates the models with `GameInventory.GetGO(id)`, which **instantiates** a prefab (`0x18102F…`), and paints and
  sizes them;
- calls `PartScript.SetCondition` on the model parts, plays `audioSource` when `!instant`, and runs LeanTween moves
  (zero length when `instant`);
- turns `InteractiveObject.Off()` on. The completion lambdas `b__20_0`/`b__20_1` set IO on, `Reload` and
  `OnSeparateFinished`/`OnWheelConnectFinished`;
- if either item is not in the part database, it nulls `rim`, `tire` and the group.

There is no inventory call. **The old `rim`/`tire` are not destroyed**, so calling it on an occupied changer leaks
models.

Silent clear: `ClearForTutorial()` (private IEnumerator, builder `0x1810D0930`, body `<ClearForTutorial>d__24`
`0x180B80F80`). It destroys the models, waits one frame, nulls the group and reloads the IO. It makes no inventory
call, plays no sound and fires no action.

## Wheel balancer and minigame (`WheelBalancerLogic`, `ToolsManager+0x18`)

State: `groupOnWheelBalancer` `+0x80` (private; use `GetGroupOnWheelBalancer()`), `hasToFinishTween` `+0x60`,
**`balanceCanceled` `+0x61`** (`IsCanceled()`/`SetCanceled(bool)`), and the models `rim` `+0x50`, `tire` `+0x58`.

| Action | Chain | Inventory |
|---|---|---|
| Put | pie `b__72_65` (`0x1812DC3C0`) when the balancer is empty → `ChoosePartUpWindow.Show(GetUnbalancedWheels, 3)` → `NewButtonAccept "SelectWheelToBalance"` | real `DeleteGroup`, then `SetGroupOnWheelBalancer(g, false)`. That call **calls `Balance(false)` itself** (`0x18092ECAE`), so the minigame opens at once |
| Rebalance | pie `b__72_65` when occupied → `Balance(true)` (`0x1812DC4CC`) | — |
| Minigame | `Balance(bool)` `0x18092F3C0`: optional `ResetWheel`, `balanceCanceled = false`, IO off, `WindowManager.DisableAllWindowsOpening`, `GameScript+0x38` disabled, builds `<OpenWheelBalanceWindow>d__21` itself (**builder inlined**). `d__21` `0x1818CE100` shows window `0x2F` (`WheelBalanceWindow`) | — |
| Finish | `WheelBalanceWindow.BalanceAction` jmp → `ProcessGameResult` `0x18092D420` (also from `HandleInput`, `HandleSubmitGameInput`, `StopMiniGame`). On success it hides the window and writes `balancer+0x60 = 1`. **That is `FinishBalance` inlined**; `FinishBalance` `0x18092F980` has no callers | — |
| Cancel | `WheelBalanceWindow.CancelAction` `0x18092C8F0` (a delegate from `PrepareDescriptions`) hides the window and writes `balancer+0x60 = 0x0101`. **That is `CancelBalance` inlined** | — |
| Spin end | the tween update `<Balance>b__20_1` checks `hasToFinishTween` and **jmp `FinishBalanceInternal`** `0x18092F9A0` (real, `0x1809303D8`). That stops the loop SFX, tweens the cover and clamp back, sets IO on and fires `OnWheelBalanceFinished`. It runs for success and cancel alike | — |
| Take | pie `b__72_64` (`0x1812DC2B0`) builds `<Clear>d__26` (**`Clear()` builder inlined**) → `MoveNext` `0x1818CDB80` | state 0: destroy models, one frame. State 1: **`items[0].WheelData.IsBalanced = !balanceCanceled`**, real `AddGroup(group)` (`0x1818CDE03`), `PlaySFX`, `stat_wheels_balanced` if not cancelled, group = null, `OnWheelTake` |

So "balanced" is the machine flag `!balanceCanceled` until take-off. A freshly put wheel whose minigame was never
finished keeps `balanceCanceled` from the last run (`false` after `Balance()` starts). Only cancel sets it.

`SetGroupOnWheelBalancer(GroupItem, bool instant)` `0x18092E6E0`:

- returns if `ItemList.Count < 2`;
- sets the group and invokes `OnWheelBalance`;
- instantiates the models (old ones **not** destroyed), calls `ResetWheel`, parents them and calls
  `IO.ReinitMaterials`;
- **when `!instant`, calls `Balance(false)`**;
- does not touch `balanceCanceled`.

Silent clear: `ClearForTutorial()` (public, `<ClearForTutorial>d__27` `0x1818CDF50`). It destroys the models, nulls
the group and reloads the IO; it does not reset `balanceCanceled`. `SetCanceled` (`0x18047A740`) and
`GetGroupOnWheelBalancer` (`0x18047A860`) are folded bodies shared by 5 and 30 methods. Call them, but **never patch
them**. `IsCanceled` (`0x18092FF70`) is its own function.

QoLmod 4.4 patches `WheelBalanceWindow.StartMiniGame` and the three `SetGroupOn…` methods
(`tools/test-env/fixtures/mod-targets/QoLmod.json`).

## Spring clamp (`SpringClampLogic`, `ToolsManager+0x38`)

State: `GroupOnSpringClampIsMounting` `+0x38`, `GroupOnSpringClamp` `+0x40`, and the models `shockAbsorber`,
`spring` and `coverShockAbsorber`. `IsEmpty()` checks the `shockAbsorber` model, not the group.

| Action | Chain | Inventory |
|---|---|---|
| Separate | pie `b__72_59` → `ChoosePartUpWindow` type 7 with `Inventory.GetAbsorbers()` → `NewButtonAccept "SelectSpringToSeparate"` | real `DeleteGroup`, then `SetGroupOnSpringClamp(g, false, false)` |
| Connect | pie `b__72_58` → `Show("SpringConnect", 6)` → `SelectItemInCreateGroup` ×3 → `SubmitGroupItem` | real `Delete` per item, a new group (new UID), then `SetGroupOnSpringClamp(g, false, true)` |
| Take | pie `b__72_60` → **`ClearSpringClamp()` (void, real call `0x1812DC03A`)** `0x180D93250` | destroys the 3 models. **Not mounting:** real `Add(item)` for each item (`0x180D934B2`). **Mounting:** real `AddGroup(group)` (`0x180D93504`). Then `PlaySFX`, `OnTake`, group = null |

`SetGroupOnSpringClamp(GroupItem, bool instant, bool mount)` `0x180D93580`:

- **sets the group before** it checks `ItemList.Count < 3`, then returns with no models;
- instantiates 3 models (old ones not destroyed) and reads `Data/SpringClamp` INI offsets;
- runs `SeparateSpring`/`ConnectSpring` (tweens, SFX).

There is no silent clear. Destroy the models and null the fields directly, or run `ClearSpringClamp` with the UIDs
neutral (design D3.3; its adds are real calls).

## Brake lathe (`BrakeLatheLogic`, `ToolsManager+0x40`)

State: `Item` `+0x60`, model `brakeDisc` `+0x50`, and `canRotate`/`rotateSpeed`. `IsEmpty()` checks `brakeDisc`.

| Action | Chain | Inventory |
|---|---|---|
| Put | pie `b__72_67` → `ChoosePartUpWindow` with `Inventory.GetItemsForBrakeLathe()` (type 8) → `NewButtonAccept "SelectItemForBrakeLathe"` | real `Delete(item)`, then `SetItem(item, false)` |
| Process | `SetItem(Item, bool instant)` `0x180F43760` | **`instant`:** `Item.Condition = 1` at once. **Otherwise:** tweens (`RotorTuningTool` SFX, particles). The update lambda writes the disc `PartScript.Condition`; the completion `b__13_2` writes `Item.Condition = 1` (uncertain: whether an early take keeps the old condition) |
| Take | pie `b__72_69` → **`Clear()` (void, real `0x1812DCAEA`)** `0x180F43450` | destroys `brakeDisc`, real `Add(Item, false)` (`0x180F43523`), `PlaySFX`, hides `strap`, `Item = null`. It does not cancel the tween |

## Battery charger (`BatteryChargerLogic`, `ToolsManager+0x28`)

State: `ItemOnBatteryCharger` `+0x28`, model `battery` `+0x20`. `IsEmpty()` checks `battery`.

| Action | Chain | Inventory |
|---|---|---|
| Put | pie `b__72_66` → `ChoosePartUpWindow` with `Inventory.GetItems("akumulator")` (type 2) → `NewButtonAccept "SelectItemForBatteryCharge"` | real `Delete`, then `SetItemOnBatteryCharger(item, false)` `0x180B62BC0` |
| Process | `SetItemOnBatteryCharger` instantiates the model and calls the real **`BatteryChargerActivate(true)`** (`0x180B62DC5`). **`instant`:** `Condition = 1`. **Otherwise:** `LeanTween.value(Condition → 1, (1 - Condition) * 10 s)`, and the update `b__5_0` writes `item.Condition = val`. So an early take gives a partial charge | — |
| Take | pie `b__72_68` → **`ClearBatteryCharger()` (real)** `0x180B62880` | `LeanTween.cancel`, real `BatteryChargerActivate(false)`, destroy the model, real `Add(item, false)` (`0x180B629DF`), `PlaySFX`, `ItemOnBatteryCharger = null` |

`BatteryChargerActivate(bool)` only swaps two LOD GameObjects. Its only callers are the setter (`true`) and
`Clear` (`false`). **"Active" is therefore just `ItemOnBatteryCharger != null`.** The design's `Active` property
(D5) is redundant.

## Engine stand (`EngineStandLogic`, `ToolsManager+0x30`)

State: `EngineStand` (transform) `+0x18`, `PartScriptCuller` `+0x20`, `engineGameObject` `+0x28`,
`GroupOnEngineStand` `+0x30` and `EngineStandAngle` `+0x38`.

| Action | Chain | Inventory |
|---|---|---|
| Hang on (from inventory) | pie `b__72_33` → `WindowManager.Show(0x34 ChooseEngineWindow, ["engine_all", false])` → `SelectEngineAction` jmp `ActionHangOn(group)` (or `NewButtonAccept` `0x1809DDA9E`) | `ActionHangOn` `0x1809E0E30`: **real** `SetGroupOnEngineStand(group, true)` (`0x1809E0EB0`), `StartCoroutine`, then **jmp `Inventory.DeleteGroup(uid)`** (`0x1809E0F11`, Ghidra merged the body). The hook fires, and the group still has all its items |
| Create engine | pie `b__72_34` → `Show(0x30 CreateEngineWindow)`, which lists `GameInventory.GetEnginesToCreate` → `CreateEngineAction` → **real `SetEngineOnEngineStand(currentEngine)`** (`0x180E5A40F`) | **None.** `SetEngineOnEngineStand` `0x180BE8CB0` wraps the item in `new GroupItem(id)` (`IsNormalGroup = false`, `Size = 1`) and starts `SetGroupOnEngineStand(g, true)` |
| Coroutine | `SetGroupOnEngineStand(GroupItem, bool withFade)` `0x180BE8E80` (a real builder) → `<SetGroupOnEngineStand>d__8.MoveNext` `0x1808886D0` | see below |
| Rotate | `PieMenuController.<>c.<GetOnClick>b__72_56`/`_57` → **real `IncreaseEngineStandAngle(±90)`** `0x180BE9960` | — |
| Take off | pie `b__72_35` (`0x1812D7DF0`) builds `<TakeOffEngineFromStand>d__25` itself (**`TakeOffEngineFromStand()` builder `0x1809E0F20` has no callers**) → `MoveNext` `0x180A6F290` | fade in, then real `GetGroupOnEngineStand()` (`0x180A6F5E3`), then **inlined `Inventory.groups.Add(group)`: `AddGroup` does not fire**. Then `PlaySFX`, real `ClearEngineStand()` (`0x180A6F66F`), fade out |

`d__8`:

- states 0–1: fade only when `withFade`, together with an input lock;
- state 2: real `ClearEngineStand()` (so it **clears an occupied stand itself**), then one frame;
- next state: `GroupOnEngineStand = new GroupItem(group.ID)` (**new UID**) with `ItemList = new List<Item>(group.ItemList)`,
  `Size`; then `Instantiate(Resources.Load(ID), EngineStand)`;
- state 5: for each `PartScript`, it finds the item by `gameObject.name` in the **caller's** `group.ItemList`.
  - item found: `TunePart`, `SetCondition`, `IsExamined`, `Quality`, paint, `SetMountObjectData`, then `RemoveAt` on
    the caller's list, which **consumes the passed group**;
  - no item: `UnMountByGroup(true, instant: true)` (`IsOilDrainCheckFill` excepted);
- then it places the engine at the pivot, runs `PartScriptCuller.Init`, `GameScript.ReinitPartScriptCuller` and
  `SwitchLod`.

`GetGroupOnEngineStand()` `0x180BE8F90` (also called by `GarageLoader.Save`) **replaces** `GroupOnEngineStand.ItemList`
with one `new Item(name)` (new UID) per **mounted** `PartScript` under `EngineStand`. Each item gets condition,
examined, `tunedID ?: id`, quality, colour, paint and `GetMountObjectDataForSave`. It is therefore a full snapshot of
the engine on the stand, part work included, but every call spends new UIDs.

`ClearEngineStand()` `0x180BE8A90` deletes materials and shader backups, destroys `engineGameObject`, sets
`GroupOnEngineStand = null` and resets the culler. **No inventory call and no sound: it is a silent clear.**

`IncreaseEngineStandAngle(float)` returns early while the stand is tweening. Otherwise it updates `EngineStandAngle`
synchronously (wrapped to 0..360), tweens the parent rotation over 1 s and plays `EngineStandRotate`.
`SetEngineStandAngle(float)` `0x180BE95D0` sets the field and the transforms at once, without sound.

Part work on the engine on the stand uses the generic `PartScript` paths (`Hide`/`ShowMounted`/`DoMount`, see
`native-decompile.md` d). Nothing in them refers to the stand; the parts are children of `engineGameObject`.

### Engine stand 2

- No code refers to a second stand. Every pie lambda, `NewButtonAccept`, `ActionHangOn`, `CreateEngineAction`,
  `TakeOffEngineFromStand`, `LoadMachines` and `Save` use `ToolsManager.Get().EngineStandLogic`.
- `NewMachines` saves one group and one angle, and `strings.tsv` has no `Engine_stand_2`.
- If a second `EngineStandLogic` exists in the scene, its pie menu acts on stand 1. Uncertain until runtime check 1.

## Other bench machines

- **Repair table** (`RepairPartWindow`):
  - `UpdateItemCondition` `0x181B01600`, `RepairItem` and `BreakItem` have **no callers**; they are inlined into
    `ProcessGameResult` `0x181B00FC0`.
  - `ProcessGameResult` is a real call from `RepairPartAction` and `StopMiniGame`. It writes
    `currentItemInfo.Item.Condition/Dent/RepairAmount` in place, calls `AddPlayerMoney(-price)` and sets
    `stat_fix_parts/body`.
  - The item stays in the inventory: no inventory call.
- **Paint shop (part)**:
  - Put: real `Inventory.Delete` (see the table above), then `PrepareForPart`.
  - Paint: the `MakePartPaintEffects()` builder `0x1809F81D0` has no callers; `<MakePaintEffects>d__80` allocates
    `<MakePartPaintEffects>d__82` itself.
  - Exit: `PaintshopManager.ExitFromPaintshopPart` `0x1809F7EE0` (real, from `PaintshopWindow.<HideCoroutine>d__11`)
    calls the real **`Inventory.Add(item, false)`** (`0x1809F7F77`) with the painted `Item` (same object and UID).
  - So the paint result travels in that ADD, and no `Update` is needed for the paint shop.

## Patches that never fire

| Method | Why |
|---|---|
| `WheelBalancerLogic.FinishBalance`, `CancelBalance` | `WheelBalanceWindow.ProcessGameResult`/`CancelAction` write `+0x60`/`+0x61` directly |
| `TireChangerLogic.Clear`, `WheelBalancerLogic.Clear`, `ClearForTutorial` (both) | the builders are inlined into pie `b__72_61`/`_64` and `ResetActions`. The managed side can still call them, and patches on `_Clear_d__23/26.MoveNext` are possible |
| `WheelBalancerLogic.OpenWheelBalanceWindow` | inlined into `Balance` |
| `NotificationCenter.TakeOffEngineFromStand` | inlined into pie `b__72_35` |
| `Inventory.AddGroup` on engine-stand take-off | `groups.Add` is inlined into `d__25` |
| `BrakeLatheLogic.SetItem` during garage load | inlined into `LoadMachines` (fires for UI puts) |
| `RepairPartWindow.UpdateItemCondition`/`RepairItem`/`BreakItem` | inlined into `ProcessGameResult` |
| `PaintshopManager.MakePartPaintEffects` | allocated inside `d__80` |
| `SetCanceled`, `GetGroupOnWheelBalancer`, `EngineStandLogic.GetEngineStand` | folded bodies (5, 30 and 1,270 methods share them): never patch them |

These fire (real calls): all `SetGroupOn…`/`SetItem…` from the UI, `ClearSpringClamp`, `BrakeLatheLogic.Clear`,
`ClearBatteryCharger`, `BatteryChargerActivate`, `Balance`, `FinishBalanceInternal` (tail jmp),
`WheelBalanceWindow.ProcessGameResult`/`CancelAction`, `IncreaseEngineStandAngle`, `ActionHangOn`,
`SetEngineOnEngineStand`, `SetGroupOnEngineStand` (builder), `GetGroupOnEngineStand`, `ClearEngineStand`, the pie
lambdas, `RepairPartWindow.ProcessGameResult` and `ExitFromPaintshopPart`.

## Remote-apply primitives

| Machine | Put (remote) | Clear (remote, silent) | Notes |
|---|---|---|---|
| Tire changer | `SetGroupOnTireChanger(g, true, mounting)` | `MelonCoroutines.Start(ClearForTutorial())` | Clear first if occupied. The put invokes the `OnWheelConnect`/`OnSeparate` actions (null outside the tutorial) |
| Balancer | `SetGroupOnWheelBalancer(g, true)` (**never `false`**), then `SetCanceled(!balanced)` | `ClearForTutorial()` | A remote "balanced" change on the same UID needs only `SetCanceled`. Do not write `IsBalanced` into the held items: vanilla overwrites it at take |
| Spring clamp | `SetGroupOnSpringClamp(g, true, mounting)` | none silent: destroy the 3 models and null the fields, or call `ClearSpringClamp` with the UIDs neutral | It plays a sound and runs `OnTake` |
| Brake lathe | `SetItem(item, instant)` | none: `Clear` adds to the inventory (neutral UIDs) | `instant: true` finishes the work at once (`Condition = 1`), and so does a save load |
| Battery charger | `SetItemOnBatteryCharger(item, instant)` | none: `ClearBatteryCharger` adds (neutral UIDs) | `Active` follows automatically |
| Engine stand | `MelonCoroutines.Start(SetGroupOnEngineStand(copy, false))`, with a **copy** of the group | `ClearEngineStand()` | Clears and rebuilds the whole engine. The local stand group gets its own UID. Parts missing from the group come out through `UnMountByGroup`, which may fire the `Examine` hook ~0.5 s later (crane spike) |
| Stand angle | `SetEngineStandAngle(a)` | — | silent |

## Recommended hook points

- **Puts** (all machines): the inventory REMOVE hooks fire first, then the setter postfix:
  - `SetGroupOnTireChanger`, `SetGroupOnWheelBalancer`, `SetGroupOnSpringClamp`, `BrakeLatheLogic.SetItem` and
    `SetItemOnBatteryCharger` postfixes send the slot snapshot;
  - the garage load calls the same setters with `instant: true`; D4's gating (no send before `SyncAck` or while
    the garage is not ready) already filters them.
- **Takes:**
  - tire changer and balancer: postfix `_Clear_d__23.MoveNext`/`_Clear_d__26.MoveNext` returning `false`. Or prefix
    them and capture the group while it is still set: the real `Add`/`AddGroup` happens in the last step;
  - spring clamp, lathe, charger: prefix `ClearSpringClamp`/`BrakeLatheLogic.Clear`/`ClearBatteryCharger`.
- **Engine stand:**
  - put: postfix `SetGroupOnEngineStand` (builder) as the trigger, plus postfix `_SetGroupOnEngineStand_d__8.MoveNext`
    returning `false` as "built";
  - take-off: **prefix `ClearEngineStand`**. When `GroupOnEngineStand` is already in `Inventory.groups` (by
    reference), it is a take-off: record that group as the added group (the `AddGroup` hook was blind), then send the
    empty slot;
  - angle: postfix `IncreaseEngineStandAngle` that compares `EngineStandAngle` before and after (it can return early).
    No poll or throttle is needed: the steps are discrete ±90°.
  - snapshot of part work: `GetGroupOnEngineStand()`. Prefer reading the `PartScript`s directly to avoid spending
    UIDs.
- **Balancer reservation:**
  - claim: prefix `WheelBalancerLogic.Balance(bool)`, the only path to the minigame;
  - block: prefix `PieMenuController._GetOnClick_b__72_65` (put/rebalance) and `_b__72_64` (take). Each returns
    `bool`: set `__result = true` and skip;
  - release / result: postfix `FinishBalanceInternal` (success and cancel), reading `IsCanceled()`;
  - optional: postfix `WheelBalanceWindow.CancelAction` and `ProcessGameResult`.
- **Repair:** postfix `RepairPartWindow.ProcessGameResult`.
- **Paint shop part:** no machine hook; the real `Delete`/`Add` pair carries it.

## Runtime checks left

1. **Engine stand 2:** list every `EngineStandLogic` in the garage scene, and check which one a pie action on the second
   stand moves.
2. Harmony patches on `_Clear_d__23/26.MoveNext`, `_SetGroupOnEngineStand_d__8.MoveNext` and
   `_TakeOffEngineFromStand_d__25.MoveNext` fire under MelonLoader 0.5.7.
3. `SetGroupOn…(…, true)` on an occupied tire changer, balancer and spring clamp duplicates models, as expected; and
   `ClearForTutorial()` is silent on both.
4. Remote `SetGroupOnEngineStand(copy, false)`:
   - no fade or input lock;
   - `Examine`/part hooks during its `UnMountByGroup` calls;
   - the stand's `PartScript`s are ignored by the `sync-car-parts` hooks.
5. Balancer: after a remote `SetGroupOnWheelBalancer(g, true)` plus `SetCanceled(false)`, a take yields
   `IsBalanced == true`. Also check what QoLmod's `StartMiniGame` skip does to `balanceCanceled` and
   `FinishBalanceInternal`.
6. Lathe and charger early take: what `Item.Condition` the take ADD carries.
7. "Create engine": what the stand shows (which parts come out unmounted), and that no inventory or money changes.
8. Pie lambda patches (`_GetOnClick_b__72_64/65`) can block, and the menu closes cleanly.
