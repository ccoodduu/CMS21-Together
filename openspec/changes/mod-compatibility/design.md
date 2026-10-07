# Design

## Context

See proposal.md — Why. Observed in the code and the game stubs:

- Handshake today (DirectIP): `Server.TcpConnectCallback` assigns a slot and sends a welcome `ConnectPacket`
  (`modVersion = Program.MOD_VERSION`, `gameVersion = ""`); the client's `AuthHandler.HandleConnect` only logs it and
  answers with its own `ConnectPacket` (`gameVersion = ""`, `username = "TestUser{id}"`). Steam: the server sends the
  same welcome from `SteamTransport.OnConnectionChanged`, the client sends its packet from `ClientSteam.OnConnected`.
  The server's `AuthHandler.OnConnected` compares `modVersion` only; on mismatch it sends a `DisconnectPacket` without
  `playerID` (so `0`), which the client's `HandleDisconnect` treats as another player leaving (slot ids start at 1),
  and it never closes the slot. The heartbeat timeout cannot free it either: `Client.Update` checks the timeout only
  once `ConnectionValid`, which a refused client never reaches. (Second review: verified in `AuthHandlers.cs`,
  `Server.cs`, `Client.cs`.)
- `ClientSteam.OnConnected` is the client's own Steam connection callback, so on Steam the client's `ConnectPacket` is
  sent before the server's welcome has been read, with `playerID` still unset. `Client.Disconnect()` on the server
  closes TCP/UDP only; a Steam slot's `SteamConnection` stays open (and `SteamTransport.OnDisconnected` does not call
  `Disconnect` — row 6 task 1.8 fixes that direction). Server packet dispatch runs on the socket/Steam threads under
  `StateLock`; an undeserializable packet ends in the `catch` of `Tcp.HandleData` / `SteamTransport.OnMessage`.
- Row 8 (`hosting-and-join-ui`, M1, lands before this change) appends `ServerFull` and `WrongPassword` to
  `DisconnectReason` (this change appends its three values after them), creates `Server.Refuse` (its task 2.4), owns
  `ConnectionStatus`, `ConnectionMessages.For(reason, detail)`, `Client.ResetAfterFailure()`, the harness status
  field `Status.lastDisconnect { reason, message }` and the harness verb `mp-fake-version` (overrides the sent mod
  version; this change reuses it).
- Packets are serialized with `BinaryFormatter` (`Packet.cs`). A field added to `ConnectPacket` makes an older peer's
  packet fail to deserialize unless the field is `[OptionalField]`.
- Row 7 (`session-persistence-and-rejoin`, group 3, M0) adds `DisconnectPacket.reason` with `DisconnectReason { None,
  ServerShutdown, Kicked, VersionMismatch, DuplicateIdentity, MissingIdentity, SyncFailed }`, shows the reason in the
  menu after a disconnect (its D10), and later (group 4, M5) resolves identity in `OnConnected`.
- Game API (stubs): `GameSettings.BuildVersion` (static string; QoLmod logs it as "Game version 1.0.40" on this
  install); `PlatformManager.GetDLCs()` → `DLC { ProductId, Name, Owned, Index }`, `IsDLCInstalled(int)`,
  `GameManager.PlatformManager`; `GameInventory.PartPropertyList` (`IDictionary<string, PartProperty>`).
- `CMS21-Together-Server/Database/` holds `item_database.json` (Core `PartProperty` incl. `DLC[]`, `HaveDLC`,
  `IsMod`), `garage_upgrade_database.json`, `player_upgrade_database.json`, plus a runtime-written
  `modded_item_database.json` (`RegisterModItem`). The server reads neither `DLC` nor `HaveDLC`.
