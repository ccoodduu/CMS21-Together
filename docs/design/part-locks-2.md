# Design note: locks visible before the click (ROADMAP row 24 `part-locks-2`)

Size M, no OpenSpec proposal. Follow-up of row 18 `part-locks` (open question 7, tasks group 12): row 18 refuses a
click on a part, item or car another player works on, and shows the lock on hover (no highlight, label). This row adds
the visual layer in the three places row 18 left: the mount-mode previews, the item chooser and the car pie. Nothing
here changes what is allowed; every click stays refused by row 18's gate, so a piece that misses a case costs a
refusal message, never a race.

Static answers come from Il2CppDumper's `dump.cs` and Ghidra decompiles in
`%USERPROFILE%\CMS21-TestInstalls\native\out\locks3_clean`, `locks4_clean`, `locks5_clean`, `locks6_clean` (targets
`native/work/targets/locks2b.txt` … `locks2e.txt`). Runtime proof is the scenario `locks-select-2`.

## Spike

**Mount-mode previews.** Entering `PartSelectMount` (13) runs `GameScript.HideAllUnMounted` and then
`GameScript.PrepareItemsToMount` (`GameMode.SetCurrentMode` case 0xd). `HideAllUnMounted` calls
`PartScript.HidePreview` on every unmounted part of the car under `IOMouseOverCarLoader` that sits on layer 16 (`Part`)
or 26: colliders off, preview shader, layer 26, `Alpha0`. `PrepareItemsToMount` then calls `PartScript.Show()` on each
unmounted part whose blockers (`unblockOnUnmount`) are all mounted: `Alpha1`, layer 16, colliders on, also for its
`unmountWith` members. That ghost is the "you can mount here" preview. `PartScript.ShowPreviewToMount` has no caller
(dead), `ShowPreview`/`ShowGroupPreview` belong to the garage and group modes.

- `PrepareItemsToMount` (1344 bytes) is a real call from `SetCurrentMode`, `PartScript.<DoMount>d__151` and
  `PartScript.<Hide>d__159` (after every mount and unmount), so the game itself re-runs it; it is not inlined and takes
  no struct by value.
- `PartScript.Show()` is not hooked: `DoMount` and `FastMount` call it for the real mount, also when a remote mount is
  applied, and skipping it there would break row 1.
- `HidePreview` puts a slot exactly in the state every unmounted part has outside mount mode, so a later mount (own or
  remote) starts from a state the game already handles.

**Item chooser.** `ChoosePartUpWindow.Show(List<BaseItem>, type)` turns the list into `ChoosePartDownItem`s and hands
them to its `ChoosePartDownWindow`; `ChoosePartPageManager.DrawPage` and `RedrawCurrentPage` fill the rows
(`InventoryItem`, `ID` = item UID) with `ItemHelper.FillChoosePartDownItem` (items) or `FillInventoryItem` (groups).
Every row has a lock overlay: `InventoryItem.Locked` (a `GameObject`) and `LockedText`, which the game shows for the
repair table's level requirement (`ChoosePartDownItem.IsLocked`, text `GUI_UpgradeRenovatorReq`). The chooser's accept
does not look at `IsLocked`. The single-item accept (`NotificationCenter.NewButtonAccept`, "SelectItem") hides the
window first and then calls `GameScript.SelectPartToMount`, so a refused pick always finds the window closed.

**Pie.** `PieMenuController.PrepareIcons(string[])` computes each element's `IsAvailable` as
`options[id].Enabled && options[id].IsAvailable()` and draws the lock icon (`lockers`) for an unavailable element;
`CheckSelectedOption` ignores a click on an unavailable element. The car pie's move submenu is the ini entry
`!ChangeCarPosition`; its options read the car under the cursor (`GameScript.GetIOMouseOverCarLoader2`) and map to
`CarPlace` by name (`move_entrance1..3`, `move_carLift1/2`, `move_paintshop`, `move_dyno`, `move_pathTest`,
`move_carWash`). The row 14a guard toggled `Enabled` in a `PrepareIcons` **postfix**, after the elements were
computed, so its lock icon only showed from the second opening of a menu.

## Design

**Previews (`LockPreviews`).** A postfix on `GameScript.PrepareItemsToMount` hides, in mount mode only, every
unmounted slot on layer 16 whose `LockSelection.BlockedMessage` is set (another player's lock on it or on what it
belongs to, or the car is moving) with the game's own `HidePreview`. On a lock change of the car in front of the
player while in mount mode: a new lock hides the slots it blocks; a release re-runs `PrepareItemsToMount` (the same call
the game makes after each mount), which shows the free slots again and the postfix hides the still-blocked ones. Own
locks are exempt, as in row 18. A hidden slot has no collider, so it gets no hover label either; the label stays on
the parts row 18 covers (mounted parts, unmount mode).

**Chooser (`LockChooser`).** Postfixes on `ChoosePartPageManager.DrawPage` and `RedrawCurrentPage`, only for the
`ChoosePartUpWindow`'s own down window: a row whose item (or group) UID is in another player's item lock
(`CarLockMirror.ItemHolder`) shows the game's lock overlay with "<name> is mounting this"; a row marked earlier is
set back to the game's own `IsLocked`. The rows are marked, not left out: the list keeps its order and size while
locks come and go, and the pick of a marked item is still refused by row 18. A lock change that carries items
redraws an open chooser (`DrawPage`). After an item step is refused (on the own game or by the server), row 18 ends
the slot lock; three frames later, if the slot is still empty, the game is back in mount mode and no chooser is open,
the chooser opens again through row 18's gate (`LockHooks.MountAction`, a new slot lock), so the player sees the
message and the list again instead of an empty view.

**Pie (`PieOptionState`, `LockPie`).** One owner of pie option state, `Guard/PieOptionState`, runs as a
`PrepareIcons` **prefix**: for each option of the menu it asks its sources in order (the guard first, then the locks)
and disables the option with `SetEnableOption(id, false)` while any source names a reason, remembering the value it
found; when no source blocks any more it restores that value. The guard's former postfix and its own dictionary are
gone, so the two can never restore each other's values, and guard blocks now show on the first opening too.
`LockPie` blocks the move options (`move_car`, the ten `move_*` places, `move_parking`) and `car_drive` when the car
under the cursor is moving or another player holds any lock on it (`LockSets.ForCar` against the mirror, row 13's away
claim included), and a move option whose target place holds another car with such a lock (the swap). A click on an
option disabled by the locks shows the lock message (the guard's click keeps its notice). `GetIsAvailable` is not
wrapped (it returns an IL2CPP `Func<bool>`).

## Not covered

- **Lifts.** The lift is driven by the lifter's buttons (`CarLifter.Action` through `GameScript.ClickIO`), not by a pie
  option; a press on a locked car is refused by row 18 with the message. No pre-click mark.
- **An open pie** keeps the state it was drawn with until it is opened again; a lock that arrives meanwhile is refused
  at the click by row 18, a lock released meanwhile leaves a stale lock icon until the next opening.
- **Group mode** (`GroupMount`, `ShowAllUnMountedGroups`) and the body and interior assembly modes are not changed;
  row 18's hover label covers them.
- **Items that left the inventory** while a chooser is open (another player mounted them) stay listed as the game left
  them; row 18 handles the pick ("item already gone").
