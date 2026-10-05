using UnityEngine;

namespace TogetherTestHarness;

public static class SceneState
{
    private static readonly string[] TransitionScenes = { "init", "IntroPlayWay", "IntroRedDot", "IntroCMS", "LoadResources", "SceneLoader" };

    public static string Current { get; private set; } = "";
    public static bool Playable { get; private set; }

    private static float nextCheck;

    public static void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.25f;

        string active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (active != Current)
        {
            Current = active;
            Playable = false;
        }

        bool playable = System.Array.IndexOf(TransitionScenes, active) < 0
                        && !SceneLoader.blockProgress
                        && Object.FindObjectOfType<SceneLoader>() == null;
        if (playable && !Playable) HarnessMod.Log.Msg($"[Harness] scene {active} ready");
        Playable = playable;
    }
}