- Installed mods on the user's main install, decompiled for this draft (patch targets by attribute or
  `harmony.Patch`): QoLmod 4.4 (≈90 classes: `Inventory`, `CarLoader`, `PartScript`, `Warehouse.Delete`,
  `TireChangerLogic`, many windows), TK Aftermarket 0.7.4 (`CarLoader.LoadCar/CreateParts`, `PartScript.Awake`,
  `GameInventory`), TK Basics 0.5.2 (`Inventory.Add/GetBaseItem`, `PartScript.Start`, several `DrawPage`),
  LvxBetterCarSpawns (`JunkyardGenerator`, `ShedManager`, `AuctionManager`, `ItemsExchangeWindow`), QuickShop 1.04
  (postfix on `GameScript.Update`, own `Harmony` instance), LvxOwnedCarsOnly (patches a LvxBetterCarSpawns method),
  CMS21LoadOptimizer (Unity `Texture2D`/`UnityWebRequestTexture`, QoLmod methods, opt-in profiler patching
  `MoveNext` of game coroutines; several own `Harmony` instances), AutosaveMod (no patches; calls
  `GarageLoader.Save(false)` on a timer). The test installs carry only CMS21-Together, TogetherTestHarness and
  CMS21LoadOptimizer.

- Observed on the test installs (task 1.1, `compat-report patches` in run `20261006-101337_L2_compat-refusal`, menu
  and garage): `GameSettings.BuildVersion` = `1.0.40` (in `Assembly-CSharp-firstpass`, like `Inventory`,
  `CarLoader`, `FPSCamera` and every game type the fixtures target), `Application.version` = `1.0`.
  `PlatformManager.GetDLCs()` returns 33 entries in the menu already (same list in the garage), all `Owned = false`
  on these installs; 11 of them have `ProductId = "-1"` (Dodge, Jeep, Tuning, Plymouth, Bentley Remastered, Garage
  Customization, Dodge Modern, Maserati Remastered, Ram, Rims, Chrysler), so product ids are not unique (see
  review.md, Implementation notes). Harmony's registry holds patches by CMS21-Together (40), TogetherTestHarness (12),
  MelonLoader itself (12, on `mscorlib`, `System` and `0Harmony` methods) and CMS21LoadOptimizer (5, on
  `UnityWebRequestTexture.GetTexture`, `DownloadHandlerTexture.GetContent`, `Texture2D.Compress`); the patch owner id
  is the patching assembly's full name. `MelonHandler.Plugins` is empty; `MelonHandler.Mods` lists the three mods.

## Goals / Non-Goals

**Goals:** refuse every mismatch that would corrupt a session before the join sync; a refused player knows what to
fix; the operator can override the classifier without a new build; no change to gameplay code paths.

**Non-Goals:** supporting gameplay mods; disabling or unpatching mods at connect; anti-cheat (a client can lie about
its mods — acceptable for friends); exact matching of visual mods; exporting modded items.

## Decisions

### D1. Check order and where each check runs

```
client                                        server
                                       ←      welcome ConnectPacket {modVersion, protocolHash, playerID}
check welcome (version, protocolHash)
  mismatch → disconnect locally, menu, show reason (VersionMismatch)
ConnectPacket {modVersion, protocolHash,  →    CompatibilityPolicy.Evaluate(packet):
  gameVersion, dlc, mods, (row 7 key)}           1 mod build  2 game version  3 mods  (DLC: recorded only)
                                                fail → DisconnectPacket{playerID = slot, reason, message},
                                                       close the slot after the send is flushed
                                                ok   → (row 7 identity, M5) → OnConnectedSuccessfully
```

- Both sides check the build because either side may fail to read the other's packet when builds differ. The
  client-side check needs no server cooperation, so it also covers an old server.
- One send path: the client sends its `ConnectPacket` only from `AuthHandler.HandleConnect`, after the welcome check,
  for both transports (`ClientSteam.OnConnected` stops sending; DirectIP keeps its UDP connect there). The packet is
  built in one place, `ConnectPacketFactory.Build()`, which rows 6 (name), 7 (`playerKey`), 8 (password, admin key)
  and this change fill. Without this, a Steam client sends before it can check the welcome, and row 7's `playerKey`
  (sent only from `HandleConnect`) would never reach a Steam server.
