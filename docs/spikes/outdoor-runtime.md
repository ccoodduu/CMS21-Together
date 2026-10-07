# Spike: outdoor scenes at runtime (row 15 `shared-outdoor-scenes`, group 1)

Date: 2026-10-07. Static halves first; the **runtime results** section at the end comes from lane 2 runs the same
day (`outdoor-trace` four times, then `outdoor-junkyard`, `-barn`, `-auction`, `-return`, `-latejoin`; run folders
`tools/runs/20261007-18*_L2_outdoor-*`, instances C and D, headless). The static facts come from the Il2CppDumper dump, the decompiled methods in `native\out\outdoor_clean` and `outdoor2_clean`
(`native-decompile.md`), `xref.py` and the unhollowed `Assembly-CSharp.dll` the client compiles against. The runtime
halves are listed per task under "Open" and are run with `tools/test-env/scenarios/outdoor-trace.ps1` (a two-instance
spike scenario, skipped by `Run-All`) and the verbs in `tools/TestHarness/Features/OutdoorCommands.cs`.

## 1.1 Generator entry, hold point and call order

- `JunkyardGenerator.Start` (0x30 bytes) is only `StartCoroutine(Generate())`; `ShedManager.Start` sets the input
  player and does the same. `Generate()` is the iterator *builder* (returns `IEnumerator`), so a prefix returning false
  on it would have to fake an `Il2CppSystem.Collections.IEnumerator`.
- Both iterators already wait in their own loop right after state 0: `<Generate>d__18` state 0 runs
  `ScreenFader.SetBlack`, `InputManager.ChangeInput(false…)` and starts `GameDataManager.Load`; states 1/2 wait (yield
  `WaitForEndOfFrame`, state 2) until `CarBundleLoader` is ready. `<Generate>d__29` state 0 does the fader and the load;
  states 1/2 wait for `Localization`. No random roll happens before these states.
- **Hold (in code):** a prefix on each `MoveNext` that, in state 1 or 2 while the instance has not arrived, sets
  `__1__state = 2`, `__2__current = null`, returns `true` and skips the original: the vanilla wait loop, one frame per
  call, screen black and input off. No bypass flag and no second call of the builder are needed; the generator runs
  once. The first `MoveNext` after the instance (or after the 15 s timeout) runs vanilla state 1/2.
- Car loops: `d__18` creates car `i` by yielding `CreateCar(i, carSupport, positions[pos], randomCars[i])` (nested
  coroutine `d__19`, `index` = `i`); `d__29` state 9 does the same for `i < 3` with `randomCars[i]` (`d__30`, field
  `cl` for the loader). Both builders take `CarsIdWithConfig` (a class: `CarID`, `ConfigVersion`, `DLC`) by
  reference value, so a `ref` prefix replaces it.
- Unhollowed names used by the hooks: `_Generate_d__18._i_5__11`, `_CreateCar_d__19._carLoader_5__3`,
  `_Generate_d__29._i_5__12`, `_CreateCar_d__30._cl_5__3`, `__1__state`, `__2__current`, `__4__this` (all compile).
- Runtime: see "Runtime results" below.

## 1.2 Reseed, difficulty, barn on the map

- Reseed (first version): a key per `MoveNext` step. Replaced at runtime by one random stream per iterator (see
  "Runtime results").
