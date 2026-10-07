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
    private const int DefaultCarPrice = 1000;
    private const float WindowWaitSeconds = 3f;
    private const float PressDelaySeconds = 0.5f;

    [HarnessCommand("outdoor-cars")]
    private static object OutdoorCars(string args) => Cars().Select((car, i) => (object)Describe(i, car)).ToList();

    [HarnessCommand("buy-car-here")]
    private static object BuyCarHere(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || parts.Length > 3 || (parts.Length == 3 && parts[2] != "parking" && parts[2] != "garage"))
            throw new ArgumentException("usage: buy-car-here <index> [price] [parking|garage]");
        var scene = GameScript.Get().CurrentSceneType;
        if (scene == SceneType.Garage) throw new InvalidOperationException("buy-car-here is for scenes outside the garage");
        var cars = Cars();
        int index = int.Parse(parts[0]);
        if (index < 0 || index >= cars.Count) throw new ArgumentException($"no car {index} here ({cars.Count} cars)");
        int price = parts.Length > 1 ? int.Parse(parts[1]) : DefaultCarPrice;
        int button = parts.Length > 2 && parts[2] == "garage" ? 0 : 1;

        var carLoader = cars[index];
        var result = Describe(index, carLoader);
        result["price"] = price;
        result["moneyBefore"] = GlobalData.PlayerMoney;
        GameScript.Get().BuyCar(carLoader, price);
        result["moneyAfter"] = GlobalData.PlayerMoney;
        result["captureOpen"] = CarPurchaseSync.IsOpen;
        MelonCoroutines.Start(Press(button));
        result["pressing"] = button == 0 ? "garage" : "parking";
        return result;
    }

    private static IEnumerator Press(int button)
    {
        float giveUp = Time.realtimeSinceStartup + WindowWaitSeconds;
        while (WindowManager.Instance == null || !WindowManager.Instance.IsWindowActive(WindowID.CarLocationWindow))
        {
            if (Time.realtimeSinceStartup > giveUp)
            {
                MelonLogger.Warning("[Harness] buy-car-here: the location window did not open");
                yield break;
            }
            yield return null;
        }
        float until = Time.realtimeSinceStartup + PressDelaySeconds;
        while (Time.realtimeSinceStartup < until) yield return null;
        var window = WindowManager.Instance?.GetWindowByID<CarLocationWindow>(WindowID.CarLocationWindow);
        var buttons = window?.buttons;
        if (buttons == null || buttons.Length <= button || buttons[button] == null)
        {
            MelonLogger.Warning($"[Harness] buy-car-here: the location window has no button {button}");
            yield break;
        }
        MelonLogger.Msg($"[Harness] buy-car-here: pressing location button {button}");
        buttons[button].OnClick.Invoke();
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