- A client-side refusal goes through row 8's `ConnectionStatus.Failed(VersionMismatch, detail)` and
  `Client.ResetAfterFailure()` (menu with `saveGame = false`); before row 8 group 2 has landed, through row 7's menu
  display.
- New `ConnectPacket` fields are `[OptionalField]`; a server that receives a packet it cannot deserialize at all
  from an unaccepted slot logs it and refuses with `VersionMismatch` (best effort; the client's own check already
  told the player).
- All four checks run and their findings are collected; the refusal `reason` is the first failing check in the
  order above, and `message` lists every finding (one line each). This avoids a fix-reconnect-fix loop.
- Insertion point for row 7: `OnConnected` calls `CompatibilityPolicy.Evaluate` first; row 7's identity resolution
  (group 4) runs only on success. Both edit `OnConnected`; this change keeps its call a single line at the top.
- The refusal fix: one server helper `Server.Refuse(clientId, reason, message)` — `DisconnectPacket{playerID =
  clientId, reason, message}`, then on the next server tick (main loop, `ServerWindow.TickServer`) `Client.Disconnect()`
  plus `SteamConnection.Close()` for a Steam slot (the send is queued on the same ordered stream; a tick later it has
  been handed to the socket/Steam). The helper is row 8's (task 2.4, lands first in M1, also moves `/kick` to it);
  this change calls it for every compatibility refusal and for an undeserializable `ConnectPacket`. Rejected: closing immediately (the packet can be lost on DirectIP when the socket
  closes first).

### D2. Protocol hash

`ProtocolHash.Compute()` in Core: SHA-256 (first 16 hex chars) over the sorted `PacketTypes` names with their
numeric values, and for every `[NetworkPacket]` type in Core the sorted list of serializable field names and field
type full names (recursing into Core DTO types). Computed once per process. Core is the same assembly on both sides,
so equal hashes mean both read the same wire format. Rejected: a manually bumped protocol number (forgotten on
branches; `PacketTypes` is append-only, so branches differ silently) and the assembly MVID (changes on every build
even without packet changes, which would refuse harmless rebuilds between friends).

### D3. Game version and DLC set

- Client: `GameSettings.BuildVersion`; DLC = `GameManager.PlatformManager.GetDLCs()` where `Owned`, as sorted
  `ProductId` strings (product ids are stable across languages; `Name` is used in messages). Read at connect time,
  not at start (Steam refreshes DLC ownership after start, `RefreshDLCs`).