- `difficulty == 2` in `d__18` state 0xB (barn map) and `d__29` state 10 (loot case) reads
  `DifficultyManager.currentDifficulty` (+0x18, enum `DifficultyLevel`): **2 = Sandbox** (Normal 0, Expert 1,
  Sandbox 2, Easy 3; the mod's `Gamemode` uses the same values). Spike check 10 answered.
- The map's barn availability check was **not found statically**: `GlobalData.get_BarnsAmount` has no native caller
  (inlined into every reader), and `MapWindow` has no barn-named method. D9's fallback stands until a decompile with
  a data xref on the `BarnsAmount` static or a runtime trace finds it.
- Runtime: see "Runtime results" below.

## 1.3 Auction

- Claim hook: `AuctionBidding.StartAuction` (0xCEC9F0) fires through `StartAuctionAction` (a 16-byte `jmp` thunk, a
  `UIDescription` delegate). A prefix returning false cancels the start; calling `StartAuction()` again after the
  grant runs it.
- **`AIBid`, `FinishAuction` and `StopAuction` have no native callers: inlined into `Update`.** Hooks on them never
  fire. `OnStateChange(AuctionState)` is called directly from `StartAuction`, `PlayerBid` and `Update` and fires:
  `AuctionState` BeforeStart 0, AI 1 (after a team bid), Player 2 (after an AI bid), Win 3, Loss 4 (`Update` sets
  `4 - lastBidByPlayer` when `auctionTimer <= 0`). The bid state is sent from an `OnStateChange` postfix plus once a
  second.
- `PlayerBid` and `BidAction` share one body (identical-code folding at 0xCECF70; hooking one hooks both).
  `PlayerBid` only returns early on `auctionFinished` or `lastBidByPlayer`; it has **no money check** (that is
  `OnBidButtonClick`/`PlayerHasMoney`), raises `currentBid` by `bidAmount`, resets `auctionTimer = bidTime` and sets
  `lastBidByPlayer`. So a raise from another player can be applied by calling `PlayerBid()` on the owner's client
  after the server's money check (Q3 not view-only, pending the runtime check).
- Fields: `currentBid` 0xA4, `AIMaxBid` 0xA8, `AINextMove` 0xAC, `bidAmount` (bid step) 0xB0, `bidTime` 0xB4,
  `auctionTimer` 0xB8, `startedAuction` 0xBC, `lastBidByPlayer` 0xBD (team leads), `auctionFinished` 0xBE,
  `state` 0xD4.
- `AuctionManager.normalCarsAmountRange` (0x50) and `salvageCarsAmountRange` (0x78) are private fields of the
  scene's manager, so they are **not readable from the garage**; the generator sends them on arrival
  (`OutdoorDigest.AuctionAmounts`) and the server builds the lots then (and for later instances at once).
- Lot values in vanilla `GenerateCars`: `Rating = AuctionHelper.GetRatingForCar(type)`, `Value =
  CarBundleLoaderExtension.GetCarValue(carBundleLoader, car, version)`, `StartingPrice =
  AuctionHelper.GetStartingPrice(Value, rangeNormal|SalvageStartPriceMod, Rating)`. Every client computes them under
  `Random.InitState(lot seed)` in that order; the generator's values become the server's.
- `AuctionCarData` has a `Sold` flag (0x2C); a closed lot is marked `Sold` in the manager's lists.
- Runtime: see "Runtime results" below.

## 1.4 Piles

- `Junk.ItemsInTrash` is a `List<BaseItem>`: piles can hold `GroupItem`s by type. v1 records only `Item`s and leaves
  group items local (counted in the dump's `outdoor.loot.groupItemsLeftLocal`).
- `BaseItem.UID` is a public field (0x18): a rebuilt `Item` can get the recorded UID (`ToGameItem` sets it).
- `NotificationCenter.MoveItem(Item|GroupItem, bool toWarehouse, string windowType)`: `windowType` comes from the grid
  item's hashtable (not a literal in `FoundTab`/`CollectedTab`); the hooks key on "shared visit with piles" and the
  item's UID, and the dump records the last `windowType` seen. Right click goes `BetterButtonAction.RightClick`.
- `TempInventory.RemoveItem(BaseItem)` and `RemoveItem(long)` are called only by `TakenItemsWindow.OnTrashButtonClick`
  and `RemoveCurrentItemAction`, so one prefix on each overload covers "removed in the taken-items window".
- `ItemsExchangeWindow` has a `Junk` getter and a public `Refresh(bool resetPage)`.
- Keys (in code): the pile's world position rounded to 0.1 m, piles ordered by key, `#n` for duplicate keys; the scan
  uses `Resources.FindObjectsOfTypeAll<Junk>` so inactive `JunkyardRandomShow` piles are included.
- Runtime: see "Runtime results" below.

## 1.5 Catalog

- `CarBundleLoader.GetCarsForScene(SceneType)` is `GetAllAvailableCars()` filtered by `CarIsAvailableOnScene` unless
  `GameSettings.CarsSpawnEverywhere`; nothing depends on the loaded scene, so it can be read in the garage (once the
  bundle loader is ready). Salvage auctions use the junkyard list (scene 1), as vanilla.
- The config's `rarity` key is not read yet (`CatalogEntry.Rarity = -1`); only the optional Lvx selector needs it.
- Runtime: see "Runtime results" below.

## 1.6 LvxBetterCarSpawns

Nothing new statically beyond `outdoor-scenes.md`. **Open (runtime, user's install only):** the behaviour section.

## Runtime results (lane 2, 2026-10-07)

**1.1 Call order and hold.** Junkyard: `d__18` 0 → 1 → 5 (amount, `GetRandomCars`, first `CreateCar`) → 6 per further
car; each `d__19` runs states 0-13 once except state 1 (waiting for the car to load), which repeats 28-45 times and
differs between clients. Hold (`net-delay` on C's incoming packets): 2 s → no hold (the instance arrived before the
generator reached state 1), 5 s → held 0.03-0.42 s, 15 s → held 10.1-10.4 s, then the 15 s timeout made the visit
local and the generator ran vanilla; the scene loader tolerated the 10 s hold (junkyard ready 17-18 s after travel)
and the generator ran once each time. D2's 15 s timeout stays.

**1.2 Reseed determinism: works after three fixes** (commit `7699469`):
- Per-step reseeding with a step key did not give equal cars: the wait loops repeat a machine-dependent number of
  times and some draws happen in a different step on each client (positions and conditions differed, colours not).
- `UnityEngine.Random.State` is unhollowed as a **1-byte struct without its seed words**: `Random.state` get/set copies
  16 bytes into or out of it (garbage, and a stack write). `Reseed` calls `UnityEngine.Random::get_state_Injected` /
  `set_state_Injected` through `IL2CPP.ResolveICall` with a 16-byte struct, and every generator iterator gets its own
  random stream (seeded once from seed, kind and car index, saved and restored around each `MoveNext`), so the
  number of wait steps no longer matters.
- `Helper.GetRandomCars` rerolls duplicates, and the clients' `GetCarsForScene` lists are in different orders, so it
  consumed a different number of draws; in a shared junkyard or barn it now returns the server's picks without drawing.
- `CarLoader.SetRandomColorPanels` (started from `d__19` state 8) runs as its own coroutine with global random; its
  `d__321` iterator gets a stream per car too (part condition sums differed by up to 2 before).

Result: equal digests for every car (id, version, position, colour, missing panels, condition sum, `negotationMod`)
and for the barn layout (`ShedRoot` rotation and children) in every two-client run since (trace, junkyard, barn,
latejoin). *Still open:* a run with different owned cars on the two clients (piles do not depend on it: they are
replayed).

**1.3 Auction.** `normalCarsAmountRange` and `salvageCarsAmountRange` are both (30, 45): 30-41 lots per type. Lot values
computed under the lot seed on the generator; B's list showed the same starting prices. `StartAuction` claim, the
`OnStateChange` relay (B saw A's bid within 0.22-0.24 s) and a raise through `PlayerBid()` from the other client work
(Q3 keeps the raise). Headless only: `OpenNormalAuctions` throws in the UI part after the list is set, and the
bidding GameObject stays inactive so `Update` does not run (the harness drives it). Found: the guard blocked
`Window:CarLocationWindow` after `ReceiveCarAction`, so a won lot could not be bought under `Enforce` (row 6 bug,
fixed in `1fcd164`).

**1.4 Piles.** 46 junkyard piles (about 950 items) and 27-28 barn piles; keys, order and item UIDs equal on C and D,
0 unmatched piles, 0 group items. Takes reach the other client in 0.5 s, put backs in 0.7 s. The harness takes items
without `MoveItem`, so `windowType`, right-click move-all and a refreshed open window stay hand checks
(`docs/playtest.md`). Trace patches on methods with `ref` Il2Cpp generic list arguments (`Junk.AddRandomItems`,
`ShedManager.AddItems`) throw `BadImageFormatException` in MelonLoader 0.5.7 and break the generator: never patch them.

**1.5 Catalog.** In the garage on both clients: junkyard 95 configs / 46 models, barn 101 / 50, normal auction 137 / 72,
salvage 95 / 46; equal on C and D; no DLC owned on the test installs.

**Other.** The auction scene is called `Auctions` (`MapWindow.SubmitPanelAction`); the harness `travel` verb now uses it.
`OutdoorEnter` reaches the server before the barn's `TravelFee`, so the opener's own fee saw an open barn and used no
barn (fixed in `af97277`: the opener's fee is charged once in either order).
