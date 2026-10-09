using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS.Containers;
using CMS.Salon;
using CMS.UI;
using CMS.UI.Logic;
using CMS.UI.Windows;
using CMS21Together.Logic.Economy;
using CMS21Together.Logic.Outdoor;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// Row 6 part 2 (sync-players-and-scenes 6.4): buying cars and parts outside the garage through the game's own calls.
public static class PurchaseCommands
{
    private const float WindowWaitSeconds = 3f;
    private const float PressDelaySeconds = 0.5f;

    private static readonly List<string> lastBuy = new List<string>();
    private static readonly Dictionary<string, object> lastSalon = new Dictionary<string, object>();

    [HarnessCommand("outdoor-cars")]
    private static object OutdoorCars(string args) => Cars().Select((car, i) => (object)Describe(i, car)).ToList();

    // The car info window must be open: GameScript.BuyCar hides it with force, which throws when it was never shown.
    [HarnessCommand("buy-car-here")]
    private static object BuyCarHere(string args)
    {
        const string usage = "usage: buy-car-here <index|pick:N> [price|-] [parking|garage] [direct]";
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || parts.Length > 4) throw new ArgumentException(usage);
        if (parts.Length > 2 && parts[2] != "parking" && parts[2] != "garage") throw new ArgumentException(usage);
        if (parts.Length > 3 && parts[3] != "direct") throw new ArgumentException(usage);
        if (GameScript.Get().CurrentSceneType == SceneType.Garage) throw new InvalidOperationException("buy-car-here is for scenes outside the garage");
        var cars = Cars();
        int index = parts[0].StartsWith("pick:")
            ? cars.FindIndex(car => OutdoorCarSync.IndexOf(car) == int.Parse(parts[0].Substring(5)))
            : int.Parse(parts[0]);
        if (index < 0 || index >= cars.Count) throw new ArgumentException($"no car {parts[0]} here ({cars.Count} cars)");
        int? price = parts.Length > 1 && parts[1] != "-" ? int.Parse(parts[1]) : (int?)null;
        int button = parts.Length > 2 && parts[2] == "garage" ? 0 : 1;
        bool direct = parts.Length > 3;

