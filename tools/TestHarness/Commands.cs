using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CMS21Together.Network;
using UnityEngine;

namespace TogetherTestHarness;

/// <summary>
/// Marks a static method <c>object Name(string args)</c> as a harness command. Feature-specific commands
/// live in their own files under Features/ and are discovered by reflection.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class HarnessCommandAttribute : Attribute
{
    public string Verb { get; }
    public HarnessCommandAttribute(string verb) => Verb = verb;
}

public static class Commands
{
    private static Dictionary<string, Func<string, object>> handlers;

    public static object Execute(string verb, string args)
    {
        handlers ??= Discover();
        if (!handlers.TryGetValue(verb, out var handler)) throw new ArgumentException($"unknown command '{verb}'");
        return handler(args);
    }

    private static Dictionary<string, Func<string, object>> Discover()
    {
        var found = new Dictionary<string, Func<string, object>>();
        foreach (var type in typeof(Commands).Assembly.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var attribute = method.GetCustomAttribute<HarnessCommandAttribute>();
            if (attribute == null) continue;
            var handler = (Func<string, object>)Delegate.CreateDelegate(typeof(Func<string, object>), method);
            if (found.ContainsKey(attribute.Verb)) throw new InvalidOperationException($"duplicate harness command '{attribute.Verb}'");
            found[attribute.Verb] = handler;
        }
        return found;
    }

    [HarnessCommand("ping")]
    private static object Ping(string args) => StateDump.Status();

    [HarnessCommand("connect")]
    private static object Connect(string args)
    {
        Client.Instance.ConnectToServer(string.IsNullOrEmpty(args) ? "127.0.0.1" : args);
        return "connecting";
    }

    [HarnessCommand("disconnect")]
    private static object Disconnect(string args)
    {
        Client.Instance.Disconnect();
        return "disconnected";
    }

    [HarnessCommand("dump")]
    private static object Dump(string args) => StateDump.Full();

    [HarnessCommand("screenshot")]
    private static object Screenshot(string args)
    {
        if (string.IsNullOrEmpty(args)) throw new ArgumentException("screenshot needs a file path");
        Directory.CreateDirectory(Path.GetDirectoryName(args));
        ScreenCapture.CaptureScreenshot(args);
        return args;
    }

    [HarnessCommand("quit")]
    private static object Quit(string args)
    {
        Application.Quit();
        return "quitting";
    }
}
