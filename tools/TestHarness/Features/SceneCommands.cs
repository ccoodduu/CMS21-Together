using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using HarmonyLib;
using MelonLoader;
using UnityEngine.SceneManagement;

namespace TogetherTestHarness.Features;

[HarmonyPatch]
public static class SceneCommands
{
    private static readonly List<Action<GameScene, GameScene>> armedLeaveMarks = new List<Action<GameScene, GameScene>>();

    [HarnessCommand("travel")]
    private static object Travel(string args)
    {
        if (!Enum.TryParse((args ?? "").Trim(), true, out SceneType type) || type == SceneType.None)
            throw new ArgumentException("usage: travel <SceneType>, e.g. Junkyard or Garage");
        string sceneName = type == SceneType.Garage ? "garage" : type == SceneType.Auction ? "Auctions" : type.ToString();
        NotificationCenter.m_instance.StartSelectSceneToLoad(sceneName, type, true, false);
        return $"travelling to {sceneName} ({type})";
    }

    [HarnessCommand("scene-list")]
    private static object SceneList(string args)
    {
        var names = new List<string>();
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            names.Add(SceneUtility.GetScenePathByBuildIndex(i));
        return names;
    }

    [HarnessCommand("leave-mark")]
    private static object LeaveMark(string args)
    {
        int scrap = int.Parse((args ?? "").Trim());
        Action<CMS21_Together_Core.Data.Enum.GameScene, CMS21_Together_Core.Data.Enum.GameScene> handler = null;
        handler = (from, to) =>
        {
            ClientScene.LeavingScene -= handler;
            armedLeaveMarks.Remove(handler);
            Client.Instance.Send(new StatsActionPacket { ScrapsDelta = scrap });
            MelonLogger.Msg($"[Harness] leave-mark sent {scrap} scrap while leaving {from} for {to}");
        };
        ClientScene.LeavingScene += handler;
        armedLeaveMarks.Add(handler);
        return $"armed: {scrap} scrap on the next scene change";
    }

    internal static void Reset(List<string> changed)
    {
        if (armedLeaveMarks.Count > 0) changed.Add($"leave-mark ({armedLeaveMarks.Count} armed)");
        foreach (var handler in armedLeaveMarks) ClientScene.LeavingScene -= handler;
        armedLeaveMarks.Clear();
    }

    [HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.SelectSceneToLoad),
        typeof(string), typeof(SceneType), typeof(bool), typeof(bool))]
    [HarmonyPrefix]
    private static void TraceCoroutine(string newSceneName, SceneType sceneType, bool useFader, bool saveGame) =>
        MelonLogger.Msg($"[Harness] trace SelectSceneToLoad(4) {newSceneName} {sceneType} fader={useFader} save={saveGame}");

    [HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.SelectSceneToLoad),
        typeof(string), typeof(SceneType), typeof(bool))]
    [HarmonyPrefix]
    private static void TraceThreeArgs(string newSceneName, SceneType sceneType, bool useFader) =>
        MelonLogger.Msg($"[Harness] trace SelectSceneToLoad(3) {newSceneName} {sceneType} fader={useFader}");

    [HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.StartSelectSceneToLoad))]
    [HarmonyPrefix]
    private static void TraceStart(string newSceneName, SceneType sceneType, bool useFader, bool saveGame) =>
        MelonLogger.Msg($"[Harness] trace StartSelectSceneToLoad {newSceneName} {sceneType} fader={useFader} save={saveGame}");
}
