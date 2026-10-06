# Spike: engine crane (engine out / engine in / engine swap)

Goal: sync "engine out" and "engine in" as one group transaction (all engine part keys plus the engine `GroupItem`).
Method: static decompile only (setup in `native-decompile.md`), game not launched. Date: 2026-10-06.

Decompiles are in `%USERPROFILE%\CMS21-TestInstalls\native\out\crane_clean`, `crane2_clean` and `crane3_clean`
(targets in `work\targets\crane*.txt`). Branch targets were checked against the raw x86 bytes (capstone), because
Ghidra **merges tail-jump targets into the caller** when auto-analysis is off. For example, `ChooseEngineWindow.SelectEngineAction`
looks as if it contains `InsertEngineToCar`'s body, but it ends in `jmp 0x1809E0B90`. Read a "merged" body as a call.

## Short answers

1. **Out:** the player uses the crane, and `CarLoader.UseEngineCrane` starts `ToolsManager.<UseEngineCrane>d__37`, which calls `NotificationCenter.ActionUnMountGroup(iO)`.
   That method creates `new GroupItem(engineGO.name)`, adds one `Item` per mounted engine `PartScript` and starts
   `PartScript.UnMountByGroup(true)` on each. It then adds the group to the inventory with **an inlined `groups.Add`,
   so our `Inventory.AddGroup` hook does not fire**. The crane does not hold the engine: the group goes to the inventory.
2. **In:** `ChooseEngineWindow.SelectEngineAction` → `NotificationCenter.InsertEngineToCar(group)` →
   `<ActionInsertEngineToCar>d__23`. For each engine `PartScript` with an item in the group it calls `MountByGroup(true)`
   (only if unmounted), `TunePart`, `SetCondition`, quality, paint and `SetMountObjectData`, and removes that item from
   `group.ItemList`. Then it calls **`Inventory.DeleteGroup(group.UID)`**, a real call that our hook sees. For a different
   engine it first sets `EngineParams.EngineSwap = group.ID` and runs `<SwapEngine>d__282`.
3. **No** `Hide`, `DoMount`, `ShowMounted` or `TakeOffCarPart` runs. State is set by `UnMountByGroup`, `HidePreview` and
   `MountByGroup`. The `Examine` hook *does* fire, 0.5 s after the engine comes out. Out and in (same engine) do
   **no reparenting**: the parts stay under `e_engine_h`, so their keys stay stable. **A swap destroys and rebuilds the
   engine subtree under `root`**, and that shifts sibling indices (see 3).
4. `EngineSwap` is written in exactly two places: `ActionInsertEngineToCar` (on a swap: the group ID; on putting back the
   original type: `""`) and `LoadCarFromFile` (from `NewCarData.engineSwap`). `DeleteCar` clears it, and
   `CreateEngine` reads it. To apply a stored swap, set it before `CreateEngine` runs during a load. On a car that is already loaded, run
   `SwapEngine(group)` after setting it.
5. Hooks: see the last section.

## 1. Engine out

Chain (VA):

| Step | Method | Notes |
|------|--------|-------|
| tool use | `ToolsMoveManager.Use` `0x1810D72A0`, tool 14 (`IOSpecialType.EngineCrane`) | calls `CarLoader.UseEngineCrane` |
| entry | `CarLoader.UseEngineCrane()` `0x18050B5B0` | inlines `ToolsManager.UseEngineCrane` (that builder has no callers) and starts `d__37` on the CarLoader |
| coroutine | `ToolsManager.<UseEngineCrane>d__37.MoveNext` `0x180B83C40` | see below |
| unmount | `NotificationCenter.ActionUnMountGroup(InteractiveObject)` `0x1809E13C0` | its only caller is `d__37` |
| per part | `PartScript.UnMountByGroup(bool b, bool instant, float delay)` `0x180FF5040` → `<UnMountByGroup>d__108` `0x180D2D180` | started with `(true, false, 0)` |

`d__37` state 0 does the checks and **can stop without changing anything**:

- each `iO.unMountPartsToUnmountGroup` part that is still mounted and not `SpecialGroup 1` → `GUI_EngineUnmountCraneWarning`;
- `GetMountedItemsAmount() < 1` → `GUI_EngineEmptyCraneWarning`;
- non-electric with oil level > 0 → `GUI_EngineOilCrane`.

