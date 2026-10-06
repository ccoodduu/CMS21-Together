# Spike: server-hosted generator client (desk research)

Roadmap: "Early spikes" → server-hosted generator client; QUESTIONS.md "Decide later". Feeds rows 3, 15 and 16.
Date: 2026-10-06. **Static and web research only. The game was not launched.** Order payout and `OrderGenerator`
internals are covered in `orders-and-jobs.md`, so this file only covers what matters for running the generator
headless.

**Short answer:** a headless run (`-batchmode -nographics`) looks **plausible but unproven**. HDRP 10.x turns
itself off on the null device instead of crashing, `WaitForEndOfFrame` works in a batch-mode *player*, and the
generators are ordinary `Update`/coroutine code. The blockers are not graphics but **Steam** (the game cannot start
without a running, logged-in Steam client) and **scope**. The generator does not roll per-part damage when an order
appears. That roll happens when a car is *loaded* (`TakeJob` → `PrepareJob`), and a junkyard or barn layout is only
made by *entering* that scene. One instance cannot be in the garage and the junkyard at the same time.

## 1. Can a Unity 2020.3 IL2CPP HDRP Windows player run with `-batchmode` / `-nographics`?

Facts:

- Both flags are player options. `-batchmode` means "doesn't display anything or accept user input". With
  `-nographics`, "Unity doesn't initialize a graphics device", and the docs add that "output logs are turned off in
  this mode", so pass `-logFile <path>` explicitly. [Unity 2020.3 player CLI][cli]
