using System;
using System.Collections.Generic;
using CMS.UI;
using CMS.UI.Windows;
using HarmonyLib;

namespace TogetherTestHarness.Features;

// shop-buy presses Buy in the shop's buy window (ShopBuyWindow.BuyItem, the method the multiplayer hook replaces);
// popup-trace records the popups and one-shot sounds the game plays meanwhile.
[HarmonyPatch]
public static class ShopBuyCommands
{
    private static bool tracing;
    private static readonly List<object> events = new List<object>();

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("popup-trace");
        tracing = false;
        events.Clear();
    }

    [HarnessCommand("shop-buy")]
    private static object ShopBuy(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) throw new ArgumentException("usage: shop-buy <itemId> [amount]");
        var manager = WindowManager.Instance ?? throw new InvalidOperationException("no WindowManager");
        string shown = "shown";
        try
        {
            if (!manager.IsWindowActive(WindowID.ShopBuy)) manager.Show(WindowID.ShopBuy, false);
        }
        catch (Exception e) { shown = $"not shown: {e.Message}"; }
        var window = manager.GetWindowByID<ShopBuyWindow>(WindowID.ShopBuy) ?? throw new InvalidOperationException("no ShopBuy window");
        window.itemID = parts[0];
        window.currentAmount = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        window.BuyItem();
        return new Dictionary<string, object>
        {
            ["id"] = parts[0], ["amount"] = window.currentAmount, ["window"] = shown, ["openAfter"] = manager.IsWindowActive(WindowID.ShopBuy),
        };
    }

    [HarnessCommand("popup-trace")]
    private static object PopupTrace(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on":
                tracing = true;
                events.Clear();
                return "tracing";
            case "off":
                tracing = false;
                return "not tracing";
            case "report":
                return new { tracing, events = new List<object>(events) };
            default:
                throw new ArgumentException("usage: popup-trace on|off|report");
        }
    }

    [HarmonyPatch(typeof(UIManager), nameof(UIManager.ShowPopup), typeof(string), typeof(string), typeof(PopupType))]
    [HarmonyPostfix]
    private static void AfterShowPopup(string title, string text, PopupType popupType)
    {
        if (tracing) events.Add(new { kind = "popup", title, text, type = popupType.ToString() });
    }

    [HarmonyPatch(typeof(SoundManager), nameof(SoundManager.PlaySFXOneShot), typeof(string))]
    [HarmonyPostfix]
    private static void AfterPlaySfxOneShot(string soundName)
    {
        if (tracing) events.Add(new { kind = "sound", name = soundName });
    }

    [HarmonyPatch(typeof(SoundManager), nameof(SoundManager.PlaySFXOneShot), typeof(string), typeof(float))]
    [HarmonyPostfix]
    private static void AfterPlaySfxOneShotScaled(string soundName)
    {
        if (tracing) events.Add(new { kind = "sound", name = soundName });
    }
}
