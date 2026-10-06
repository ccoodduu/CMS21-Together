using System;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Network;

namespace TogetherTestHarness.Features;

public static class SessionCommands
{
    [HarnessCommand("stats-add")]
    private static object StatsAdd(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out int scrap) || !int.TryParse(parts[1], out int exp))
            throw new ArgumentException("usage: stats-add <scrap> <exp>");
        if (Client.Instance == null || !Client.Instance.IsConnected) throw new InvalidOperationException("not connected");

        Client.Instance.Send(new StatsActionPacket { ScrapsDelta = scrap, ExpDelta = exp });
        return $"sent scrap {scrap}, exp {exp}";
    }

    [HarnessCommand("to-menu")]
    private static object ToMenu(string args)
    {
        bool save = string.Equals((args ?? "").Trim(), "save", StringComparison.OrdinalIgnoreCase);
        if (Client.Instance != null && Client.Instance.IsConnected) Client.Instance.Disconnect();

        var center = NotificationCenter.m_instance;
        if (center == null) throw new InvalidOperationException("NotificationCenter not available");
        center.StartCoroutine(center.SelectSceneToLoad("Menu", SceneType.Menu, true, save));
        return save ? "loading menu (save)" : "loading menu";
    }
}
