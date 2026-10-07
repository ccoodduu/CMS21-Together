using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21Together.Logic.Economy;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// Row 6 part 2 (sync-players-and-scenes 6.4): buying cars and parts outside the garage through the game's own calls.
public static class PurchaseCommands
{
    private const float WindowWaitSeconds = 3f;
    private const float PressDelaySeconds = 0.5f;

    private static readonly List<string> lastBuy = new List<string>();

    [HarnessCommand("outdoor-cars")]
    private static object OutdoorCars(string args) => Cars().Select((car, i) => (object)Describe(i, car)).ToList();

    // The car info window must be open: GameScript.BuyCar hides it with force, which throws when it was never shown.
    [HarnessCommand("buy-car-here")]
    private static object BuyCarHere(string args)
    {
        const string usage = "usage: buy-car-here <index> [price|-] [parking|garage] [direct]";
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || parts.Length > 4) throw new ArgumentException(usage);
        if (parts.Length > 2 && parts[2] != "parking" && parts[2] != "garage") throw new ArgumentException(usage);
        if (parts.Length > 3 && parts[3] != "direct") throw new ArgumentException(usage);
        if (GameScript.Get().CurrentSceneType == SceneType.Garage) throw new InvalidOperationException("buy-car-here is for scenes outside the garage");
        var cars = Cars();
        int index = int.Parse(parts[0]);
        if (index < 0 || index >= cars.Count) throw new ArgumentException($"no car {index} here ({cars.Count} cars)");
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
        if (parts.Length < 1 || parts.Length > 2) throw new ArgumentException("usage: junk-buy <itemId> [count]");
        var scene = GameScript.Get().CurrentSceneType;
        if (scene != SceneType.Junkyard && scene != SceneType.Barn) throw new InvalidOperationException($"junk-buy needs the junkyard or a barn, not {scene}");
        int count = parts.Length > 1 ? int.Parse(parts[1]) : 1;

        var temp = Singleton<GameManager>.Instance.TempInventory;
        var uids = new List<long>();
        for (int i = 0; i < count; i++)
        {
            var item = new Item(parts[0]) { Condition = 0.5f };
            temp.AddItem(item);
            uids.Add(item.UID);
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
    };
}
