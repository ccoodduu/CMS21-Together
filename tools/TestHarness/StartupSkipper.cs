using System.Collections;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness;

/// <summary>
/// Skips the intro videos and the "press any key" screen so a test instance reaches the main menu without input.
/// </summary>
public static class StartupSkipper
{
    public static void OnSceneLoaded(string sceneName)
    {
        if (sceneName == "IntroPlayWay")
            UnityEngine.SceneManagement.SceneManager.LoadScene("LoadResources");
        else if (sceneName == "LoadResources")
            MelonCoroutines.Start(PressContinue());
    }

    private static IEnumerator PressContinue()
    {
        MenuPressButton button = null;
        while (button == null)
        {
            yield return new WaitForSeconds(0.2f);
            button = Object.FindObjectOfType<MenuPressButton>();
        }
        while (button != null && button.MenuState == MenuPressButtonState.Lock)
            yield return new WaitForSeconds(0.2f);
        if (button == null) yield break;

        HarnessMod.Log.Msg("[Harness] continuing past the start screen");
        button.StartCoroutine(button.LoadScene());
    }
}