- Server reference for the game version, in priority order: 1 configured (`game_version = 1.0.40`), 2 database
  (`Database/meta.json` `GameVersion`, from Part 2's exporter), 3 pinned (first accepted client of this run).
  `auto` (the default) skips step 1. A pinned value lives in memory only and is logged; it resets when the server
  restarts. Rejected: storing the pinned value in the save (a host who updates the game would lock themselves out
  of their own save).
- **DLC is never a refusal reason** (user decision 2026-10-06, QUESTIONS.md fifth round). DLC content loads only on
  a client that owns it, so the server keeps the **shared DLC set**: `SharedDlc` (server `Data/SharedDlc.cs`) holds
  each accepted client's reported set and their intersection (empty while nobody is connected). It is updated when
  a client is accepted and when it leaves (`PresenceEvents.Left`), logged when the intersection changes, printed by
  `compat` (intersection and each player's set) and sent to clients in `ServerInfoPacket.SharedDlc`
  (`[OptionalField]`): to the joining client with its `ServerInfo`, and to every connected client when the set
  changes. The client keeps it in `ClientData.ServerInfo.SharedDlc`.
- Blocking DLC content outside the shared set from shared use (DLC cars, parts, tools: spawn, shared inventory,
  parking) belongs to the rows that share them (1, 2, 5a; INTEGRATION.md notes it). Content already in shared use
  when a player without that DLC joins is their problem too (e.g. keep it parked or hidden for that player); this
  change only provides the set. Rejected: refusing a different DLC set (the user's group may buy DLC at different
  times) and a configured DLC reference (`dlc =` key, dropped with the refusal).

### D4. Collecting mods and patch targets (client)

`ModInventory.Collect()` runs when the client builds its `ConnectPacket` (late patches such as LvxOwnedCarsOnly's
`TryPatch(last: true)` are applied by then):

1. Melons: `MelonHandler.Mods` and `MelonHandler.Plugins` → name, version, author, assembly file name, assembly.
2. Patches: for each method in `Harmony.GetAllPatchedMethods()`, `Harmony.GetPatchInfo(m)` gives prefixes,
   postfixes, transpilers and finalizers; each patch's `PatchMethod.DeclaringType.Assembly` is mapped to the melon
   that owns that assembly. Rejected: `MelonBase.HarmonyInstance.GetPatchedMethods()` per mod — misses every mod
   that creates its own `Harmony` (QuickShop, LoadOptimizer), and the `owner` id string is free text.
3. Each target becomes `PatchTarget { Assembly, Type (full name), Method }`. Patches whose method lives in an
   assembly no melon owns are reported under `(unattributed)` and classified like a mod.
4. Excluded: this mod's own assembly (`Assembly.GetExecutingAssembly()`) and the assembly named
   `TogetherTestHarness` (it patches the startup flow).

`ConnectPacket.mods` = `List<ModReport { Name, Version, Author, File, Targets (capped at 200 per mod) }>`.
Classification happens on the server (D5) from the targets, so operator rules apply without a client rebuild; the
client runs the same classifier only for `compat-report` and its own log.

### D5. Classifier (Core `ModClassifier`, rules in data)

Per target:
- Target type's assembly is not `Assembly-CSharp` / `Assembly-CSharp-firstpass` → **ignored** if it is another
  melon's assembly (that mod is judged on its own), **visual** if it is a Unity/engine or system assembly.
- Target in the game's assemblies → **visual** if it matches a visual rule, else **gameplay**, except UI types
  (namespace `CMS.UI*` or name ending in `Window`, `Tab`, `TabData`, `Page`, `PageManager`, `Panel`, `Bar`) whose
  method is not a visual one → **unknown**.
- Visual rules (default set, overridable): types whose name ends in `Camera`, `CameraController`, `Flycam`,
  `AudioController`, `SaveIcon`; `StringExtension.Localize`, `Resources.Load`; UI-type methods named `Draw*`,
  `Redraw*`, `Refresh*`, `Prepare*Description*`, `*Icon*`, `Setup`, `Activate`, `OnCategoryChange`, `FillItem`.

Per mod: any gameplay target → **gameplay**; else any unknown → **unknown**; else **visual** (also when it has no
targets). The report keeps the first five gameplay/unknown targets as the reason.

Applied to the installed mods: QoLmod, TK Aftermarket, TK Basics, LvxBetterCarSpawns, QuickShop → gameplay
(correct); CMS21LoadOptimizer → visual with its profiler off (correct; with the profiler on it patches game
coroutines → gameplay); LvxOwnedCarsOnly → visual (only another mod's method; it requires LvxBetterCarSpawns, which is
refused anyway); AutosaveMod → visual (no patches) — a false negative; its `GarageLoader.Save` calls are covered by
row 7's save guard (see review.md). Rules live in Core (`ModClassifierRules.Default`) and the server can extend them
with `Database/mod_rules.json` (same shape) without a client update.

**Known mods (part 2, 2026-10-07).** `ModClassifierRules.KnownMods` maps a mod name (`MelonInfo.Name`, case-insensitive)
to a class and a short reason; it overrides the heuristic, the reason is what the refused player reads ("QoLmod 4.4
changes inventory, repairs, minigames, shops and more."), and the target list stays in the server log. The server's
`mod_rules.json` can add or replace entries (`"KnownMods": { "Name": { "Class": "Visual", "Reason": "…" } }`);
`mods_ignored`/`mods_gameplay` in `server_config.ini` still win. The refusal lists one line per mod and ends with one
line saying what to do.

Tuning table (task 7.1; the user's main install, runtime `compat-report` in run `20261007-161603_L1_compat-mods-probe`
before, `20261007-162145_L1_compat-mods-probe` after; friends' installs not collected yet):

| Mod (version) | Runtime targets | Heuristic | Right class | Final | Why |
|---|---|---|---|---|---|
| QoLmod 4.4 | 178 | Gameplay | Gameplay | Gameplay (known) | inventory, repair minigames, tire changer, shops, … |
| TK Aftermarket 0.7.4 | 16 | Gameplay | Gameplay | Gameplay (known) | `CarLoader.LoadCar/CreateParts`, `GameInventory` part lists |
| TK Basics 0.5.2 | 13 | Gameplay | Gameplay | Gameplay (known) | `Inventory.Add/GetBaseItem`, `PartScript.Start`, repair groups |
| Lvx Better Car Spawns 1.0.0 | 6 | Gameplay | Gameplay | Gameplay (known) | junkyard, barn and auction generators |
| QuickShop 1.04 | 1 | Gameplay | Gameplay | Gameplay (known) | `GameScript.Update` postfix: buys parts by hotkey |
| CMS21 Load Optimizer 0.5.0 | 31 | Visual | Visual | Visual (known) | Unity texture/asset-bundle methods and other mods' loaders (ignored targets); known so its opt-in profiler (game coroutine `MoveNext`) does not refuse it |
| Lvx Owned Cars Only 1.0.0 | 1 | **Visual** | Gameplay | Gameplay (known) | its only target is a Lvx Better Car Spawns method (another melon's, ignored), but it filters which cars spawn |
| Autosave Mod 1.0.0 | 0 | **Visual** | Gameplay | Gameplay (known) | no patches; calls `GarageLoader.Save(false)` every 5 min and on each completed job |

AutosaveMod is refused rather than allowed. In a session its saves never reach disk (`GarageLoader.Save` ends in
`GameDataManager.Save(4)`, which `SessionGuard` blocks; seen in the probe's client log), so it protects nothing and
the server saves the session anyway. But each trigger still runs the whole save routine on that one client at a time
nobody chose: every car is written into the in-memory profile, `GetGroupOnEngineStand()` replaces the engine stand's
items with new UIDs, the player position is written, and the save icon says "Autosaving" although nothing is saved.
A host who wants it anyway lists it in `mods_ignored`.

No mod applied extra patches between the menu and the garage (the probe's garage report equals the menu report), so
collecting at connect time is enough for these mods.

Rejected alternatives: a fixed list of known mods only (every new mod is unknown); classifying by mod name or
description (meaningless); a pure gameplay-type list ("`Inventory`, `CarLoader`, `GlobalData`") — misses the long
tail of game classes and is the wrong default for a mod check that must be safe.

### D6. Server policy and config

`server_config.ini` gains (defaults in brackets):

```
game_version = auto          # auto | <BuildVersion>
mods_required =              # <name>[@<version>], comma separated
mods_ignored =               # names treated as visual
mods_gameplay =              # names treated as gameplay (e.g. a mod that changes saves without patches)
```

Mod names compare case-insensitively on `MelonInfo.Name`. Policy: let `G` = the client's gameplay + unknown mods
after the lists are applied. Refuse (`ModMismatch`) if `G` contains a name not in `mods_required`, or a required name
is absent, or a required `@version` differs. Ignored mods are logged as "ignored by configuration". Every connect logs
the full classified list at info level; `compat` prints the reference game version (with its source: configured /
database / pinned), the shared DLC set with each connected player's set, the lists and the last ten refusals.

### D7. What the player sees

The reason and message travel in row 7's `DisconnectPacket.reason`/`message`. Row 8 part 1 lands before this change
in M1 and owns the display (`ConnectionMessages.For(reason, detail)`, shown once in the menu); this change adds its
texts there — `GameVersionMismatch`, `ModMismatch` and the `VersionMismatch` wording for the protocol
hash (row 8's `VersionMismatch` text already names both mod versions from `message`). If row 8 group 2 has not landed,
the texts go into row 7's menu display and row 8 moves them. Example:

```
Can't join: gameplay mods differ.
  QoLmod 4.4 changes gameplay (Inventory.Add, CarLoader.LoadCar, …) — remove it or ask the host to allow it.
```

### D8. Part 2 — exporter and tuning

- `Tools/DatabaseExporter/` in the client, compiled in every build but reachable only through the harness verb
  `db-export <dir>` and a MelonPreferences flag `CMS21Together.EnableDevTools` (default off) that binds it to `CMS21Together.DbExportHotkey` (default unbound). It needs the
  menu or garage loaded (`GameInventory.PartPropertyListLoaded`), refuses while connected and refuses when D5 finds
  a gameplay mod (so modded items never enter the base database).
- Tables: `IDatabaseTable { FileName; object Export(); }`; this change implements `item_database.json` (from
  `GameInventory.PartPropertyList`, mapped to Core `PartProperty`, `IsMod = false`), `garage_upgrade_database.json`
  and `player_upgrade_database.json` (sources found by task 6.1), then writes `meta.json { GameVersion,
  ExporterVersion, ExportedAtUtc, Tables[] }`. Row 16 adds its tables as more `IDatabaseTable`s.
- Verification is a diff against the committed files (same game version): equal apart from `HaveDLC` (machine
  dependent; the server never reads it).
- Tuning: collect `compat-report` output (or the server's connect log) from the user's main install and each
  friend's install, record misclassifications in design.md's table, adjust the default rules and add a
  `KnownMods` table (name → class) in Core for popular mods.
- Timing: the exporter is needed by row 16 (prices, M3), earlier than M6. The exporter group (6) can land
  independently whenever row 16 starts; only the tuning group (7) is M6. See review.md.

### What the server stores vs relays

Stores nothing new in the save. Keeps in memory: the pinned game version for the run, the DLC set of each connected
player and the last ten refusals. Reads config and `Database/meta.json`/`mod_rules.json`. Relays the shared DLC set
(`ServerInfo.SharedDlc`) to every client.

### Late join

The checks run on every connect, so a late joiner and a rejoining player go through the same path; a client that
passes then enters row 7's join sync unchanged. A pinned reference survives as long as the server runs, even if every
player leaves.

## Risks / Trade-offs

- [Classifier refuses a harmless visual mod] → it reports unknown/gameplay with targets; the host adds it to
  `mods_ignored`; group 7 tunes the defaults.
- [Classifier misses a gameplay mod without patches (AutosaveMod) or with direct calls only] → `KnownMods`, `mods_gameplay`;
  row 7's save guard blocks game saves; row 14's reconciliation catches resulting desyncs.
- [The user's own main install has five gameplay mods] → they are refused like anyone else; the user plays from an
  install without them (question for the user in review.md).
- [`GetAllPatchedMethods` misses patches applied after connect] → mods patching on scene load are rare; LoadOptimizer's
  profiler is the one known case and is off by default. Re-checking later is not planned.
- [BinaryFormatter cannot read a newer/older `ConnectPacket`] → `[OptionalField]` + the client-side welcome check.
- [`GameSettings.BuildVersion` empty or not the Steam build id] → task 2.1 logs it on both installs; fallback
  `Application.version`.
- [DLC list not ready at connect (Steam refresh)] → read at connect; task 1.1 confirms `Owned` is set in the menu.
- [Shared DLC content already in use when a player without that DLC joins] → the owning rows (1, 2, 5a) decide what
  that player sees; until they land, the M1 guard blocks working on cars, so no DLC car is shared in M1.

## Migration Plan

Client and server ship together. Old clients connecting to a new server are refused with `VersionMismatch` (old
clients cannot show the reason text; they see the old behaviour). Existing `server_config.ini` files get the new keys
with defaults on first start (append if missing). Rollback: remove the keys; an old server ignores them.

## Open Questions

1. Exact source tables for the garage and player upgrade exports (task 6.1 finds them; the schema is fixed).
2. (Closed by the DLC decision: DLC is tracked, not pinned, so `meta.json` holds no DLC set.)