If all checks pass it changes the input, fades in, and then:

```c
if (GetEngineSide() == 0) { hood, engine_cover_openable, clamshell_front_openable }
else                      { trunk, clamshell_rear_openable }
  -> if (!part.Switched && !part.Unmounted) StartCoroutine(CarLoader.SwitchCarPart(CarPart, instant:true))  // 0x180500290
NotificationCenter.ActionUnMountGroup(iO);       // iO = e_engine_h.GetComponent<InteractiveObject>()
yield WaitForSeconds(0.5); GameMode.SetCurrentMode(0); fade out; restore input
```

Note that the crane opens the hood, trunk or clamshell through the `SwitchCarPart(CarPart, bool)` IEnumerator overload, not
the hooked `SwitchCarPart(string)`. That `CarPart.Switched` change is not seen today.

`ActionUnMountGroup` (summary of the decompile, offsets checked against `dump.cs`):

```c
group = new GroupItem(iO.gameObject.name);   // ctor: IsNormalGroup=true, ItemList=new, UID=UIDManager.GetNewUID()
group.ItemList = new List<Item>();
group.Size = iO.transform.localScale.x;
foreach (ps in iO.gameObject.GetComponentsInChildren<PartScript>(false)) {   // inactive parts are skipped
    if (!ps.IsUnmounted && GameInventory.GetItemProperty(ps.tunedID ?: ps.id).SpecialGroup != 1) {
        item = new Item(ps.gameObject.name);
        item.Condition = ps.Condition; item.IsExamined = ps.IsExamined; item.NormalID = ps.tunedID ?: ps.id;
        item.Quality = ps.Quality; item.IsPainted, Color, PaintType, PaintData from ps;
        item.MountObjectData = ps.GetMountObjectDataForSave();
        group.ItemList.Add(item);
        StartCoroutine(ps.UnMountByGroup(true));      // first step runs now: IsUnmounted = true
    } else ps.HidePreview();                          // already unmounted, or SpecialGroup 1 (oil drain/check/fill)
}
GameManager.Instance.Inventory.groups.Add(group);    // Inventory.AddGroup inlined: no call to 0x180C7DEB0
```

The capstone disassembly of `0x1809E13C0..0x1809E1960` has no call to `Inventory.AddGroup` (`0x180C7DEB0`). Its
`Il2CppList.Add` goes straight to `Inventory.groups` (`GameManager+0x18` → `Inventory+0x20`). `Inventory.AddGroup`
itself is only `groups.Add(group)`.

`<UnMountByGroup>d__108`:

- state 0 runs synchronously inside `StartCoroutine`. It sets `IsUnmounted = true`, then for every `MountObject`
  calls `SetFullUnmountPosition` and `UpdateCondition(1)`. It disables the `hideWhenUnmontingMounting` and
  `disableOnUnmount` renderers and the colliders, and starts `TweenAlphaDissolve`.
- after `WaitForSeconds(0.5)` it sets layer 26 and calls **`PartScript.Examine(true)`** and `ReplaceShader(1)`.
- in both cases it ends with `UnblockBlockParts(true)`.

There is no `SetParent`, no `SetActive(false)` on a root, no sound, no XP and no money. The engine `GameObject` stays in the car,
and its parts are just unmounted and invisible. **The crane holds nothing**: the engine exists only as the inventory group.

Inventory events: one group add with a fresh UID, which today's hooks do not see. There are no `Inventory.Add` calls for
individual parts.

## 2. Engine in

| Step | Method |
|------|--------|
| mount tool | tool 15 → `CarLoader.UseEngineCraneMount` `0x18050BB50` → `ToolsManager.UseEngineCraneMount`. It checks `InteractiveObject.CanMountGroup`, then opens `WindowManager.Show(0x34 ChooseEngineWindow, [Swapoptions or {e_engine_h.name}, engineCrane:true])` |
| list | `ChooseEngineWindow.GetEngines`: `Inventory.GetGroupInventory(type)` for each allowed engine type |
| select | `ChooseEngineWindow.SelectEngineAction` `0x180F933B0`. Crane: `jmp NotificationCenter.InsertEngineToCar(currentEngine)`. Engine stand: `jmp NotificationCenter.ActionHangOn(group)` |
| entry | `NotificationCenter.InsertEngineToCar(GroupItem)` `0x1809E0B90`: changes the input, then builds and starts `d__23` itself. `ActionInsertEngineToCar` (the builder) is inlined and never called |
| coroutine | `NotificationCenter.<ActionInsertEngineToCar>d__23.MoveNext` `0x180A6A590` |

