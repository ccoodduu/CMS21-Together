using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Data;
using CMS21Together.Session;
using CMS21Together.UI;

namespace TogetherTestHarness.Features;

public static class JoinCommands
{
    [HarnessCommand("mp-join")]
    private static object Join(string args)
    {
        if (!JoinService.Join(args?.Trim(), out string error)) throw new ArgumentException(error);
        return $"joining {JoinService.CurrentTarget}";
    }

    [HarnessCommand("mp-presence")]
    private static object Presence(string args)
    {
        var (joinString, reason) = RichPresence.Build(ClientData.ServerInfo, JoinService.CurrentTarget);
        return new Dictionary<string, object> { ["joinString"] = joinString, ["reason"] = reason, ["published"] = RichPresence.JoinString, ["status"] = RichPresence.StatusText };
    }

    [HarnessCommand("mp-join-string")]
    private static object JoinString(string args)
    {
        if (!JoinService.HandleJoinString(args?.Trim(), out string error)) throw new ArgumentException(error);
        return JoinService.PendingConfirmation != null ? "waiting for confirmation" : "joining";
    }

    [HarnessCommand("mp-answer")]
    private static object Answer(string args)
    {
        bool yes = string.Equals(args?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        JoinService.Answer(yes);
        return yes ? "leaving and joining" : "staying";
    }

    [HarnessCommand("mp-status")]
    private static object Status(string args) => Session();

    [HarnessCommand("mp-fake-version")]
    private static object FakeVersion(string args)
    {
        string value = args?.Trim();
        ClientVersion.Override = string.Equals(value, "reset", StringComparison.OrdinalIgnoreCase) ? null : value;
        return $"client version {ClientVersion.Current}";
    }

    [HarnessCommand("mp-ui")]
    private static object Ui(string args)
    {
        switch ((args ?? "").Trim().ToLowerInvariant())
        {
            case "open join":
                MultiplayerMenuModel.OpenJoinPanel();
                break;
            case "close":
                MultiplayerMenuModel.Close();
                break;
            case "ok":
                MultiplayerMenuModel.AcknowledgeMessage();
                break;
            default:
                throw new ArgumentException("usage: mp-ui open join|close|ok");
        }
        return Session();
    }

    public static Dictionary<string, object> LastDisconnect() => new Dictionary<string, object>
    {
        ["reason"] = ConnectionStatus.LastReason,
        ["message"] = ConnectionStatus.Message,
    };

    public static Dictionary<string, object> Session()
    {
        var info = ClientData.ServerInfo;
        return new Dictionary<string, object>
        {
            ["joinStatus"] = ConnectionStatus.State.ToString(),
            ["lastDisconnect"] = LastDisconnect(),
            ["messagePending"] = ConnectionStatus.MessagePending,
            ["target"] = JoinService.CurrentTarget?.ToString(),
            ["serverInfo"] = info == null ? null : new Dictionary<string, object>
            {
                ["name"] = info.ServerName,
                ["modVersion"] = info.ModVersion,
                ["port"] = info.Port,
                ["maxPlayers"] = info.MaxPlayers,
                ["steamId"] = info.SteamId.ToString(),
                ["difficulty"] = info.Difficulty.ToString(),
                ["sharedDlc"] = info.SharedDlc ?? new List<string>(),
            },
            ["toasts"] = ModNotify.History.Select(t => t.Text).ToList(),
            ["panel"] = MultiplayerMenuModel.JoinPanelOpen ? "join" : "none",
            ["panelError"] = MultiplayerMenuModel.PanelError,
            ["pendingConfirmation"] = JoinService.PendingConfirmation?.ToString(),
        };
    }
}
