using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS.Garage.Customization;
using CMS21Together.Data;
using UnityEngine;

namespace TogetherTestHarness.Features;

// Spike for the single-player feature changes (docs/spikes/singleplayer-features.md): race tracks, garage look,
// bonus parts and the gearbox, read straight from the game.
public static class SpFeatureProbeCommands
{
    private static readonly Dictionary<string, (string Scene, SceneType Type)> tracks = new Dictionary<string, (string, SceneType)>(StringComparer.OrdinalIgnoreCase)
    {
        ["RaceTrack"] = ("Race_track_1", SceneType.RaceTrack),
        ["SpeedTrack"] = ("Speedtrack", SceneType.SpeedTrack),
        ["TestTrack"] = ("Test_track_1", SceneType.TestTrack),
    };

    [HarnessCommand("track-go")]
    private static object TrackGo(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !tracks.TryGetValue(parts[1], out var track)) throw new ArgumentException("usage: track-go <loader> <RaceTrack|SpeedTrack|TestTrack>");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0]));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        GlobalData.SelectedCarLoader = carLoader.gameObject.name;
        var center = NotificationCenter.m_instance;
        center.StartCoroutine(center.SelectSceneToLoad(track.Scene, track.Type, true, true));
        return new { selected = GlobalData.SelectedCarLoader, track.Scene, type = track.Type.ToString() };
    }

    [HarnessCommand("track-state")]
    private static object TrackState(string args)
    {
        var physics = UnityEngine.Object.FindObjectOfType<PrepareCarPhysics>();
        var manager = TrackManager.Instance;
        var profile = Singleton<GameManager>.Instance?.GameDataManager?.CurrentProfileData;
        var race = UnityEngine.Object.FindObjectOfType<RaceTrackManager>();
        return new Dictionary<string, object>
        {
            ["scene"] = ClientScene.LocalScene.ToString(),
            ["unityScene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            ["mode"] = GameMode.Get()?.GetCurrentMode().ToString(),
            ["manager"] = manager == null ? null : manager.GetIl2CppType().Name,
            ["physics"] = physics != null,
            ["carLoaded"] = physics != null && physics.CarLoader != null && physics.CarLoader.IsCarLoaded(),
            ["car"] = physics?.CarLoader?.carToLoad,
            ["mileage"] = physics == null ? 0f : physics.mileage,
            ["selected"] = GlobalData.SelectedCarLoader,
            ["newMileage"] = GlobalData.NewMileage,
            ["bestRaceTime"] = profile == null ? -1 : profile.BestRaceTime,
            ["topSpeed"] = profile == null ? -1 : profile.TopSpeed,
            ["laps"] = race == null ? -1 : race.laps,
            ["lastBestTime"] = race == null ? -1 : race.lastBestTime,
        };
    }

    [HarnessCommand("track-return")]
    private static object TrackReturn(string args)
    {
        var manager = TrackManager.Instance ?? throw new InvalidOperationException("no TrackManager (not on a track)");
        manager.ReturnToGarage();
        return $"returning from {manager.GetIl2CppType().Name}";
    }

    [HarnessCommand("look-probe")]
    private static object LookProbe(string args)
    {
        var manager = GarageLookManager.Instance ?? throw new InvalidOperationException("no GarageLookManager");
        var sections = manager.GetSections();
        var result = new List<object>();
        for (int i = 0; sections != null && i < sections.Length; i++)
        {
            var section = sections[i];
            var renderer = section.RendererData != null && section.RendererData.Length > 0 ? section.RendererData[0] : null;
            result.Add(new Dictionary<string, object>
            {
                ["index"] = i,
                ["name"] = section.Name,
                ["upgrade"] = section.RequiredUpgrade,
                ["upgrade2"] = section.RequiredUpgrade2,
                ["materials"] = section.ProjectMaterials?.Length ?? 0,
                ["selected"] = section.SelectedMaterialIndex,
                ["renderers"] = section.RendererData?.Length ?? 0,
                ["firstMaterial"] = renderer?.Renderer == null ? null : MaterialName(renderer.Renderer, renderer.MaterialNo),
            });
        }
        var data = Singleton<GameManager>.Instance.GameDataManager.CurrentProfileData?.garageCustomizationData;
        var packs = TexturePackManager.Instance;
        return new Dictionary<string, object>
        {
            ["sections"] = result,
            ["profileIndexes"] = data?.MaterialIndexes == null ? null : data.MaterialIndexes.ToArray(),
            ["profilePack"] = data?.CurrentTexturePack,
            ["texturePacks"] = packs == null ? -1 : packs.GetTexturePacksCount(),
            ["currentPack"] = packs == null ? null : packs.GetCurrentTexturePack().ID,
        };
    }

    [HarnessCommand("look-set")]
    private static object LookSet(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        if (parts.Length != 2) throw new ArgumentException("usage: look-set <section> <material>");
        var manager = GarageLookManager.Instance ?? throw new InvalidOperationException("no GarageLookManager");
        var section = manager.GetSections()[parts[0]];
        var renderer = section.RendererData != null && section.RendererData.Length > 0 ? section.RendererData[0] : null;
        string before = renderer?.Renderer == null ? null : MaterialName(renderer.Renderer, renderer.MaterialNo);
        manager.SetMaterialIndexForSection(parts[0], parts[1]);
        manager.UpdateMaterials(parts[0], parts[1] == 0);
        return new { section = parts[0], material = parts[1], before, selected = manager.GetSections()[parts[0]].SelectedMaterialIndex };
    }

    [HarnessCommand("look-apply")]
    private static object LookApply(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        if (parts.Length != 2) throw new ArgumentException("usage: look-apply <section> <material>");
        var manager = GarageLookManager.Instance ?? throw new InvalidOperationException("no GarageLookManager");
        var section = manager.GetSections()[parts[0]];
        var renderer = section.RendererData != null && section.RendererData.Length > 0 ? section.RendererData[0] : null;
        string before = renderer?.Renderer == null ? null : MaterialName(renderer.Renderer, renderer.MaterialNo);
        bool hadCache = manager.cachedMaterialsList != null;
        if (!hadCache) manager.CreateCachedMaterialsList();
        manager.SetMaterialIndexForSection(parts[0], parts[1]);
        string error = null;
        try { manager.UpdateMaterials(parts[0], parts[1] < 0); }
        catch (Exception e) { error = e.Message.Split('\n')[0]; }
        return new { section = parts[0], material = parts[1], hadCache, cache = manager.cachedMaterialsList?.Count ?? -1, before, error };
    }

    [HarnessCommand("stand-new")]
    private static object StandNew(string args)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((args ?? "0").Trim()));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        string id = carLoader.GetEngineName();
        var logic = ToolsManager.Get()?.EngineStandLogic ?? throw new InvalidOperationException("no engine stand");
        long money = GlobalData.PlayerMoney;
        logic.SetEngineOnEngineStand(new Item(id));
        return new { id, moneyBefore = money };
    }

    private static string MaterialName(Renderer renderer, int slot)
    {
        var materials = renderer.sharedMaterials;
        return materials != null && slot >= 0 && slot < materials.Length && materials[slot] != null ? materials[slot].name : null;
    }

    [HarnessCommand("bonus-probe")]
    private static object BonusProbe(string args)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((args ?? "0").Trim()));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        var list = carLoader.GetBonusParts();
        var result = new List<object>();
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var part = list[i];
            result.Add(new { index = i, id = part.ID, uid = part.UID, unmounted = part.IsUnmounted, dummy = part.IsDummy(), painted = part.IsPainted, paintType = part.PaintType.ToString(), handle = part.Handle == null ? null : part.Handle.name });
        }
        return new { car = carLoader.carToLoad, count = result.Count, parts = result };
    }

    [HarnessCommand("gearbox-probe")]
    private static object GearboxProbe(string args)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((args ?? "0").Trim()));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        var handle = carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>();
        return new
        {
            car = carLoader.carToLoad,
            handle = handle != null,
            path = handle == null ? null : handle.transform.parent?.name,
            ratios = handle?.gearRatio == null ? null : handle.gearRatio.ToArray().Select(r => r.ToString("0.###", CultureInfo.InvariantCulture)).ToArray(),
            finalDrive = handle == null ? 0f : handle.finalDriveRatio,
        };
    }
}