`d__23`:

```c
car = ToolsMoveManager.GetConnectedCarLoader(14); engine = car.e_engine_h;
if (car.customerCar && group.ID != engine.name) { ShowInfoWindow("GUI_SwapOnlyUserCar"); restore input; return; }  // no changes
GameScript.<+0x23> = false; crane IO disabled; fade in; wait for the fader;
open hood/engine cover/clamshell or trunk exactly as in "out" (SwitchCarPart(CarPart, true));
if (group.ID != engine.name) {                          // swap
    car.EngineParams.EngineSwap = group.ID;            // write-barrier helper 0x180082120 stores at +0x28 (checked in asm)
    IncrementStat("stat_swap");
    yield StartCoroutine(new CarLoader.<SwapEngine>d__282(car, group));   // CarLoader.SwapEngine builder inlined
} else {                                                // same engine
    if (group.ID == car.EngineParams.Type) car.EngineParams.EngineSwap = "";
    foreach (ps in engine.GetComponentsInChildren<PartScript>(false)) {
        i = group.ItemList.FindIndex(x => x.ID == ps.gameObject.name);  // first match by name
        if (i == -1) continue;                          // part stays as it is (unmounted if it came out)
        if (ps.IsUnmounted) ps.MountByGroup(true);
        ps.TunePart(item.NormalID ?: item.ID); ps.SetCondition(item.Condition, true);
        ps.IsExamined = item.IsExamined; ps.Quality = item.Quality;
        if (item.IsPainted) { SetColor; PaintType/PaintData; PaintHelper.Set(Custom)PaintType }
        ps.SetMountObjectData(item.MountObjectData);
        group.ItemList.RemoveAt(i);                     // the group object is consumed
    }
}
Inventory.DeleteGroup(group.UID);                       // real call (xref 0x180A6B74C): our hook fires
GameScript.ReinitPartScriptCuller(); fade out; GameMode 0; crane IO enabled again; restore input
```

`PartScript.MountByGroup(bool instantSet)` `0x180FF5110` sets layer 16, `ReplaceShader(0)` and clears dust. It enables the
colliders and calls `Alpha1`, then `UnblockBlockParts(false)` and `IsUnmounted = false`. For every `MountObject` it calls
`SetFullMountPosition`, and it re-enables the `hideWhenUnmontingMounting` and `disableOnUnmount` renderers. There is no
sound, inventory change, XP or `Examine`.

Inventory events: one `DeleteGroup(group.UID)`, the UID of the group chosen in the window. **By then
`group.ItemList` holds only the items that matched no part** (usually none). If the existing `DeleteGroup` prefix
serializes `GetGroup(UId)` for rollback, it captures a group without its items.

`<SwapEngine>d__282` `0x181723DE0`:

1. Logs `"<CarLoader> SwapEngine <old> to <new>"`, calls `DeleteMaterials` and `DeleteShaderBackup` on every part, then
   **`DestroyImmediate(e_engine_h)`** and sets `e_engine_h = null`. It yields one frame.
2. `CreateEngine("garage")`, then `SetEngine()`, and yields one frame.
   - `CreateEngine` uses `Resources.Load(EngineSwap ?: Type)` and **`Instantiate(prefab, root.transform)`**, so the new
     engine is appended as the **last child of `root`**.
   - It sets `e_engine_h` and its name, `EngineData` from stock, and calls `RedoneEnginePartScriptBlocks()` when
     `EngineSwap` is set.
3. For each new engine `PartScript`, it looks for an item by name in the group:
   - item found: the same per-part apply as above (`TunePart`, `SetCondition`, quality, paint; **no `SetMountObjectData`**),
     then `RemoveAt`;
   - no item and not oil drain/check/fill: `HideBySavegame(false)` and layer 16, so the part comes out **unmounted**.