        var carLoader = cars[index];
        lastBuy.Clear();
        var result = Describe(index, carLoader);
        result["moneyBefore"] = GlobalData.PlayerMoney;
        result["path"] = direct ? "car info, then GameScript.BuyCar" : "car info summary tab, ask window";
        MelonCoroutines.Start(Buy(carLoader, price, button, direct));
        return result;
    }

    // shared-salon 2.1: the configurator path of the car salon. Configurator.Open shows the car list, SubmitCar opens the
    // version window for a model with several versions, the rims are set as the wizard's submit sets them, and the
    // configured car is bought like buy-car-here. Car "multi" is the first catalog model with several versions and no DLC;
    // version "other" is the one after the default, rim "other" the first rim that is not the car's original one.
    [HarnessCommand("salon-buy")]
    private static object SalonBuy(string args)
    {
        const string usage = "usage: salon-buy <carId|multi> [version|other|-] [rimId|other|-] [nobuy]";
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || parts.Length > 4 || parts.Length == 4 && parts[3] != "nobuy") throw new ArgumentException(usage);
        if (GameScript.Get().CurrentSceneType != SceneType.Salon) throw new InvalidOperationException("salon-buy needs the car salon");
        var configurator = UnityEngine.Object.FindObjectOfType<Configurator>() ?? throw new InvalidOperationException("no configurator in this scene");
        string version = parts.Length > 1 && parts[1] != "-" ? parts[1] : null;
        string rim = parts.Length > 2 && parts[2] != "-" ? parts[2] : null;
        bool buy = parts.Length < 4;
        lastBuy.Clear();
        lastSalon.Clear();
        MelonCoroutines.Start(SalonBuyRoutine(configurator, parts[0], version, rim, buy));
        return new Dictionary<string, object> { ["car"] = parts[0], ["version"] = version, ["rim"] = rim, ["buy"] = buy, ["moneyBefore"] = GlobalData.PlayerMoney };
    }

    [HarnessCommand("salon-car")]
    private static object SalonCar(string args)
    {
        var configurator = UnityEngine.Object.FindObjectOfType<Configurator>();
        var car = configurator?.CustomCar?.CarLoader;
        if (car == null) return null;
        var salon = car.SalonCarData;
        return new Dictionary<string, object>
        {
            ["carToLoad"] = car.carToLoad, ["configVersion"] = car.ConfigVersion, ["loaded"] = configurator.CustomCar.IsCarLoaded,
            ["carFrom"] = car.CarInfoData.CarFrom.ToString(), ["price"] = configurator.GetPrice(), ["basePrice"] = salon.BaseCarPrice,
            ["originalFrontRim"] = salon.OriginalFrontRim, ["selectedFrontRim"] = salon.SelectedFrontRim, ["selectedRearRim"] = salon.SelectedRearRim,
            ["frontRimsCost"] = salon.FrontRimsCost, ["rearRimsCost"] = salon.RearRimsCost,
        };
    }

    [HarnessCommand("salon-last")]
    private static object SalonLast(string args) => new Dictionary<string, object>(lastSalon);

    private static IEnumerator SalonBuyRoutine(Configurator configurator, string carId, string version, string rim, bool buy)
    {
        var manager = WindowManager.Instance;
        if (!manager.IsWindowActive(WindowID.SalonSelectCar)) configurator.Open();
        yield return null;
        var select = manager.GetWindowByID<SalonSelectCarWindow>(WindowID.SalonSelectCar);
        if (select == null || !manager.IsWindowActive(WindowID.SalonSelectCar))
        {
            Step("failed: the salon car list did not open");
            yield break;
        }
        float listGiveUp = Time.realtimeSinceStartup + WindowWaitSeconds;
        while ((select.cars == null || select.cars.Count == 0) && Time.realtimeSinceStartup < listGiveUp) yield return null;
        SalonCarConfigData data = null;
        for (int i = 0; select.cars != null && i < select.cars.Count && data == null; i++)
        {
            var candidate = select.cars[i];
            if (carId == "multi" ? candidate.AmountOfConfigs > 1 && CMS21Together.Logic.Car.CarDlc.For(candidate.CarID) == CMS21Together.Logic.Car.CarDlc.BaseGame : candidate.CarID == carId) data = candidate;
        }
        ShowroomCarItem item = null;
        for (int i = 0; select.carItems != null && i < select.carItems.Length && item == null; i++)
            if (select.carItems[i] != null) item = select.carItems[i];
        if (data == null || item == null)
        {
            var catalog = new List<string>();
            for (int i = 0; select.cars != null && i < select.cars.Count; i++)
                catalog.Add($"{select.cars[i].CarID}:{select.cars[i].AmountOfConfigs}:dlc{CMS21Together.Logic.Car.CarDlc.For(select.cars[i].CarID)}");
            Step($"failed: {carId} is not in the salon catalog or the list has no item ({item != null}); catalog: {string.Join(", ", catalog)}");
            yield break;
        }
        carId = data.CarID;
        lastSalon["car"] = carId;
        lastSalon["versions"] = data.AmountOfConfigs;
        lastSalon["defaultVersion"] = data.DefaultConfig;
        item.SetupForSalonCarConfigData(data);
        Step($"submit {carId}: {data.AmountOfConfigs} versions, default {data.DefaultConfig}, money {GlobalData.PlayerMoney}");
        select.SubmitCar(item);
        if (data.AmountOfConfigs > 1)
        {
            yield return null;
            yield return null;
            int chosen = version == null ? data.DefaultConfig : version == "other" ? (data.DefaultConfig + 1) % data.AmountOfConfigs : int.Parse(version);
            var versions = select.carVersionWindow;
            Step($"version window active {versions.gameObject.activeInHierarchy}, via WindowManager {manager.IsWindowActive(WindowID.CarVersion)}, extension {versions.carLoaderExtension != null}; choosing {chosen}");
            versions.LoadCar(chosen);
        }
        float giveUp = Time.realtimeSinceStartup + 60f;
        CustomCar custom = null;
        while (Time.realtimeSinceStartup < giveUp)
        {
            custom = configurator.CustomCar;
            if (custom != null && custom.IsCarLoaded && !custom.CarLoadingInProgress && custom.CarLoader != null && custom.CarLoader.carToLoad == carId) break;
            yield return null;
        }
        var car = custom?.CarLoader;
        if (car == null || car.carToLoad != carId || !custom.IsCarLoaded)
        {
            Step($"failed: the configurator did not load {carId}");
            yield break;
        }
        Step($"loaded {car.carToLoad} version {car.ConfigVersion}, price {configurator.GetPrice()}, original rim {car.SalonCarData.OriginalFrontRim}");
        lastSalon["version"] = car.ConfigVersion;
        lastSalon["originalRim"] = car.SalonCarData.OriginalFrontRim;

        if (rim != null)
        {
            var rims = configurator.GetItemsToWheelConfiguration(WheelConfiguration.Rim);
            ChoosePartDownItem chosen = null;
            var ids = new List<string>();
            for (int i = 0; rims != null && i < rims.Count; i++)
            {
                string id = rims[i]?.BaseItem?.ID;
                ids.Add($"{id}:{rims[i]?.Price}");
                if (chosen != null || id == null) continue;
                if (rim == "other" ? id != car.SalonCarData.OriginalFrontRim && id != car.SalonCarData.SelectedFrontRim : id == rim) chosen = rims[i];
            }
            Step($"rims: {string.Join(", ", ids)}");
            if (chosen == null)
            {
                Step($"failed: no rim {rim}");
                yield break;
            }
            string rimId = chosen.BaseItem.ID;
            foreach (bool front in new[] { true, false })
            {
                configurator.SetRim(rimId, front);
                configurator.UpdateSelectedRim(rimId, front);
                configurator.UpdateCost(WheelConfiguration.Rim, front ? Side.Front : Side.Rear, chosen.Price);
            }
            yield return null;
            lastSalon["rim"] = rimId;
            Step($"rim {rimId} ({chosen.Price} each side): selected {car.SalonCarData.SelectedFrontRim}/{car.SalonCarData.SelectedRearRim}, price {configurator.GetPrice()}");
        }
        lastSalon["configuratorPrice"] = configurator.GetPrice();
        if (!buy) yield break;
        yield return Buy(car, null, 1, false);
    }

    [HarnessCommand("buy-car-last")]
    private static object BuyCarLast(string args) => new List<string>(lastBuy);

    private static void Step(string text)
    {
        lastBuy.Add(text);
        MelonLogger.Msg($"[Harness] buy-car-here: {text}");
    }

    private static IEnumerator Buy(CarLoader carLoader, int? price, int button, bool direct)
    {
        var manager = WindowManager.Instance;
        GameScript.Get().IOMouseOverCarLoader = carLoader;
        manager.Show(WindowID.CarInfo, false);
        yield return null;
        var info = manager.GetWindowByID<CarInfoWindow>(WindowID.CarInfo);
        if (info == null || !manager.IsWindowActive(WindowID.CarInfo))
        {
            Step("failed: the car info window did not open");
            yield break;
        }
        info.EnableTab(CMS.UI.Logic.CarInfoTabs.Summary);
        yield return null;
        var summary = info.carSummaryTab;
        if (summary == null || summary.currentCarLoader == null)
        {
            Step("failed: the summary tab has no car");
            yield break;
        }
        if (price.HasValue) summary.price = price.Value;
        lastSalon["price"] = summary.price;
        Step($"car info open for {summary.currentCarLoader.carToLoad}, price {summary.price}, money {GlobalData.PlayerMoney}");

        if (direct)
        {
            GameScript.Get().BuyCar(summary.currentCarLoader, summary.price);
            Step($"GameScript.BuyCar called, capture open {CarPurchaseSync.IsOpen}, money {GlobalData.PlayerMoney}");
        }
        else
        {
            summary.BuyCar();
            yield return null;
            if (!manager.IsWindowActive(WindowID.AskWindow))
            {
                Step($"no ask window: the game refused the purchase (money {GlobalData.PlayerMoney}, info window {manager.IsWindowActive(WindowID.InfoWindow)})");
                yield break;
            }
            manager.GetWindowByID<AskWindow>(WindowID.AskWindow).AcceptAction();
            Step($"ask window accepted, capture open {CarPurchaseSync.IsOpen}, money {GlobalData.PlayerMoney}");
        }

        float giveUp = Time.realtimeSinceStartup + WindowWaitSeconds;
        while (!manager.IsWindowActive(WindowID.CarLocationWindow))
        {
            if (Time.realtimeSinceStartup > giveUp)
            {
                Step("failed: the location window did not open");
                yield break;
            }
            yield return null;
        }
        float until = Time.realtimeSinceStartup + PressDelaySeconds;
        while (Time.realtimeSinceStartup < until) yield return null;
        var buttons = manager.GetWindowByID<CarLocationWindow>(WindowID.CarLocationWindow)?.buttons;
        if (buttons == null || buttons.Length <= button || buttons[button] == null)
        {
            Step($"failed: the location window has no button {button}");
            yield break;
        }
        buttons[button].OnClick.Invoke();
        Step($"pressed {(button == 0 ? "garage" : "parking")}");
    }

    [HarnessCommand("buy-probe")]
    private static object BuyProbe(string args)
    {
        var result = new Dictionary<string, object>();
        var gameScript = GameScript.Get();
        result["gameScript"] = gameScript != null;
        result["gameMode"] = gameScript != null && gameScript.GameMode != null;
        result["scene"] = gameScript?.CurrentSceneType.ToString();
        var manager = WindowManager.Instance;
        result["windowManager"] = manager != null;
        result["uiManager"] = UIManager.Get() != null;
        if (manager != null)
        {
            var ids = new List<string>();
            foreach (var window in manager.windows) ids.Add(window == null ? "null" : window.ID.ToString());
            result["registered"] = ids;
            result["locationById"] = manager.GetWindowByID(WindowID.CarLocationWindow) != null;
            result["locationGeneric"] = manager.GetWindowByID<CarLocationWindow>(WindowID.CarLocationWindow) != null;
        }
        result["locationObjects"] = Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<CarLocationWindow>()).Select(o => o.TryCast<CarLocationWindow>())
            .Select(w => (object)$"{w.gameObject.name} active={w.gameObject.activeInHierarchy} scene={w.gameObject.scene.name} root={w.transform.root.name}").ToList();
        return result;
    }

    [HarnessCommand("junk-buy")]
    private static object JunkBuy(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || parts.Length > 2) throw new ArgumentException("usage: junk-buy <itemId|*> [count]");
        var scene = GameScript.Get().CurrentSceneType;
        if (scene != SceneType.Junkyard && scene != SceneType.Barn) throw new InvalidOperationException($"junk-buy needs the junkyard or a barn, not {scene}");
        int count = parts.Length > 1 ? int.Parse(parts[1]) : 1;

        var temp = Singleton<GameManager>.Instance.TempInventory;
        var uids = new List<long>();
        if (LootSync.SharedPiles)
        {
            foreach (var pile in LootSync.Scan())
            {
                for (int i = pile.Junk.ItemsInTrash.Count - 1; i >= 0 && uids.Count < count; i--)
                {
                    var candidate = pile.Junk.ItemsInTrash[i]?.TryCast<Item>();
                    if (candidate == null || (parts[0] != "*" && candidate.ID != parts[0])) continue;
                    uids.Add(LootSync.HarnessTake(pile.Index, pile.Key, candidate.UID, -1).UID);
                }
            }
            if (uids.Count < count) throw new InvalidOperationException($"the shared piles hold only {uids.Count} of {count} '{parts[0]}' items");
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                var item = new Item(parts[0]) { Condition = 0.5f };
                temp.AddItem(item);
                uids.Add(item.UID);
            }
        }
        var manager = WindowManager.Instance ?? throw new InvalidOperationException("no WindowManager");
        if (!manager.IsWindowActive(WindowID.TakenItems)) manager.Show(WindowID.TakenItems, false);
        var window = manager.GetWindowByID<TakenItemsWindow>(WindowID.TakenItems) ?? throw new InvalidOperationException("no TakenItems window");
        var result = new Dictionary<string, object>
        {
            ["id"] = parts[0], ["count"] = count, ["uids"] = uids, ["tempItems"] = temp.GetItemsCount(),
            ["price"] = window.priceAfterDiscount, ["moneyBefore"] = GlobalData.PlayerMoney,
        };
        window.BuyPartsAction();
        return result;
    }

    private static List<CarLoader> Cars() =>
        UnityEngine.Object.FindObjectsOfType<CarLoader>()
            .Where(car => car != null && !string.IsNullOrEmpty(car.carToLoad) && car.IsCarLoaded())
            .OrderBy(car => car.gameObject.name, StringComparer.Ordinal)
            .ThenBy(car => car.transform.position.x)
            .ThenBy(car => car.transform.position.z)
            .ToList();

    private static Dictionary<string, object> Describe(int index, CarLoader car) => new Dictionary<string, object>
    {
        ["index"] = index,
        ["name"] = car.gameObject.name,
        ["carToLoad"] = car.carToLoad,
        ["carFrom"] = car.CarInfoData.CarFrom,
        ["buyPrice"] = car.CarInfoData.BuyPrice,
        ["pick"] = OutdoorCarSync.IndexOf(car),
    };
}
