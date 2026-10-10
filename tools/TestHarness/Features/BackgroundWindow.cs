using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// --harness.background: a visible (graphics) test game keeps its window minimized and hands the foreground back to the
// window that had it before the game started, so it never keeps the user's keyboard. Unity restores and activates its
// window at start-up even when it is launched minimized.
public static class BackgroundWindow
{
    private const int MinimizedNoActivate = 7;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    private static IntPtr userWindow;
    private static float nextCheck;
    private static int minimized;
    private static int handedBack;

    public static void Start()
    {
        var foreground = GetForegroundWindow();
        if (!IsOwn(foreground)) userWindow = foreground;
        foreach (var arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith("--harness.returnfocus=") && long.TryParse(arg.Substring("--harness.returnfocus=".Length), out long window) && window != 0)
                userWindow = new IntPtr(window);
        Check();
    }

    public static void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.25f;
        Check();
    }

    private static void Check()
    {
        uint pid = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == pid && IsWindowVisible(window) && !IsIconic(window))
            {
                ShowWindow(window, MinimizedNoActivate);
                minimized++;
            }
            return true;
        }, IntPtr.Zero);
        var foreground = GetForegroundWindow();
        if (IsOwn(foreground) && userWindow != IntPtr.Zero && SetForegroundWindow(userWindow))
        {
            handedBack++;
            MelonLogger.Msg($"[Harness] background: handed the foreground back ({handedBack})");
        }
    }

    private static bool IsOwn(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint owner);
        return owner == (uint)Process.GetCurrentProcess().Id;
    }

    [HarnessCommand("background-state")]
    private static object State(string args) => new { minimized, handedBack, userWindow = userWindow.ToInt64(), foregroundIsOwn = IsOwn(GetForegroundWindow()) };
}