4. `PartScriptCuller` is created or initialised on `root`.

## 3. Per-part hooks and part keys

| Our hook | Out | In (same engine) | Swap |
|----------|-----|------------------|------|
| `PartScript.Hide` / `DoMount` / `ShowMounted` | no | no | no |
| `CarLoader.TakeOffCarPart(string)` / `SwitchCarPart(string)` | no (uses the `SwitchCarPart(CarPart,bool)` overload) | no | no |
| `PartScript.Examine` | **yes, once per unmounted part, 0.5 s after `ActionUnMountGroup`** | no | no |
| `Inventory.AddGroup` | **no (inlined)** | — | — |
| `Inventory.DeleteGroup` | — | yes, with the group UID | yes, after `SwapEngine` finishes |

Part keys (`PartRegistry`, a sibling-index path from `CarLoader.root`):

- **Out and in with the same engine:** nothing is reparented, destroyed or deactivated. Keys are unchanged.
- **Swap:** every engine `PartScript` is a new object, so the registry must be rebuilt. `LoadCar` (`d__215`) creates
  `Chassis, Wheels, Interior, Engine, Driveshaft, Exterior, Parts, BonusParts` in that order. The engine is therefore
  not the last child of `root` after a normal load.
  - `DestroyImmediate` plus `Instantiate(…, root)` moves the engine to the end, and every later sibling (driveshaft,
    exterior, parts, …) shifts down by one index.
  - A client that loads the same car later (save load or join, with `EngineSwap` set before `CreateEngine`) gets the
    normal order again.
  - So **after an in-place swap the sibling-index keys differ between the swapping client and a client that loads the car fresh**.
    Uncertain: whether the driveshaft, exterior and part objects really are direct children of `root`. If they are
    not, only the engine's own index changes.
  - A safer key design for this: key the engine subtree relative to `e_engine_h` (for example `E/<path>`), and key the
    other parts by path while skipping the engine child, or re-baseline the car after a swap.

## 4. EngineSwap

