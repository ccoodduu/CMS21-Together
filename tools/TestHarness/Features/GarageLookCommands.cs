using System;
using System.Collections.Generic;
using System.Linq;
using CMS.Containers;
using CMS.Garage.Customization;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Logic.Garage;
using UnityEngine;

namespace TogetherTestHarness.Features;

// shared-garage-look (row 28): drives the customisation window through the game's own entry points (the #garageLook
// click, the window's category and variation handlers, its close action) and reads sections back from the renderers.
public static class GarageLookCommands
{
    private const string ClickType = "#garageLook";

    private static GarageLookManager Manager => GarageLookManager.Instance ?? throw new InvalidOperationException("no GarageLookManager");

    private static GarageCustomizationWindow Window =>
        WindowManager.Instance?.GetWindowByID<GarageCustomizationWindow>(WindowID.GarageCustomization) ?? throw new InvalidOperationException("no garage customisation window");

    private static int[] Ints(string args, int count, string usage)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        if (parts.Length != count) throw new ArgumentException(usage);
        return parts;
    }

    [HarnessCommand("look-open")]
    private static object Open(string args)
    {
        var game = GameScript.Get() ?? throw new InvalidOperationException("no GameScript");
        string previous = game.IOMouseOverType;
        game.IOMouseOverType = ClickType;
        try { game.ClickIO(0); }
        finally { game.IOMouseOverType = previous; }
        return State();
    }

    [HarnessCommand("look-pick")]
    private static object Pick(string args)
    {
        var parts = Ints(args, 2, "usage: look-pick <section> <material> (-1 = default)");
        var window = Window;
        if (!window.isActive) throw new InvalidOperationException("the window is not open");
        var categories = window.categories ?? throw new InvalidOperationException("no categories");
        int category = -1;
        for (int k = 0; k < 128 && category < 0; k++)
            if (categories.ContainsKey(k) && categories[k] == parts[0]) category = k;
        if (category < 0) throw new ArgumentException($"section {parts[0]} is not offered in the window");
        window.OnCategoryChange(category);
        window.OnVariationChange(parts[1] + 1);
        return Read(parts[0]);
    }

    [HarnessCommand("look-close")]
    private static object Close(string args)
    {
        var window = Window;
        if (!window.isActive) throw new InvalidOperationException("the window is not open");
        window.HideAction();
        return "closing";
    }

    [HarnessCommand("look-read")]
    private static object LookRead(string args) => Read(Ints(args, 1, "usage: look-read <section>")[0]);

    [HarnessCommand("look-pack")]
    private static object Pack(string args)
    {
        string id = (args ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("usage: look-pack <id>");
        var packs = TexturePackManager.Instance ?? throw new InvalidOperationException("no TexturePackManager");
        var pack = new TexturePack { ID = id, Name = id, Author = "harness", Path = "" };
        packs.currentActiveTexturePack = pack;
        return new { current = packs.GetCurrentTexturePack().ID };
    }

    [HarnessCommand("look-apply-local")]
    private static object ApplyLocal(string args)
    {
        var sections = Manager.GetSections();
        var indexes = Enumerable.Repeat(ModGarageLook.DefaultMaterial, sections.Length).ToArray();
        foreach (string pair in (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=');
            if (kv[0] == "all") { for (int i = 0; i < indexes.Length; i++) indexes[i] = int.Parse(kv[1]); continue; }
            indexes[int.Parse(kv[0])] = int.Parse(kv[1]);
        }
        GarageLookSync.Receive(new ModGarageLook { MaterialIndexes = indexes, SectionCount = sections.Length });
        return "applying";
    }

    [HarnessCommand("look-save")]
    private static object SaveProfile(string args)
    {
        Manager.Save();
        var data = Singleton<GameManager>.Instance.GameDataManager.CurrentProfileData.garageCustomizationData;
        return new { profile = data.MaterialIndexes?.ToArray(), pack = data.CurrentTexturePack };
    }

    [HarnessCommand("look-pack-try")]
    private static object PackTry(string args)
    {
        var packs = TexturePackManager.Instance ?? throw new InvalidOperationException("no TexturePackManager");
        var errors = new List<string>();
        try { packs.SetActiveTexturePack("not-installed"); } catch (Exception e) { errors.Add($"SetActiveTexturePack: {e.Message.Split('\n')[0]}"); }
        string afterActive = packs.GetCurrentTexturePack().ID;
        try { packs.SetDefaultTexturePack(); } catch (Exception e) { errors.Add($"SetDefaultTexturePack: {e.Message.Split('\n')[0]}"); }
        return new { afterActive, afterDefault = packs.GetCurrentTexturePack().ID, defaultId = packs.defaultTexturePack.ID, installed = packs.GetTexturePacksCount(), errors };
    }

    private static object Read(int index)
    {
        var section = Manager.GetSections()[index];
        var renderers = section.RendererData;
        var names = new List<string>();
        for (int j = 0; renderers != null && j < renderers.Length && j < 3; j++) names.Add(MaterialName(renderers[j]));
        return new { section = index, name = section.Name, selected = section.SelectedMaterialIndex, materials = names, renderers = renderers?.Length ?? 0 };
    }

    private static string MaterialName(GarageLookRenderer data)
    {
        var renderer = data.Renderer;
        if (renderer == null) return null;
        var materials = renderer.sharedMaterials;
        int slot = data.MaterialNo;
        return materials != null && slot >= 0 && slot < materials.Length && materials[slot] != null ? materials[slot].name : null;
    }

    private static Dictionary<string, object> State()
    {
        var state = new Dictionary<string, object>();
        var window = WindowManager.Instance?.GetWindowByID<GarageCustomizationWindow>(WindowID.GarageCustomization);
        state["windowOpen"] = window != null && window.isActive;
        var fader = ScreenFader.Get();
        state["faded"] = fader != null && fader.isFadedIn;
        state["mode"] = GameMode.Get()?.GetCurrentMode().ToString();
        foreach (var pair in Sync()) state[pair.Key] = pair.Value;
        return state;
    }

    private static Dictionary<string, object> Sync() => new Dictionary<string, object>
    {
        ["claimHeld"] = GarageLookSync.HoldsClaim,
        ["claimPending"] = GarageLookSync.ClaimPending,
        ["applying"] = GarageLookSync.IsApplying,
        ["lastApplied"] = GarageLookSync.LastApplied == null ? null : new
        {
            indexes = GarageLookSync.LastApplied.MaterialIndexes,
            pack = GarageLookSync.LastApplied.TexturePack,
            count = GarageLookSync.LastApplied.SectionCount,
        },
        ["lastRefusal"] = GarageLookSync.LastRefusal,
        ["lastApplyMs"] = GarageLookSync.LastApplyMs,
        ["lastApplyChanged"] = GarageLookSync.LastApplyChanged,
        ["applies"] = GarageLookSync.Applies,
        ["sectionCountMismatch"] = GarageLookSync.SectionCountMismatch,
        ["pack"] = GarageLookSync.CurrentPackId(),
    };

    public static object Dump()
    {
        var manager = GarageLookManager.Instance;
        var sections = manager?.GetSections();
        if (sections == null) return null;
        var indexes = new int[sections.Length];
        var materials = new string[sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            var section = sections[i];
            indexes[i] = section.SelectedMaterialIndex;
            var renderers = section.RendererData;
            materials[i] = renderers != null && renderers.Length > 0 ? MaterialName(renderers[0]) : null;
        }
        var data = Singleton<GameManager>.Instance?.GameDataManager?.CurrentProfileData?.garageCustomizationData;
        var state = State();
        state["count"] = sections.Length;
        state["indexes"] = indexes;
        state["materials"] = materials;
        state["profileIndexes"] = data?.MaterialIndexes?.ToArray();
        state["profilePack"] = data?.CurrentTexturePack;
        return state;
    }
}
