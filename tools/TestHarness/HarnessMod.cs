using System;
using System.IO;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(TogetherTestHarness.HarnessMod), "TogetherTestHarness", "0.1.0", "ccoodduu")]
[assembly: MelonGame("Red Dot Games", "Car Mechanic Simulator 2021")]
[assembly: MelonAdditionalDependencies("CMS21-Together")]

namespace TogetherTestHarness;

public class HarnessMod : MelonMod
{
    public static MelonLogger.Instance Log { get; private set; }
    public static string Dir { get; private set; }
    public static string InstanceName { get; private set; } = "?";
    public static bool Background { get; private set; }

    private bool mute;
    private int windowWidth;
    private int windowHeight;
    private float nextStatusWrite;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Dir = Path.Combine(MelonUtils.UserDataDirectory, "TestHarness");
        Directory.CreateDirectory(Dir);
        Features.InputGuard.Install(HarmonyInstance);
        Features.StatsGuard.Install(HarmonyInstance);
        foreach (var file in Directory.GetFiles(Dir, "reply_*.json")) File.Delete(file);
        File.Delete(Path.Combine(Dir, CommandChannel.CommandFile));

        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg == "--harness.mute")
                mute = true;
            else if (arg == "--harness.background")
                Background = true;
            else if (arg.StartsWith("--harness.name="))
                InstanceName = arg.Substring("--harness.name=".Length);
            else if (arg.StartsWith("--harness.window="))
            {
                var size = arg.Substring("--harness.window=".Length).Split('x');
                if (size.Length == 2 && int.TryParse(size[0], out var w) && int.TryParse(size[1], out var h))
                {
                    windowWidth = w;
                    windowHeight = h;
                }
            }
        }
        Log.Msg($"[Harness] instance {InstanceName}, dir {Dir}{(Background ? ", background window" : "")}");
        if (Background) Features.BackgroundWindow.Start();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) => StartupSkipper.OnSceneLoaded(sceneName);

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Log.Msg($"[Harness] scene initialized: {sceneName}");
        ApplyWindow();
    }

    public override void OnUpdate()
    {
        if (!Application.runInBackground) Application.runInBackground = true;
        if (mute && AudioListener.volume > 0f) AudioListener.volume = 0f;
        if ((Application.isBatchMode || Background) && Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (Background) Features.BackgroundWindow.Update();
        Features.InputGuard.Update();
        Features.LockTraceCommands.Update();
        Features.SeatPoseCommands.Update();

        Features.PerfCommands.RecordFrame();
        SceneState.Update();
        CommandChannel.Poll();

        if (Time.unscaledTime >= nextStatusWrite)
        {
            nextStatusWrite = Time.unscaledTime + 1f;
            StateDump.WriteStatus();
        }
    }

    public override void OnLateUpdate() => Features.SeatPoseCommands.LateUpdate();

    private void ApplyWindow()
    {
        if (windowWidth <= 0 || Application.isBatchMode || Background) return;
        if (Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == windowWidth && Screen.height == windowHeight) return;
        Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);
    }
}