| Where | What |
|-------|------|
| `NotificationCenter.<ActionInsertEngineToCar>d__23` | Swap: `EngineSwap = group.ID` (before `SwapEngine`). Same engine and `group.ID == EngineParams.Type`: `EngineSwap = ""` |
| `CarLoader.<LoadCarFromFile>d__425` `0x18171FD60` | `EngineSwap = carData.engineSwap` (`NewCarData+0x60`; the state machine's `<carData>5__3` is at `+0x1E8`, so the read is `+0x248`), then `StartCoroutine(LoadCar(carToLoad))` |
| `CarLoader.DeleteCar` | Clears `EngineSwap` (and `EngineData`) |
| `CarLoader.CreateEngine(string)` `0x1804E4620` | Reads it: the prefab is `EngineSwap` if not empty, else `Type`. Callers: `LoadCar d__215`, `SwapEngine d__282`, `CarLoaderExtended.LoadCar`, car editor |
| `CarLoader.SaveCarToFile`, `GetEngineName`, `RedoneEnginePartScriptBlocks`, `CreateEngineAsync` | Read it |
| `LoadConfig` | Writes `Position`, `Type` and `Swapoptions` only. It does **not** reset `EngineSwap` |

`SupportsEngineSwap()` is `Swapoptions.Length > 1`, and `CanSwapEngineTo(id)` is `Swapoptions.Contains(id)`.

Script-callable ways to apply a stored swap:

- **Fresh load (preferred):** set `EngineParams.EngineSwap` before `CreateEngine` runs.
  - `LoadCar d__215` calls `DeleteCar` first when `root` exists, and that clears the field. So either set it only when
    `root == null`, as `LoadCarFromFile` does, or, more robustly, set it in a **Harmony prefix on
    `CarLoader.CreateEngine(string)`** from a "pending swap for this loader" table.
  - This gives the same object order as a save load.
  - `EngineParams` is a non-blittable struct field. In the unhollowed API, write it back as a whole
    (`var p = cl.EngineParams; p.EngineSwap = id; cl.EngineParams = p;`) and read it back to check. *Uncertain:
    whether a direct member write persists.*
- **Car already loaded:** do what `d__23` does: set `EngineSwap = id`, then
  `cl.StartCoroutine(cl.SwapEngine(group))`, with `group.ID = id` and **one `Item` per engine part name**. Parts without
  an item come out unmounted. It has no stat, sound or inventory side effects. Afterwards call
  `GameScript.ReinitPartScriptCuller()`, apply bolts (`SetMountObjectData`, which `SwapEngine` skips) and rebuild the
  registry.
  - The `CarLoader.SwapEngine` builder method is real and callable from managed code, even though the game itself
    inlines it.

## 5. Recommended hook points

Names as generated in `Libs\Assembly-CSharp-firstpass.dll`: `NotificationCenter._ActionInsertEngineToCar_d__23`,
`ToolsManager._UseEngineCrane_d__37`, `CarLoader._SwapEngine_d__282`, `PartScript._UnMountByGroup_d__108`.

Do **not** hook `NotificationCenter.ActionInsertEngineToCar`, `CarLoader.SwapEngine` or `ToolsManager.UseEngineCrane`.
Their builders are inlined, so those patches never fire.

Engine out:

| Purpose | Hook |
|---------|------|
| remember the car | prefix `CarLoader.UseEngineCrane()` (`__instance`). The coroutine may still stop at a warning, so do not open the transaction here |
| **open** | **prefix `NotificationCenter.ActionUnMountGroup(InteractiveObject iO)`**: keys = every `PartScript` under `iO.gameObject` (plus the hood, engine cover, clamshell or trunk body keys, already switched one frame earlier), current states |
| **group + commit point** | **postfix `ActionUnMountGroup`**: all `IsUnmounted` flags are already set. The new group is the last entry of `Inventory.GetGroups()` with `ID == iO.gameObject.name`. Record it as the added group explicitly, because the `AddGroup` hook does not fire |
| **finished** | postfix `ToolsManager._UseEngineCrane_d__37.MoveNext` returning `false`. Keep the transaction open until then and absorb the per-part `Examine` postfixes that arrive ~0.5 s later |

Engine in:

| Purpose | Hook |
|---------|------|
| **open** | **prefix `NotificationCenter.InsertEngineToCar(GroupItem engine)`**: deep-copy the group (ID, UID, items), because `ItemList` is consumed. The car is `ToolsMoveManager.Get().GetConnectedCarLoader(14)`. Keys = engine parts; a swap is possible when `engine.ID != e_engine_h.name` |
| commit point | existing prefix `Inventory.DeleteGroup(long)` with `UId == snapshot.UID`. Use the snapshot, not `GetGroup(UId)` |
| swap done | postfix `CarLoader._SwapEngine_d__282.MoveNext` returning `false`: rebuild `PartRegistry`, then read `EngineParams.EngineSwap` for the snapshot |
| **finished / aborted** | postfix `NotificationCenter._ActionInsertEngineToCar_d__23.MoveNext` returning `false`. If no `DeleteGroup` was seen (the `GUI_SwapOnlyUserCar` path), abort the transaction |

Related, outside the crane:

- The engine stand also leaves the inventory hooks blind on one side. `ActionHangOn(group)` tail-jumps into
  `Inventory.DeleteGroup`, so the hook fires there. But `<TakeOffEngineFromStand>d__25` puts the group back with an
  inlined `groups.Add`, so `AddGroup` does not fire.

## To confirm with a runtime trace

1. Engine out on a test car: log the `AddGroup` and `DeleteGroup` prefixes and `ActionUnMountGroup` pre/post, and dump
   `Inventory.GetGroups()` before and after. Expect no `AddGroup` call, one new group, and `IsUnmounted` set on all
   engine parts in the postfix.
2. Log `PartScript.Examine` with timestamps relative to `ActionUnMountGroup` (expect ~0.5 s), and check its order against
   the end of `d__37`.
3. Engine in: in the `DeleteGroup` prefix, log `GetGroup(UId).ItemList.Count` (expect 0, or only the leftovers).
4. Swap: dump `root`'s direct children (name and sibling index) before and after. Then save, reload and dump again.
   This confirms or refutes the key shift.
5. Check that Harmony patches on the `_d__NN.MoveNext` nested types fire under MelonLoader 0.5.7 / Unhollower.
6. Check that a write to `CarLoader.EngineParams.EngineSwap` from managed code persists, using the whole-struct write-back.
