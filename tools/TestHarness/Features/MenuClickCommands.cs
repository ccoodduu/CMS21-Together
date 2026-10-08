using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.UI;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TogetherTestHarness.Features;

// Clicks on the mod's IMGUI panels must not reach the game's uGUI underneath (a click on Host opened the CMS 2026
// news link). The probe walks a grid over every rect the panels cover, points the mod's pointer there, and records
// whether the game's EventSystem is enabled and which game UI element a click would hit.
public static class MenuClickCommands
{
    private const float Step = 30f;

    private static readonly List<Sample> samples = new List<Sample>();
    private static bool running;

    [HarnessCommand("menu-click-probe")]
    private static object Probe(string args)
    {
        if ((args ?? "").Trim() == "result")
        {
            var rows = samples.ToList();
            return new
            {
                running,
                points = rows.Count,
                enabledOver = rows.Count(r => r.Enabled),
                gameUiUnder = rows.Count(r => r.Hit != null),
                leaks = rows.Count(r => r.Enabled && r.Hit != null),
                leakExamples = rows.Where(r => r.Enabled && r.Hit != null).Take(5).Select(r => $"{r.X:0},{r.Y:0} -> {r.Hit}").ToList(),
                eventSystemEnabledAfter = Object.FindObjectOfType<EventSystem>()?.enabled
            };
        }
        if (running) throw new System.InvalidOperationException("a probe is running");
        samples.Clear();
        running = true;
        MelonCoroutines.Start(Walk(ImguiView.Covered().ToList()));
        return "started";
    }

    private sealed class Sample
    {
        public float X, Y;
        public bool Enabled;
        public string Hit;
    }

    private static IEnumerator Walk(List<Rect> rects)
    {
        var events = Object.FindObjectOfType<EventSystem>();
        try
        {
            foreach (var rect in rects)
            {
                for (float y = rect.yMin + Step / 2f; y < rect.yMax; y += Step)
                {
                    for (float x = rect.xMin + Step / 2f; x < rect.xMax; x += Step)
                    {
                        var screen = new Vector2(x, Screen.height - y);
                        PointerSource.Override = screen;
                        yield return null;
                        yield return null;
                        samples.Add(new Sample { X = x, Y = y, Enabled = events != null && events.enabled, Hit = GameUiAt(events, screen) });
                    }
                }
            }
        }
        finally
        {
            PointerSource.Override = new Vector2(-1000f, -1000f);
        }
        yield return null;
        yield return null;
        PointerSource.Override = null;
        running = false;
    }

    private static string GameUiAt(EventSystem events, Vector2 screen)
    {
        if (events == null) return null;
        var data = new PointerEventData(events) { position = screen };
        var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        events.RaycastAll(data, results);
        for (int i = 0; i < results.Count; i++)
        {
            var target = results[i].gameObject;
            for (var t = target != null ? target.transform : null; t != null; t = t.parent)
                if (t.GetComponent<UnityEngine.UI.Selectable>() != null) return t.name;
        }
        return null;
    }
}
