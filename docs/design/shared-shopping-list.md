# Design: shared shopping list

ROADMAP row 23 `shared-shopping-list` (user wish 2026-10-08), size S. One shopping list for the whole group instead of
one per player in the local profile.

## The game's list

- `CMS.UI.Windows.ShopListWindow` (Assembly-CSharp-firstpass) keeps the list in `items`
  (`List<ShopListItemData>`, offset 0x68). `ShopListItemData { string ID; int Amount; ShopListItemDataEx AdditionalData }`
  and `ShopListItemDataEx { string LicensePlateName; bool LicensePlate, Tire, Rim; int Width, Size, Profile, ET }` are
  both **structs**; `Ex.Equals` compares every field, including `LicensePlateName` (null and "" differ).
- `Load()` sets `items = ProfileData.shopListItems.ToList()` (profile + 0x100); `Save()` writes it back. Callers:
  `GarageLoader.<Load>d__14`, `JunkyardGenerator.<Generate>d__18`, `ShedManager.<Generate>d__29`, the mod's own
  `LoaderAddition` (garage) and `NotificationCenter.<SelectSceneToLoad>d__34` (`Save`).
- **Add:** the only caller of `ShopListWindow.AddToShopList(string, string, ShopListItemDataEx)` is
  `UIManager.AddToShopList` (same signature), which returns unless `GameScript.CurrentSceneType == Garage` and refuses
  oil drain/check/fill items. Its callers are `GameScript.HandlePlayerInput` (the add key, every frame),
  `PieMenuController.<GetOnClick>b__72_15` (pie option `list_add`) and `NotificationCenter.NewButtonAccept`. An add
  bumps an existing entry in place (capped at 99) or appends a new one, shows a popup and plays a sound.
- **Remove:** `RemoveFromShopList(string, ShopListItemDataEx, bool entireStack)` is called by `OnTrashButtonClick(x, y)`
  and `RemoveCurrentItemStackAction()`, both with `entireStack = true` and followed by `FillItems()`.
- **Clear:** `ClearShopList()` has no callers; its body is inlined into `ClearShopListAction()`.

Decompiles: `%USERPROFILE%\CMS21-TestInstalls\native\out\shoplist_clean` (targets `work\targets\shoplist.txt`).

## Decisions

1. **Detect changes by watching the list, not by patching.** Every add goes through `UIManager.AddToShopList` and
   `ShopListWindow.AddToShopList`, which take the non-blittable `ShopListItemDataEx` by value (not patched, see
   `docs/spikes/car-details.md`); their callers include a per-frame input method; clear is inlined. Instead
   `ShopListSync.Update` checks the window's `items` object and its `List._version` every 0.1 s. A new version on the
   same list object is a local change: the client diffs it against the last known list and sends `ShopListChange`. A
   different list object (the game's `Load` from the profile, a new scene's window, no list yet) is not a change: the
   client puts the server's list in. No Harmony patch is needed, and every path (keys, pie menu, notifications,
   trash, clear, other mods) is caught.
2. **The server keeps the list** (`ShopListService`, save section `shop-list` v1, `ModGameState.ShopListState`) and
   applies changes the way the game does: an amount change bumps an entry in place or appends it, capped at 99; a
   removed stack is removed whatever its amount (the trash button removes whole stacks). At most 500 entries.
3. **Changes are deltas, not lists.** `ShopListChange { ClientSeq, Removed (whole stacks), Deltas (signed amounts) }`.
   Two players changing different entries at the same time both keep their change. A clear is sent as "remove every
   stack I saw", so an entry another player added at the same moment survives.
4. **Every change is answered with the whole list.** After a change the server sends `ShopListState { Entries,
   Revision, SourcePlayer, SourceSeq, Refused }` to every client; a change that alters nothing (everything refused)
   goes to the acting client only. A refused part (a stack that is no longer on the list, more than 99, a full list)
   is named in `Refused`, logged on the server and on the acting client, and the client's list follows the server.
   Nothing is ignored silently.
5. **No echo, no flicker.** The acting client changed its own list already (the game did it) and only sends the
   delta. When it applies a server list it records that list object and version as known, so the watcher does not
   see it as a local change (`ShopListSync.IsApplying` guards the apply). While its own changes are unanswered it
   keeps the server lists in the mirror and applies only the one that answers its last change, so its list does not
   jump back and forth.
6. **Snapshot and rejoin.** `ShopListSection` is a snapshot provider (`SyncOrder.ShopList` 420, one item). The client
   stores the list and puts it into the window when there is one; the garage reload, the junkyard and the barn load
   the local profile's list first, and the watcher replaces it on the next poll. The list lives in the server save, so
   it survives a server restart.
7. **Applying** builds a new `Il2CppSystem.Collections.Generic.List<ShopListItemData>` (List.Add copies the struct
   with the game's write barriers) and sets `ShopListWindow.items`; if the window is open, `FillItems()` redraws it.
   The game's own `Save()` then carries it into the session profile on scene changes (session saves are blocked).

## Proof

Scenario `shopping-list` (areas `shoplist`, `persistence`), harness verbs `shoplist` (game list, mirror, outstanding,
`windowManagerSame`), `shoplist-add <id> [tire|rim] [width=] [size=] [profile=] [et=] [plate=] [bonus=]` (through
`UIManager.AddToShopList`), `shoplist-remove <id> …` (`RemoveFromShopList(…, entireStack: true)` like the trash
button) and `shoplist-clear` (`ClearShopList`); server command `shoplist`.

## Open

- Local changes made in the 0.1 s before a scene change or before the join's snapshot is acknowledged are not sent.
- No digest for the list yet (row 14's desync check does not cover it); F7 resync resends it.
- An open shopping-list window is redrawn with `FillItems()` when another player's change arrives; the selected cell
  may move.
