using System;
using System.Collections.Generic;
using System.Linq;
using CMS.Containers;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Data;
using CMS21Together.Logic.ShopList;

namespace TogetherTestHarness.Features;

// shared-shopping-list: the game's own shopping-list calls (UIManager.AddToShopList, ShopListWindow.RemoveFromShopList
// with entireStack like the trash button, ClearShopList) and a read of the game list and the server mirror.
// Item arguments: "<id> [tire|rim] [width=..] [size=..] [profile=..] [et=..] [plate=<name>] [bonus=<text>]".
public static class ShopListCommands
{
    [HarnessCommand("shoplist")]
    private static object Read(string args) => State();

    [HarnessCommand("shoplist-add")]
    private static object Add(string args)
    {
        var (id, extra, bonus) = Parse(args);
        var ui = UIManager.Get();
        if (ui == null) throw new InvalidOperationException("no UIManager");
        ui.AddToShopList(id, bonus, extra);
        return State();
    }

    [HarnessCommand("shoplist-remove")]
    private static object Remove(string args)
    {
        var (id, extra, _) = Parse(args);
        var window = Window();
        window.RemoveFromShopList(id, extra, true);
        if (window.IsActive) window.FillItems();
        return State();
    }

    [HarnessCommand("shoplist-clear")]
    private static object Clear(string args)
    {
        var window = Window();
        window.ClearShopList();
        if (window.IsActive) window.FillItems();
        return State();
    }

    private static ShopListWindow Window()
    {
        var ui = UIManager.Get();
        var window = ui == null ? null : ui.ShopListWindow;
        if (window == null) throw new InvalidOperationException("no shopping-list window in this scene");
        return window;
    }

    private static object State() => new
    {
        game = Rows(ShopListSync.ReadGame()),
        mirror = ShopListSync.Mirror == null ? null : Rows(ShopListSync.Mirror),
        outstanding = ShopListSync.HasOutstanding,
        windowManagerSame = SameAsWindowManager(),
    };

    private static bool SameAsWindowManager()
    {
        var ui = UIManager.Get();
        var managed = WindowManager.Instance == null ? null : WindowManager.Instance.GetWindowByID<ShopListWindow>(WindowID.ShopList);
        return ui != null && ui.ShopListWindow != null && managed != null && managed.Pointer == ui.ShopListWindow.Pointer;
    }

    private static List<string> Rows(IEnumerable<ShopListEntry> entries) => entries.Select(e => e.Describe()).ToList();

    private static (string Id, ShopListItemDataEx Extra, string Bonus) Parse(string args)
    {
        var tokens = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new ArgumentException("needs an item id");
        var extra = new ShopListItemDataEx { LicensePlateName = "" };
        string bonus = "";
        foreach (string token in tokens.Skip(1))
        {
            string[] pair = token.Split(new[] { '=' }, 2);
            string value = pair.Length > 1 ? pair[1] : "";
            switch (pair[0])
            {
                case "tire": extra.Tire = true; break;
                case "rim": extra.Rim = true; break;
                case "width": extra.Width = int.Parse(value); break;
                case "size": extra.Size = int.Parse(value); break;
                case "profile": extra.Profile = int.Parse(value); break;
                case "et": extra.ET = int.Parse(value); break;
                case "plate": extra.LicensePlate = true; extra.LicensePlateName = value; break;
                case "bonus": bonus = value; break;
                default: throw new ArgumentException($"unknown item argument '{token}'");
            }
        }
        return (tokens[0], extra, bonus);
    }
}
