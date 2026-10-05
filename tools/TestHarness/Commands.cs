using System;
using System.IO;
using CMS21Together.Network;
using UnityEngine;

namespace TogetherTestHarness;

public static class Commands
{
    public static object Execute(string verb, string args)
    {
        switch (verb)
        {
            case "ping":
                return StateDump.Status();
            case "connect":
                Client.Instance.ConnectToServer(string.IsNullOrEmpty(args) ? "127.0.0.1" : args);
                return "connecting";
            case "disconnect":
                Client.Instance.Disconnect();
                return "disconnected";
            case "dump":
                return StateDump.Full();
            case "screenshot":
                if (string.IsNullOrEmpty(args)) throw new ArgumentException("screenshot needs a file path");
                Directory.CreateDirectory(Path.GetDirectoryName(args));
                ScreenCapture.CaptureScreenshot(args);
                return args;
            case "quit":
                Application.Quit();
                return "quitting";
            default:
                throw new ArgumentException($"unknown command '{verb}'");
        }
    }
}