- The game is Unity **2020.3.49f1** on D3D11 (`Player.log`: "Initialize engine version: 2020.3.49f1", "Direct3D 11.0
  [level 11.1]"). The exe has no SteamStub `.bind` section, so there is no DRM wrapper. Sections: `.text .rdata
  .data .pdata _RDATA .rsrc .reloc`.
- **HDRP on the null device** (HDRP 10.x, the 2020.3 line): the `HDRenderPipeline` constructor calls
  `CheckAPIValidity()`. In a player that is `HDUtils.IsSupportedGraphicDevice(SystemInfo.graphicsDeviceType)`, which
  lists only D3D11/12, Vulkan, Metal and consoles, and `SystemInfo.supportsComputeShaders`. On failure it logs a
  notification and sets `m_ValidAPI = false`, and `Render()` then returns at once. So HDRP **does not render and does
  not crash**. [HDRenderPipeline.cs 10.x][hdrp-ctor], [HDUtils.cs 10.x][hdutils]
- Coroutines in a batch-mode **standalone player**: all yield types work, including `WaitForEndOfFrame`. Only the
  *Editor* in batch mode lacks it. [Unity 2020.3 coroutines in batch mode][coro] The game's generators yield
  `CMS_YieldInstructions.WaitForEndOfFrame` (see §4), so this matters.
- Physics (PhysX) does not depend on the graphics device. `PrepareJob` → `PreparePart` uses
  `Physics.OverlapSphereNonAlloc` (native-decompile.md §e), which should work. Not verified.
- Frame rate: a standalone player runs "at the maximum achievable frame rate" by default
  ([targetFrameRate][tfr]). In batch mode there is no vsync, so one core spins unless the mod sets
  `Application.targetFrameRate`. `Time.deltaTime` is clamped by `Time.maximumDeltaTime` (default 0.33 s), so below
  about 3 fps game time runs slower than real time. That affects `OrderGenerator`'s 30 s timer.
- Known pitfalls in the issue tracker: `SkyManager` cannot build the ambient probe without a device
  ([issue][sky]), and there are old "null device doesn't support linear rendering" reports for `-nographics`
  ([search hit][lin], the page returned 502). Expect log noise or NREs in game code that touches `RenderTexture`,
  `Camera` or `ReadPixels`. One NRE already happens with graphics on: `ToolsManager.CreateRenderCameraTexture` in an
  old `Player-prev.log` under `tools/runs`.
- Asset loading: `Resources.Load`, AssetBundles and scene loading do not need a GPU. With the null device, GPU
  uploads are skipped. Whether that lowers RAM is **unknown** and has to be measured. The car models still load,
  because the generators instantiate real cars (`CarLoader.LoadCar`).
- Is a window needed for loading? The LoadOptimizer work (memory note, 2026-09-24) found that post-scene car loading
  only advances on *rendered* frames (OnDemandRendering made garage loading 19 → 46 s). In batch mode every
  player-loop frame counts as rendered (rendering is simply a no-op), so this should not stall. Measure it.

Precedents (headless dedicated servers made from the game client):

- Valheim and 7 Days to Die ship their dedicated servers as the Unity game run with `-batchmode -nographics`
  ([Valheim example][valheim]). They were built for it.
- **Nebula (Dyson Sphere Program, BepInEx mod)** runs the normal game client as a headless server with
  `DSPGAME.exe -batchmode -nographics -nebula-server -load save1`. It needs Steam logged in, recommends **Steam
  Offline Mode** for single-account owners, and places `steam_appid.txt`. It caps logic rate with `-ups`.
  [Nebula wiki][nebula]
- **Schedule I DedicatedServerMod (MelonLoader)**: an empty server "already uses about 2.6 GB RAM and 200 % CPU
  (two fully saturated cores)". That is a useful warning about CPU when frames are not capped. [xgamingserver][s1]
- Atlyss has a Thunderstore dedicated-server mod of the same kind ([thunderstore][atlyss]).

## 2. If headless fails: the cheap-window fallback

A MelonLoader feature (in the harness or the generator role) can do these things, in order of payoff:

| Measure | How | Expected saving |
|---------|-----|-----------------|
| Stop rendering | after the scene is ready: every `Camera.enabled = false` (or `cullingMask = 0`). HDRP `Render()` returns early when `cameras.Length == 0` ([source][hdrp-render]) | most GPU time and most CPU render-thread time |
| Cap frames | `QualitySettings.vSyncCount = 0` + `Application.targetFrameRate = 10..20` **only after loading**. Leave loading uncapped, because loading is frame-bound (LoadOptimizer's `BoostLoadingScreens` does the same) | CPU from one pegged core down to a few % |
| Tiny window | `-screen-fullscreen 0 -screen-width 320 -screen-height 180`, as Run-Session already does with 960x540; `-popupwindow`; optionally `ShowWindow(SW_HIDE/SW_MINIMIZE)` | HDRP render targets scale with resolution (VRAM and the driver's commit) |
| Lowest quality | `-screen-quality` or `QualitySettings.SetQualityLevel(0)`, `masterTextureLimit = 2..3` (only affects textures loaded later), shadows off | VRAM, and part of the D3D-side commit |
| Keep running | `Application.runInBackground = true`. CMS21 pauses without focus; the harness already forces this in `HarnessMod.OnUpdate` | needed, otherwise nothing runs |
| Mute | `AudioListener.volume = 0` (harness `--harness.mute`) | small |

Nobody has measured what this saves for CMS21. Our numbers: about 8–9 GB commit while loading and about 1–2 GB
working set in the garage per instance. The commit spike most likely comes from asset and bundle loading plus D3D
staging copies. A smaller window and rendering off should mostly cut GPU, VRAM and CPU, not the loading commit.
Only `-nographics` can show whether the commit drops. **Measure both** (§5).

## 3. Steam

Static evidence from `dump.cs` and decompiled code:

- `SteamManager.Awake` is the standard Steamworks.NET code. It calls
  `SteamAPI.RestartAppIfNecessary(GameSettings.AppID)` and quits if that returns true, then calls `SteamAPI.Init()`.
  If `Init` fails it only logs `[Steamworks.NET] SteamAPI_Init() failed…` and returns without quitting.
- But `PCPlatform.<Init>` adds `SteamManager` and **immediately** calls `SteamUtils.IsSteamRunningOnSteamDeck` and
  `IsSteamInBigPictureMode` through `InteropHelp.TestIfAvailableClient()`. Steamworks.NET throws
  `InvalidOperationException("Steamworks is not initialized.")` there when Init failed. After that it adds
  `SteamAchievements`, `SteamDLC` (`SteamApps.BIsDlcInstalled` per DLC), `SteamPresence`, `SteamSave`
  and `SteamSettings`. `PCPlatform` is the only `Platform` implementation and `SteamSave` the only `BaseSave`.
  So **without a running, logged-in Steam client the platform init coroutine most likely throws and the game never
  becomes ready**. Inferred from code, not tested.
- Valve: `RestartAppIfNecessary` returns false whenever `steam_appid.txt` is present. `SteamAPI_Init` fails if the
  Steam client is not running, the user has no licence, or Steam runs as another OS user. [Steamworks API docs][steamapi]
- What the test installs do: `Setup-TestInstalls.ps1` writes `steam_appid.txt` = `1190000` (so a direct exe launch
  is not bounced through Steam), removes `single-instance=` from `boot.config`, and patches the company name in
  `globalgamemanagers`. They still use the running Steam client and the same account, and that works for several
  instances on one PC.
- `KickingOtherSession` happens when the same account is in game on *another* PC. The workaround we already use:
  put the generator PC's Steam in **Offline Mode** (`loginusers.vdf WantsOfflineMode=1`, see the LoadOptimizer
  notes). Nebula recommends the same. In offline mode `SteamAPI_Init` and the DLC checks keep working from cached
  licences. A second account with its own licence also works.
- A launch with no Steam at all would need a mod that skips `SteamManager` and `PCPlatform`'s Steam calls, or a
  Steam emulator. That is DRM circumvention: **not recommended, and not to be published**.
- Side effect: the generator's car pool follows the **generator account's DLCs**. `GenerateNewJob` uses
  `Helper.CanGenerateDLCCar`/`GetRandomDLCCar`, and the junkyard and barn use `Helper.AddRandomDLCCar`. Players
  without that DLC could receive orders for cars they cannot load. That is already an issue for row 3/15, but the
  generator makes it the server owner's DLCs.

## 4. What the generator would have to run without a player (static, from `dump.cs` + Ghidra)

| System | Trigger | Needs |
|--------|---------|-------|
| `OrderGenerator.Update` (`0x180C5B920`) | **Update-driven**: only when `NotificationCenter.IsGameReady && GameSettings.CanGenerateOrders`. While `GlobalData.Jobs < GetMaxOrdersAmount()`, `orderTimer += Time.deltaTime`. When it passes `nextOrderTime` it calls `GenerateNewJob()` and resets (`nextOrderTime = 30`). Also starts the next story mission when `CurrentMissionDone` | the garage scene loaded and ready; game time running (frame-rate independent, but see the `maximumDeltaTime` clamp) |
| `GenerateNewJob` | from the timer | data only: car id/config (`CarBundleLoader`), colour HSV, mileage, global condition, tasks, XP. **No car is loaded.** It calls `UIManager.UpdateJobs` (UI exists headless too) |
| Per-part damage, fluids, rust, dents, licence plate | **when the job is taken**: `OrderGenerator.<TakeJob>` → `CarLoader.LoadCar`, `PlaceAtPosition`, `PrepareJob` (native-decompile.md §e) | a free garage `CarLoader` place, the car's bundle and colliders loaded (`Physics.OverlapSphereNonAlloc`). The generator would have to take the job itself, then upload the baseline and delete the car |
| `JunkyardGenerator` | **scene-load driven**: `Awake` sets `IsGameReady = false`; `Start` → `StartCoroutine(Generate())` (yields `WaitForEndOfFrame`, `Helper.GetRandomCars`, `CreateCar` per slot); `Update` regenerates in place when the serialized flag `GenerateNewJunkyard` is set (deletes all `CarLoader`s, restarts `Generate`) | traveling to the Junkyard scene. The flag allows a re-roll **without reloading the scene**, which a mod can set |
| `ShedManager` (barn) | same pattern: `Start` → `Generate()`, `Update` regenerates when `GenerateNewShed`. It also calls `PlatformManager.IncrementStat`/`SetPresence`, `Camera.main`, and `FPSInputController.SetCharacterControllerPosition` | the Barn scene. `Camera.main` must not be null: keep the camera object and only disable rendering |
| Loose items | `Junk.AddRandomItems`/`AddSpecialMap`/`AddSpecialCase` during the scene generators | the scene |
| Prices | `Helper.GetPrice*` are pure formulas (native-decompile.md table) | nothing: porting is cheaper than asking a game instance |

All rolls use the global `UnityEngine.Random`. A generator instance in the garage cannot produce a junkyard at the
same moment, because travel unloads the garage and its `OrderGenerator`. A generator would therefore need a
schedule (garage by default, short trips on demand) or two instances (2× RAM).

## 5. Spike plan (one session, about 1.5–2 h, unattended except the Steam step)

Setup: a third test install `G` (`Setup-TestInstalls.ps1 -Instances G`, own company name), with the harness and
LoadOptimizer deployed. Add a small harness feature `gen-probe` that writes `jobs.json` (`GlobalData.Jobs`, the
`OrderGenerator.Jobs` list with car id, colour and condition), a `junkyard.json` dump (CarLoader id, car id, colour,
global condition per slot), and applies the §2 "cheap window" settings when started with `--harness.lowfx`.
Sampler: `Get-Process` every 2 s → `PagedMemorySize64` (private commit), `PeakPagedMemorySize64`, `WorkingSet64`,
`PeakWorkingSet64`, `TotalProcessorTime`. Add `Get-Counter '\GPU Process Memory(pid_<id>*)\Dedicated Usage'` and
`'\GPU Engine(pid_<id>*)\Utilization Percentage'`.

Run each variant separately (A = today's harness 960x540 as the baseline; B = `-batchmode -nographics -logFile
<run>\G.log`; C = `-batchmode` only; D = 320x180 + `--harness.lowfx`). Record for each:

1. Reaches Menu / Garage? Time from process start to harness `scene=Garage ready`. Errors in `G.log` and
   `MelonLoader\Latest.log` (HDRP "not supported" message, NREs, exceptions in `PCPlatform.Init`).
2. Peak commit and peak working set while loading. Working set and commit after 2 min idle in the garage.
3. CPU % idle in the garage (with and without `targetFrameRate = 15`). GPU memory and utilization (B should show 0).
4. Orders: let it idle for 4 min. Expect a new job about every 30 s up to `GetMaxOrdersAmount()`. Compare with A.
5. Damage: `TakeJob` one order through the harness, dump parts (`car-dump`), check that conditions are not all 1.0
   (proves `PrepareJob` and physics ran), then delete the car. Time it.
6. Junkyard: travel G to the junkyard, record load time and RAM, dump the layout, set `GenerateNewJunkyard = true`,
   dump again (re-roll without reload). Barn the same way if level and money allow (`ShedManager` uses
   `Camera.main`, so this is the main headless risk).
7. Steam: (a) G runs next to lane-1 A+B on the same account → expect OK. (b) Quit Steam, start G alone → confirm the
   expected failure in `PCPlatform.Init` (do not patch). (c) Another PC with the same account: needs the user. With
   the generator PC in Offline Mode, start G while the other PC is in game → no `KickingOtherSession` expected.

Decision rule: **adopt headless** if B reaches the garage, generates orders, and step 5 produces damaged parts with
steady-state working set ≤ ~1.5 GB and CPU ≤ ~10 % of a core. **Fall back to D** if only B fails. Treat the
generator as **not viable** if D still needs more than ~1 GB of extra working set on top of the server, or if orders
do not generate without a player avatar.

## Recommendation

- Run the spike. It is cheap, it reuses the harness, and B/C/D answer the RAM question that desk research cannot.
- Expected outcome, scoped: use the generator client as the **highest-priority candidate of row 3's elected order
  generator**. It closes the "nobody is in the garage" gap with the game's own `GenerateNewJob`. Keep the per-part
  damage roll on the client that loads the car, with server-chosen scalars or seed, as native-decompile.md §e
  proposes. Having the generator load every customer car only adds load time and RAM.
- Do not let it replace row 16 wholesale. It only works where the server PC has the game, a Steam licence and a
  logged-in or offline-mode Steam client, and it inherits that account's DLCs. A VPS or Linux server cannot run it.
  Prices are pure formulas and should simply be ported.
- Junkyard and barn (row 15): the generator could make the canonical layout (one trip, or a re-roll through
  `GenerateNewJunkyard`) instead of "first visitor's layout". It competes with the garage for the same instance,
  though, so decide after measuring step 6.

[cli]: https://docs.unity3d.com/2020.3/Documentation/Manual/PlayerCommandLineArguments.html
[coro]: https://docs.unity3d.com/2020.3/Documentation/Manual/CLIBatchmodeCoroutines.html
[tfr]: https://docs.unity3d.com/2020.3/Documentation/ScriptReference/Application-targetFrameRate.html
[hdrp-ctor]: https://github.com/Unity-Technologies/Graphics/blob/10.x.x/release/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/HDRenderPipeline.cs
[hdrp-render]: https://github.com/Unity-Technologies/Graphics/blob/10.x.x/release/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/HDRenderPipeline.cs
[hdutils]: https://github.com/Unity-Technologies/Graphics/blob/10.x.x/release/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Utility/HDUtils.cs
[sky]: https://issuetracker.unity.com/issues/2265/skymanager-graphics-error-when-building-addressables-in-nographics-mode
[lin]: https://issuetracker-mig.prd.it.unity3d.com/issues/standalone-your-gpu-null-device-or-driver-doesnt-support-linear-rendering-error-with-batchmode-nographics
[steamapi]: https://partner.steamgames.com/doc/sdk/api
[nebula]: https://github.com/NebulaModTeam/nebula/wiki/Setup-Headless-Server
[s1]: https://xgamingserver.com/blog/schedule-1-dedicatedservermod-melonloader-setup/
[atlyss]: https://thunderstore.io/c/atlyss/p/AtlyssModding/AtlyssDedicatedServer/v/0.1.2/
[valheim]: https://dathost.com/blog/how-to-create-a-valheim-dedicated-server-10-easy-steps

Local sources: `%USERPROFILE%\CMS21-TestInstalls\native\out\il2cppdumper\dump.cs` (`OrderGenerator` TypeDef 8771,
`JunkyardGenerator` 9103, `ShedManager` 9116, `SteamManager` 8441, `PCPlatform` 10446); decompiles in
`out\clean\OrderGenerator$$Update.c`, and in the session scratchpad: `SteamManager$$Awake`,
`JunkyardGenerator$$Start/Update`, `ShedManager$$Start/Update`, the `<Generate>` coroutines,
`PCPlatform.<Init>`, `SteamDLC$$Init`. Also `tools/test-env/Setup-TestInstalls.ps1`, `TestLanes.psm1`,
`Run-Session.ps1` and `tools/TestHarness/HarnessMod.cs`.

## Runtime results (2026-10-06, `tools/test-env/Run-GeneratorSpike.ps1`, runs `20261006-140903` and `-142655`)

One instance alone, offline, loaded into the garage from the menu, then 4 minutes idle with the harness reading the
order list (`gen-probe`). Same PC as the test lanes, same Steam account (no `KickingOtherSession`).

| Variant | To the garage | Peak commit | Idle working set | Idle commit | Idle CPU (one core = 100 %) | Orders after 4 min | Graphics |
|---|---|---|---|---|---|---|---|
| normal (960x540) | 18 s | 8.7 GB | 2.2 GB | 8.6 GB | 117 % | 7 | D3D11 |
| `-batchmode -nographics` | 20 s | 2.7 GB | 2.6 GB | 2.6 GB | 157 % | 8 | Null |
| `-batchmode -nographics` + 15 fps (`lowfx`) | 14 s | 2.7 GB | 2.6 GB | 2.6 GB | **5 %** | 7 | Null |
| `-batchmode` | 14 s | 9.7 GB | 3.9 GB | 9.7 GB | 164 % | 7 | D3D11 |
| 320x180, cameras off, 15 fps | 14 s | 9.6 GB | 3.9 GB | 9.5 GB | 12 % | 8 | D3D11 |

- Headless works: HDRP switches itself off on the null device, the garage loads, and `OrderGenerator.Update` produces
  orders with no player and no camera. The 8–9 GB loading commit disappears without a graphics device.
- An uncapped headless player spins a core; `Application.targetFrameRate = 15` brings it to 5 %.
- Against the decision rule (≤ ~1.5 GB working set, ≤ ~10 % CPU): CPU passes, working set is 2.6 GB (over the target,
  but a third of a normal instance's commit).
- Not measured yet: step 5 (a taken job's damage roll headless), step 6 (junkyard/barn generation headless), step 7c
  (Steam on another PC, needs the user).
